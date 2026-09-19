using OpenCareer.Application.Economy;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Finance;
using OpenCareer.Infrastructure.Economy;

namespace OpenCareer.Tests;

public sealed class AircraftAcquisitionTests : IDisposable
{
    private static readonly DateTimeOffset PurchaseTime =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static AircraftCapabilityProfile Plane =>
        new(
            "fixture-small",
            "Small test aircraft",
            AircraftCapability.Cargo,
            AircraftAccess.Civilian,
            MaximumPayloadPounds: 1_200,
            MaximumRangeNauticalMiles: 700,
            TypicalCruiseKnots: 135,
            Seats: 4,
            EngineCount: 1,
            IfrCapable: true,
            Pressurized: false,
            RetractableGear: false);

    private static CareerCreditHistory GoodHistory =>
        new(
            RealFlightHours: 70,
            CompletedJobs: 30,
            FailedJobs: 0,
            OnTimePayments: 12,
            MissedPayments: 0,
            SafetyScore: 95,
            EmployerTrust: 90,
            VerifiedMonthlyNetIncome: 10_000,
            ExistingMonthlyDebtPayments: 100,
            AvailableCash: 100_000,
            RequiredOperatingReserve: 4_000,
            UnresolvedDefault: false);

    private static DealerProfile Dealer =>
        new(
            "test-used",
            "Test Used Aircraft",
            "KRME",
            SellsNew: false,
            SellsUsed: true,
            MaximumListingPrice: 500_000m,
            MarkupRate: 0m,
            PromotionChance: 0m,
            MaximumDiscountRate: 0m);

    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.AircraftAcquisitionTests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CashPurchaseTransfersOwnershipAndMoneyExactlyOnce()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        await BootstrapAsync(
            store,
            100_000m);

        DealerStock stock =
            Stock(
                askingPrice: 60_000m,
                appraisedValue: 60_000m);

        DealerOffer offer =
            Assert.Single(
                AircraftDealer.QuoteInventory(
                    Dealer,
                    [stock],
                    GoodHistory,
                    dealerRelationship: 0m,
                    careerSeed: 42,
                    PurchaseTime));

        AircraftAcquisitionSettlement settlement =
            AircraftAcquisitionEngine.CreateCash(
                Guid.NewGuid(),
                "owned-001",
                offer,
                stock,
                GoodHistory,
                "KRME",
                PurchaseTime);

        var service =
            new AircraftAcquisitionService(store);

        AircraftAcquisitionResult first =
            await service.AcquireAsync(settlement);
        AircraftAcquisitionResult duplicate =
            await service.AcquireAsync(settlement);

        Assert.True(first.WasNewlyAcquired);
        Assert.False(duplicate.WasNewlyAcquired);
        Assert.Equal(40_000m, first.CashBalanceAfter);
        Assert.Equal(40_000m, duplicate.CashBalanceAfter);

        AircraftOwnershipRecord? ownership =
            await store.ReadAircraftOwnershipAsync(
                "owned-001");

        Assert.Equal(settlement.Ownership, ownership);
        Assert.Null(
            await store.ReadAircraftLoanAsync(
                settlement.Transaction.TransactionId));

