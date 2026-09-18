using OpenCareer.Domain.Cargo;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Finance;
using OpenCareer.Domain.Maintenance;
using OpenCareer.Domain.Ownership;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Tests;

public class EconomyOwnershipFoundationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static CareerCreditHistory StrongHistory => new(
        70, 45, 1, 18, 0, 96, 92, 12_000, 250, 120_000, 5_000, false);

    private static AircraftCapabilityProfile Aircraft => new(
        "light-fixture",
        "Light Fixture",
        AircraftCapability.Cargo | AircraftCapability.Training,
        AircraftAccess.Civilian,
        1_200,
        650,
        135,
        4,
        1,
        true,
        false,
        false);

    private static DealerStock Stock => new(
        "listing-1",
        Aircraft,
        AircraftCondition.Used,
        60_000m,
        58_000m,
        82m,
        true);

    private static DealerOffer Offer =>
        AircraftDealer.QuoteInventory(
            InitialDealers.UsedLocal,
            [Stock],
            StrongHistory,
            dealerRelationship: 50m,
            careerSeed: 123,
            time: Now)[0];

    private static AircraftStorageOffer Storage => new(
        "slot-a",
        "KRME",
        AircraftStorageClass.Light,
        250m,
        true,
        Now.AddDays(1));

    [Fact]
    public void NamedCargoCatalogContainsRequestedRealWorldStyleGoods()
    {
        var names = InitialCommodityCatalog.All.Select(x => x.DisplayName).ToArray();
        Assert.Contains("Green coffee beans", names);
        Assert.Contains("Smartphones", names);
        Assert.Contains("Televisions", names);
        Assert.Contains("Fresh food", names);
        Assert.Contains("Live plants", names);
        Assert.All(InitialCommodityCatalog.All, x => x.Validate());
    }

    [Fact]
    public void CommodityMarketSeparatesCargoValueFromFreightPay()
    {
        var coffee = InitialCommodityCatalog.All.Single(x => x.Id == "coffee-roasted");
        var origin = NamedCargoMarket.CreateSnapshot(
            coffee,
            "KRME",
            new CommodityMarketContext(1.0, 1.2, 0.9, 1.0),
            Now);
        var destination = NamedCargoMarket.CreateSnapshot(
            coffee,
            "KBOS",
            new CommodityMarketContext(1.05, 0.75, 1.4, 1.1),
            Now);

        var lot = NamedCargoMarket.CreateLot(
            "lot-1",
            coffee,
            2_000m,
            "KRME",
            "KBOS",
            origin,
            destination,
            Now);

        var quote = NamedCargoMarket.QuoteFreight(lot, 215, 0.4, 0.2, 65);

        Assert.True(lot.DestinationMarketValue > lot.OriginMarketValue);
        Assert.NotEqual(lot.DeclaredValue, quote.FreightPay);
        Assert.True(quote.FreightPay > 0);
        Assert.True(quote.MarketSpread > 0);
    }

    [Fact]
    public void CashPurchaseIncludesStorageAndInsuranceWithoutBreakingReserve()
    {
        var plan = AircraftPurchasePlanner.PlanCash(
            "op-cash",
            "career-1",
            "owned-1",
            "policy-1",
            "lease-1",
            Offer,
            Stock,
            StrongHistory,
            Storage,
            AircraftStorageClass.Light,
            InitialAircraftInsurance.StandardHull,
            InitialMaintenancePrograms.LightAircraftFallback,
            Now);

        Assert.Equal(AircraftPurchaseMethod.Cash, plan.Method);
        Assert.Equal(Offer.SalePrice + Storage.MonthlyCost + InitialAircraftInsurance.StandardHull.MonthlyPremium, plan.CashDebit);
        Assert.Null(plan.CreateLoan());
        Assert.Equal("KRME", plan.CreateOwnedAircraft().CurrentAirportIcao);
        Assert.Equal(1, plan.CreateInsurancePolicy().Plan.DailyRedoAllowance);
    }

    [Fact]
    public void FinancingCreatesAmortizingLoanAndPreservesOperatingReserve()
    {
        var plan = AircraftPurchasePlanner.PlanFinanced(
            "op-finance",
            "career-1",
            "owned-2",
            "policy-2",
            "lease-2",
            "loan-2",
            Offer,
            Stock,
            StrongHistory,
            InitialLenders.Community,
            deposit: 20_000m,
            termMonths: 120,
            Storage,
            AircraftStorageClass.Light,
            InitialAircraftInsurance.StandardHull,
            InitialMaintenancePrograms.LightAircraftFallback,
            Now);

        var loan = plan.CreateLoan()!;
        var schedule = loan.BuildSchedule();

        Assert.Equal(AircraftPurchaseMethod.Financing, plan.Method);
        Assert.Equal(20_000m + Storage.MonthlyCost + InitialAircraftInsurance.StandardHull.MonthlyPremium, plan.CashDebit);
        Assert.NotEmpty(schedule);
        Assert.Equal(0m, schedule[^1].RemainingPrincipal);
        Assert.Equal(loan.OriginalPrincipal, schedule.Sum(x => x.Principal));
    }

    [Fact]
    public void InsuranceRedoIsAtMostOncePerRealCalendarDayAndDoesNotCarryOver()
    {
        var policy = new AircraftInsurancePolicy(
            "policy-1",
            "owned-1",
            InitialAircraftInsurance.StandardHull,
            Now,
            null,
            true);

        var day = new DateOnly(2026, 9, 18);
        Assert.True(policy.CanUseRedo(day));

        policy = policy.UseRedo(day);
        Assert.False(policy.CanUseRedo(day));
        Assert.True(policy.CanUseRedo(day.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => policy.UseRedo(day));
        Assert.False((policy with { Plan = InitialAircraftInsurance.LiabilityOnly }).CanUseRedo(day.AddDays(1)));
    }

    [Fact]
    public void IncompatibleOrExpiredStorageBlocksAcquisition()
    {
        Assert.Throws<InvalidOperationException>(() => AircraftPurchasePlanner.PlanCash(
            "op-storage",
            "career-1",
            "owned-1",
            "policy-1",
            "lease-1",
            Offer,
            Stock,
            StrongHistory,
            Storage with { StorageClass = AircraftStorageClass.Heavy },
            AircraftStorageClass.Light,
            InitialAircraftInsurance.StandardHull,
            InitialMaintenancePrograms.LightAircraftFallback,
            Now));

        Assert.Throws<ArgumentException>(() => AircraftPurchasePlanner.PlanCash(
            "op-expired",
            "career-1",
            "owned-1",
            "policy-1",
            "lease-1",
            Offer,
            Stock,
            StrongHistory,
            Storage with { ExpiresAt = Now },
            AircraftStorageClass.Light,
            InitialAircraftInsurance.StandardHull,
            InitialMaintenancePrograms.LightAircraftFallback,
            Now));
    }

    [Fact]
    public void MaintenanceWearIsDeterministicAndSeriousAbuseCanGroundAircraft()
    {
        var program = InitialMaintenancePrograms.LightAircraftFallback;
        var state = AircraftMaintenanceEngine.CreateInitial("owned-1", 100m, program, Now);

        state = AircraftMaintenanceEngine.ApplyUsage(
            state,
            program,
            new MaintenanceUsage(
                AirframeHours: 2,
                EngineHours: 2,
                LandingCycles: 1,
                OverspeedMinutes: 3,
                EngineStressMinutes: 4,
                HardLandingSeverity: 1,
                MaximumPositiveG: 5.0,
                ExcessGSeconds: 20),
            Now.AddHours(2));

        Assert.True(state.AirframeWearPercent > 0);
        Assert.True(state.EngineWearPercent > 0);
        Assert.True(state.GearWearPercent > 0);
        Assert.True(state.DamagePercent > 0);

        var quote = AircraftMaintenanceEngine.QuoteService(state, program, Now.AddHours(2));
        Assert.True(quote.Cost > 0);
        Assert.True(quote.Downtime >= program.BaseDowntime);
    }

    [Fact]
    public void RoutineServiceCannotTurnUsedAircraftIntoNewCondition()
    {
        var program = InitialMaintenancePrograms.LightAircraftFallback;
        var state = AircraftMaintenanceEngine.CreateInitial("used-1", 60m, program, Now);
        var quote = AircraftMaintenanceEngine.QuoteService(state, program, Now);
        var serviced = AircraftMaintenanceEngine.CompleteService(state, program, quote, Now.AddHours(1));

        Assert.Equal(40, serviced.BaselineWearPercent);
        Assert.Equal(40, serviced.AirframeWearPercent);
        Assert.Equal(40, serviced.EngineWearPercent);
        Assert.Equal(40, serviced.GearWearPercent);
        Assert.Equal(program.BaseInspectionCost, quote.Cost);
    }

    [Fact]
    public void MaintenanceServiceResetsTrackedWearAndAdvancesInspectionWindow()
    {
        var program = InitialMaintenancePrograms.LightAircraftFallback;
        var state = AircraftMaintenanceEngine.CreateInitial("owned-1", 80m, program, Now);
        state = AircraftMaintenanceEngine.ApplyUsage(
            state,
            program,
            new MaintenanceUsage(10, 10, 5, 0, 0, 0.2, 3.0, 0),
            Now.AddHours(10));

        var quote = AircraftMaintenanceEngine.QuoteService(state, program, Now.AddHours(10));
        var serviced = AircraftMaintenanceEngine.CompleteService(state, program, quote, Now.AddHours(12));

        Assert.Equal(20, serviced.BaselineWearPercent);
        Assert.Equal(20, serviced.AirframeWearPercent);
        Assert.Equal(20, serviced.EngineWearPercent);
        Assert.Equal(20, serviced.GearWearPercent);
        Assert.Equal(0, serviced.DamagePercent);
        Assert.Equal(serviced.TrackedAirframeHours + program.InspectionIntervalHours, serviced.NextInspectionDueAtTrackedHours);
    }
}
