using OpenCareer.Application.Ownership;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Finance;
using OpenCareer.Domain.Maintenance;
using OpenCareer.Domain.Ownership;
using OpenCareer.Infrastructure.Ownership;

namespace OpenCareer.Tests;

public class OwnershipPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);

    private static CareerCreditHistory History => new(
        75, 55, 1, 24, 0, 97, 94, 14_000, 250, 150_000, 5_000, false);

    private static AircraftCapabilityProfile Aircraft => new(
        "persist-light",
        "Persistent Light",
        AircraftCapability.Cargo | AircraftCapability.Training,
        AircraftAccess.Civilian,
        1_100,
        700,
        140,
        4,
        1,
        true,
        false,
        false);

    private static DealerStock Stock => new(
        "persist-listing",
        Aircraft,
        AircraftCondition.Used,
        60_000m,
        58_000m,
        85m,
        true);

    [Fact]
    public async Task CashPurchaseIsAtomicReloadableAndIdempotent()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        var offer = await fixture.SeedMarketAsync(Stock);

        var plan = AircraftPurchasePlanner.PlanCash(
            "purchase-1",
            "career-1",
            "owned-1",
            "policy-1",
            "lease-1",
            offer,
            Stock,
            History,
            fixture.Storage,
            AircraftStorageClass.Light,
            InitialAircraftInsurance.StandardHull,
            InitialMaintenancePrograms.LightAircraftFallback,
            Now);

        var first = await fixture.Store.ExecutePurchaseAsync(plan);
        var second = await fixture.Store.ExecutePurchaseAsync(plan);
        var snapshot = await fixture.Store.LoadSnapshotAsync("career-1");

        Assert.Equal(first, second);
        Assert.Single(snapshot.Aircraft);
        Assert.Empty(snapshot.Loans);
        Assert.Single(snapshot.InsurancePolicies);
        Assert.Single(snapshot.StorageLeases);
        Assert.Single(snapshot.MaintenanceStates);
        Assert.Equal(History.AvailableCash - plan.CashDebit, snapshot.Account.CashBalance);

        var duplicatePlan = plan with
        {
            OperationId = "purchase-2",
            OwnershipId = "owned-2",
            InsurancePolicyId = "policy-2",
            StorageLeaseId = "lease-2"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ExecutePurchaseAsync(duplicatePlan));
        Assert.Equal(History.AvailableCash - plan.CashDebit, (await fixture.Store.LoadSnapshotAsync("career-1")).Account.CashBalance);
    }

    [Fact]
    public async Task FinancedPurchasePersistsLoanAndFullSchedule()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        var stock = Stock with { ListingId = "finance-listing", AskingPrice = 100_000m, AppraisedValue = 100_000m };
        var offer = await fixture.SeedMarketAsync(stock);

        var plan = AircraftPurchasePlanner.PlanFinanced(
            "finance-op",
            "career-1",
            "owned-finance",
            "policy-finance",
            "lease-finance",
            "loan-finance",
            offer,
            stock,
            History,
            InitialLenders.Community,
            25_000m,
            120,
            fixture.Storage,
            AircraftStorageClass.Light,
            InitialAircraftInsurance.StandardHull,
            InitialMaintenancePrograms.LightAircraftFallback,
            Now);

        await fixture.Store.ExecutePurchaseAsync(plan);
        var snapshot = await fixture.Store.LoadSnapshotAsync("career-1");
        var schedule = await fixture.Store.LoadLoanScheduleAsync("loan-finance");

        Assert.Single(snapshot.Loans);
        Assert.NotEmpty(schedule);
        Assert.Equal(120, snapshot.Loans[0].TermMonths);
        Assert.Equal(0m, schedule[^1].RemainingPrincipal);
        Assert.Equal(snapshot.Loans[0].OriginalPrincipal, schedule.Sum(x => x.Principal));
    }

    [Fact]
    public async Task InsuranceRedoPersistsOncePerDayWithoutCarryover()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        var offer = await fixture.SeedMarketAsync(Stock);
        var plan = AircraftPurchasePlanner.PlanCash(
            "insurance-purchase",
            "career-1",
            "owned-insurance",
            "policy-insurance",
            "lease-insurance",
            offer,
            Stock,
            History,
            fixture.Storage,
            AircraftStorageClass.Light,
            InitialAircraftInsurance.StandardHull,
            InitialMaintenancePrograms.LightAircraftFallback,
            Now);
        await fixture.Store.ExecutePurchaseAsync(plan);

        var day = new DateOnly(2026, 9, 18);
        Assert.True(await fixture.Store.TryUseInsuranceRedoAsync("policy-insurance", day));
        Assert.False(await fixture.Store.TryUseInsuranceRedoAsync("policy-insurance", day));
        Assert.True(await fixture.Store.TryUseInsuranceRedoAsync("policy-insurance", day.AddDays(1)));

        var reloaded = await fixture.Store.LoadSnapshotAsync("career-1");
        Assert.Equal(day.AddDays(1), reloaded.InsurancePolicies[0].LastRedoUsedOn);
    }

    [Fact]
    public async Task MaintenanceUsageAndServiceAreIdempotentAndAffectCashOnce()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        var offer = await fixture.SeedMarketAsync(Stock);
        var plan = AircraftPurchasePlanner.PlanCash(
            "maintenance-purchase",
            "career-1",
            "owned-maint",
            "policy-maint",
            "lease-maint",
            offer,
            Stock,
            History,
            fixture.Storage,
            AircraftStorageClass.Light,
            InitialAircraftInsurance.StandardHull,
            InitialMaintenancePrograms.LightAircraftFallback,
            Now);
        await fixture.Store.ExecutePurchaseAsync(plan);

        var usage = new MaintenanceUsage(4, 4, 2, 2, 3, 0.8, 4.5, 10);
        var first = await fixture.Store.RecordMaintenanceUsageAsync(
            "owned-maint",
            "flight-usage-1",
            InitialMaintenancePrograms.LightAircraftFallback,
            usage,
            Now.AddHours(4));
        var duplicate = await fixture.Store.RecordMaintenanceUsageAsync(
            "owned-maint",
            "flight-usage-1",
            InitialMaintenancePrograms.LightAircraftFallback,
            usage,
            Now.AddHours(4));

        Assert.Equal(first, duplicate);
        Assert.True(first.DamagePercent > 0);

        var quote = AircraftMaintenanceEngine.QuoteService(
            first,
            InitialMaintenancePrograms.LightAircraftFallback,
            Now.AddHours(4));

        var beforeService = (await fixture.Store.LoadSnapshotAsync("career-1")).Account.CashBalance;
        var serviced = await fixture.Store.CompleteMaintenanceServiceAsync(
            "owned-maint",
            "service-1",
            InitialMaintenancePrograms.LightAircraftFallback,
            quote,
            Now.AddHours(8));
        var duplicateService = await fixture.Store.CompleteMaintenanceServiceAsync(
            "owned-maint",
            "service-1",
            InitialMaintenancePrograms.LightAircraftFallback,
            quote,
            Now.AddHours(8));
        var afterService = (await fixture.Store.LoadSnapshotAsync("career-1")).Account.CashBalance;

        Assert.Equal(serviced, duplicateService);
        Assert.Equal(beforeService - quote.Cost, afterService);
        Assert.Equal(0, serviced.DamagePercent);
        Assert.Equal(0, serviced.AirframeWearPercent);
    }

    [Fact]
    public async Task FailedPurchaseRollsBackCashStockAndStorage()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        var offer = await fixture.SeedMarketAsync(Stock);
        var invalidPlan = AircraftPurchasePlanner.PlanCash(
            "rollback-op",
            "career-1",
            "owned-rollback",
            "policy-rollback",
            "lease-rollback",
            offer,
            Stock,
            History,
            fixture.Storage,
            AircraftStorageClass.Light,
            InitialAircraftInsurance.StandardHull,
            InitialMaintenancePrograms.LightAircraftFallback,
            Now);

        await fixture.Store.SetCareerAccountAsync(new CareerAccountSnapshot("career-1", 6_000m, 5_000m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ExecutePurchaseAsync(invalidPlan));

        var snapshot = await fixture.Store.LoadSnapshotAsync("career-1");
        Assert.Equal(6_000m, snapshot.Account.CashBalance);
        Assert.Empty(snapshot.Aircraft);

        await fixture.Store.SetCareerAccountAsync(new CareerAccountSnapshot("career-1", History.AvailableCash, History.RequiredOperatingReserve));
        var recovered = await fixture.Store.ExecutePurchaseAsync(invalidPlan);
        Assert.Equal("owned-rollback", recovered.OwnershipId);
    }

    private sealed class StoreFixture : IAsyncDisposable
    {
        private readonly string _path;

        private StoreFixture(string path, SqliteOwnershipStore store)
        {
            _path = path;
            Store = store;
        }

        public SqliteOwnershipStore Store { get; }

        public AircraftStorageOffer Storage { get; } = new(
            "persist-slot",
            "KRME",
            AircraftStorageClass.Light,
            250m,
            true,
            Now.AddDays(1));

        public static async Task<StoreFixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"opencareer-ownership-{Guid.NewGuid():N}.db");
            var store = new SqliteOwnershipStore(path);
            await store.InitializeAsync();
            await store.SetCareerAccountAsync(new CareerAccountSnapshot(
                "career-1",
                History.AvailableCash,
                History.RequiredOperatingReserve));
            return new StoreFixture(path, store);
        }

        public async Task<DealerOffer> SeedMarketAsync(DealerStock stock)
        {
            var offer = AircraftDealer.QuoteInventory(
                InitialDealers.UsedLocal,
                [stock],
                History,
                dealerRelationship: 50m,
                careerSeed: 444,
                time: Now)[0];

            await Store.UpsertDealerStockAsync(InitialDealers.UsedLocal.Id, stock);
            await Store.SaveDealerOfferAsync(offer);
            await Store.UpsertStorageOfferAsync(Storage);
            return offer;
        }

        public ValueTask DisposeAsync()
        {
            TryDelete(_path);
            TryDelete(_path + "-wal");
            TryDelete(_path + "-shm");
            return ValueTask.CompletedTask;
        }

        private static void TryDelete(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