        ActivePlayBillingState billing =
            await store.ReadActivePlayBillingStateAsync(
                "owned-001");
        Assert.Equal(0, billing.CycleIndex);
        Assert.Equal(TimeSpan.Zero, billing.CycleProgress);
    }

    [Fact]
    public async Task FinancedPurchasePostsAssetDepositAndLoanAtomically()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        await BootstrapAsync(
            store,
            100_000m);

        DealerStock stock =
            Stock(
                askingPrice: 100_000m,
                appraisedValue: 100_000m);

        DealerOffer offer =
            Assert.Single(
                AircraftDealer.QuoteInventory(
                    Dealer,
                    [stock],
                    GoodHistory,
                    dealerRelationship: 0m,
                    careerSeed: 42,
                    PurchaseTime));

        Guid acquisitionId = Guid.NewGuid();

        AircraftAcquisitionSettlement settlement =
            AircraftAcquisitionEngine.CreateFinanced(
                acquisitionId,
                "owned-financed",
                offer,
                stock,
                GoodHistory,
                InitialLenders.Community,
                deposit: 20_000m,
                termMonths: 120,
                storageIcao: "KRME",
                acquiredAt: PurchaseTime);

        AircraftAcquisitionResult result =
            await new AircraftAcquisitionService(store)
                .AcquireAsync(settlement);

        Assert.True(result.WasNewlyAcquired);
        Assert.Equal(80_000m, result.CashBalanceAfter);
        Assert.NotNull(settlement.Loan);
        Assert.Equal(
            80_000m,
            settlement.Loan!.OriginalPrincipal);

        AircraftLoanAgreement? persistedLoan =
            await store.ReadAircraftLoanAsync(
                acquisitionId);
        Assert.Equal(settlement.Loan, persistedLoan);

        AircraftOwnershipRecord? ownership =
            await store.ReadAircraftOwnershipAsync(
                "owned-financed");
        Assert.Equal(
            AircraftAcquisitionMethod.Financed,
            ownership!.AcquisitionMethod);
        Assert.Equal(acquisitionId, ownership.LoanId);

        Assert.Contains(
            settlement.Transaction.Postings,
            posting =>
                posting.Account
                    == LedgerAccountCode.AircraftAsset
                && posting.Debit == 100_000m);
        Assert.Contains(
            settlement.Transaction.Postings,
            posting =>
                posting.Account
                    == LedgerAccountCode.LoanPayable
                && posting.Credit == 80_000m);
        Assert.Equal(-20_000m, settlement.Transaction.CashChange);
    }

    [Fact]
    public async Task AuthoritativeLedgerReserveRejectsStaleAffordability()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        await BootstrapAsync(
            store,
            10_000m);

        DealerStock stock =
            Stock(
                askingPrice: 60_000m,
                appraisedValue: 60_000m);

        DealerOffer offer =
            Assert.Single(
                AircraftDealer.QuoteInventory(
                    Dealer,
                    [stock],
                    GoodHistory,
                    dealerRelationship: 0m,
                    careerSeed: 42,
                    PurchaseTime));

        AircraftAcquisitionSettlement settlement =
            AircraftAcquisitionEngine.CreateCash(
                Guid.NewGuid(),
                "owned-stale-cash",
                offer,
                stock,
                GoodHistory,
                "KRME",
                PurchaseTime);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await store.AcquireAircraftAsync(
                    settlement));

        Assert.Equal(
            10_000m,
            await store.ReadCashBalanceAsync());
        Assert.Null(
            await store.ReadAircraftOwnershipAsync(
                "owned-stale-cash"));
    }

    [Fact]
    public async Task DealerListingCannotBePurchasedTwice()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        await BootstrapAsync(
            store,
            150_000m);

        CareerCreditHistory history =
            GoodHistory with
            {
                AvailableCash = 150_000m
            };

        DealerStock stock =
            Stock(
                askingPrice: 60_000m,
                appraisedValue: 60_000m);

        DealerOffer offer =
            Assert.Single(
                AircraftDealer.QuoteInventory(
                    Dealer,
                    [stock],
                    history,
                    dealerRelationship: 0m,
                    careerSeed: 42,
                    PurchaseTime));

        AircraftAcquisitionSettlement first =
            AircraftAcquisitionEngine.CreateCash(
                Guid.NewGuid(),
                "owned-first",
                offer,
                stock,
                history,
                "KRME",
                PurchaseTime);

        AircraftAcquisitionSettlement second =
            AircraftAcquisitionEngine.CreateCash(
                Guid.NewGuid(),
                "owned-second",
                offer,
                stock,
                history,
                "KRME",
                PurchaseTime);

        await store.AcquireAircraftAsync(first);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await store.AcquireAircraftAsync(second));

        Assert.NotNull(
            await store.ReadAircraftOwnershipAsync(
                "owned-first"));
        Assert.Null(
            await store.ReadAircraftOwnershipAsync(
                "owned-second"));
        Assert.Equal(
            90_000m,
            await store.ReadCashBalanceAsync());
    }

    [Fact]
    public void MilitaryOnlyAircraftCannotEnterCivilianPurchaseSettlement()
    {
        var militaryPlane =
            Plane with
            {
                AircraftId = "F-22-fixture",
                Access = AircraftAccess.Military,
                Capabilities =
                    AircraftCapability.Military
                    | AircraftCapability.Fighter
            };

        var stock =
            new DealerStock(
                "military-listing",
                militaryPlane,
                AircraftCondition.Used,
                AskingPrice: 60_000m,
                AppraisedValue: 60_000m,
                ConditionPercent: 90m,
                CivilianSaleAuthorized: false);

        Assert.Empty(
            AircraftDealer.QuoteInventory(
                Dealer,
                [stock],
                GoodHistory,
                dealerRelationship: 0m,
                careerSeed: 42,
                PurchaseTime));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static DealerStock Stock(
        decimal askingPrice,
        decimal appraisedValue) =>
        new(
            "listing-001",
            Plane,
            AircraftCondition.Used,
            askingPrice,
            appraisedValue,
            ConditionPercent: 85m,
            CivilianSaleAuthorized: true);

    private static async Task BootstrapAsync(
        SqliteEconomyLedgerStore store,
        decimal cash)
    {
        var service =
            new CareerEconomyBootstrapService(store);

        await service.InitializeAsync(
            Guid.NewGuid(),
            cash,
            PurchaseTime.AddHours(-1));
    }
}
