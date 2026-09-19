using OpenCareer.Application.Economy;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Finance;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Economy;

namespace OpenCareer.Tests;

public sealed class AircraftLoanRepaymentPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.AircraftLoanRepaymentPersistenceTests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PartialActivePlayAccrualReducesLoanPrincipalAtomically()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        await new CareerEconomyBootstrapService(store)
            .InitializeAsync(
                Guid.NewGuid(),
                100_000m,
                Now.AddHours(-1));

        AircraftAcquisitionSettlement acquisition =
            CreateFinancedAcquisition();

        await store.AcquireAircraftAsync(
            acquisition);

        AircraftLoanAgreement loan =
            acquisition.Loan!;
        AircraftLoanAmortizationSchedule amortization =
            AircraftLoanAmortization.Build(loan);

        IReadOnlyList<RecurringOwnershipCostCycle> costs =
            AircraftLoanAmortization
                .BuildActivePlayCostSchedule(
                    amortization,
                    new OwnershipFixedCostCycle(
                        Insurance: 420m,
                        Storage: 250m),
                    cycleCount:
                        amortization.Installments.Count);

        AircraftLoanRepaymentState original =
            (await store.ReadAircraftLoanStateAsync(
                loan.LoanId))!;

        Assert.Equal(
            loan.OriginalPrincipal,
            original.RemainingPrincipal);
        Assert.Equal(0, original.Version);

        ActivePlayBillingState billingBefore =
            await store.ReadActivePlayBillingStateAsync(
                acquisition.Ownership.OwnershipId);

        PersistedActivePlayRecurringCostSettlementSummary firstHalf =
            ActivePlayRecurringCostSettlementEngine
                .CreatePersisted(
                    Guid.NewGuid(),
                    acquisition.Ownership.OwnershipId,
                    "flight-half-1",
                    billingBefore,
                    ActiveFlight(TimeSpan.FromHours(15)),
                    costs,
                    Now.AddHours(1));

        Assert.Equal(
            LedgerPostResult.Posted,
            await store.PostActivePlayRecurringCostAsync(
                firstHalf));

        AircraftLoanRepaymentState afterFirstHalf =
            (await store.ReadAircraftLoanStateAsync(
                loan.LoanId))!;

        decimal expectedHalfPrincipal =
            firstHalf.Accrual.LoanPrincipal;

        Assert.Equal(
            loan.OriginalPrincipal
                - expectedHalfPrincipal,
            afterFirstHalf.RemainingPrincipal);
        Assert.Equal(1, afterFirstHalf.Version);

        ActivePlayBillingState halfwayBilling =
            await store.ReadActivePlayBillingStateAsync(
                acquisition.Ownership.OwnershipId);

        Assert.Equal(
            TimeSpan.FromHours(15),
            halfwayBilling.CycleProgress);
        Assert.Equal(0, halfwayBilling.CycleIndex);

        PersistedActivePlayRecurringCostSettlementSummary secondHalf =
            ActivePlayRecurringCostSettlementEngine
                .CreatePersisted(
                    Guid.NewGuid(),
                    acquisition.Ownership.OwnershipId,
                    "flight-half-2",
                    halfwayBilling,
                    ActiveFlight(TimeSpan.FromHours(15)),
                    costs,
                    Now.AddHours(2));

        Assert.Equal(
            LedgerPostResult.Posted,
            await store.PostActivePlayRecurringCostAsync(
                secondHalf));

        AircraftLoanRepaymentState afterCycle =
            (await store.ReadAircraftLoanStateAsync(
                loan.LoanId))!;

        decimal firstInstallmentPrincipal =
            amortization.Installments[0].Principal;

        Assert.Equal(
            loan.OriginalPrincipal
                - firstInstallmentPrincipal,
            afterCycle.RemainingPrincipal);
        Assert.Equal(2, afterCycle.Version);

        ActivePlayBillingState completedBilling =
            await store.ReadActivePlayBillingStateAsync(
                acquisition.Ownership.OwnershipId);

        Assert.Equal(1, completedBilling.CycleIndex);
        Assert.Equal(
            TimeSpan.Zero,
            completedBilling.CycleProgress);

        decimal principalBeforeDuplicate =
            afterCycle.RemainingPrincipal;

        Assert.Equal(
            LedgerPostResult.AlreadyPosted,
            await store.PostActivePlayRecurringCostAsync(
                secondHalf));

        Assert.Equal(
            principalBeforeDuplicate,
            (await store.ReadAircraftLoanStateAsync(
                loan.LoanId))!.RemainingPrincipal);
    }

    [Fact]
    public void LoanStateCannotOverpayPrincipal()
    {
        AircraftAcquisitionSettlement acquisition =
            CreateFinancedAcquisition();

        AircraftLoanRepaymentState state =
            AircraftLoanRepaymentState.Start(
                acquisition.Loan!);

        Assert.Throws<InvalidOperationException>(
            () =>
                state.ApplyPrincipal(
                    state.RemainingPrincipal + 0.01m,
                    Now.AddHours(1)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static AircraftAcquisitionSettlement CreateFinancedAcquisition()
    {
        var aircraft =
            new AircraftCapabilityProfile(
                "fixture-small",
                "Small test aircraft",
                AircraftCapability.Cargo,
                AircraftAccess.Civilian,
                1_200,
                700,
                135,
                4,
                1,
                true,
                false,
                false);

        var stock =
            new DealerStock(
                "listing-loan",
                aircraft,
                AircraftCondition.Used,
                AskingPrice: 100_000m,
                AppraisedValue: 100_000m,
                ConditionPercent: 85m,
                CivilianSaleAuthorized: true);

        var dealer =
            new DealerProfile(
                "loan-test-dealer",
                "Loan Test Dealer",
                "KRME",
                SellsNew: false,
                SellsUsed: true,
                MaximumListingPrice: 500_000m,
                MarkupRate: 0m,
                PromotionChance: 0m,
                MaximumDiscountRate: 0m);

        CareerCreditHistory history =
            new(
                RealFlightHours: 70,
                CompletedJobs: 30,
                FailedJobs: 0,
                OnTimePayments: 12,
                MissedPayments: 0,
                SafetyScore: 95,
                EmployerTrust: 90,
                VerifiedMonthlyNetIncome: 10_000m,
                ExistingMonthlyDebtPayments: 100m,
                AvailableCash: 100_000m,
                RequiredOperatingReserve: 4_000m,
                UnresolvedDefault: false);

        DealerOffer offer =
            Assert.Single(
                AircraftDealer.QuoteInventory(
                    dealer,
                    [stock],
                    history,
                    dealerRelationship: 0m,
                    careerSeed: 42,
                    Now));

        return AircraftAcquisitionEngine.CreateFinanced(
            Guid.NewGuid(),
            "owned-loan-test",
            offer,
            stock,
            history,
            InitialLenders.Community,
            deposit: 20_000m,
            termMonths: 120,
            storageIcao: "KRME",
            acquiredAt: Now);
    }

    private static FlightTimeLedger ActiveFlight(
        TimeSpan duration) =>
        FlightTimeLedger.Empty.Add(
            new FlightTimeInterval(
                WallDuration: duration,
                SimulationRate: 1,
                ValidOperationalEvidence: true,
                Paused: false,
                SlewActive: false,
                CountsTowardBlockTime: true,
                CountsTowardFlightTime: true,
                Airborne: true,
                TaxiOut: false,
                TaxiIn: false,
                Night: false,
                ActualInstrument: false));
}
