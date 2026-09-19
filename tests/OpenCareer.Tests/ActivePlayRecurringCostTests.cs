using OpenCareer.Application.Economy;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Economy;

namespace OpenCareer.Tests;

public sealed class ActivePlayRecurringCostTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 20, 0, 0, TimeSpan.Zero);

    private static RecurringOwnershipCostCycle RepresentativeFinancedCosts =>
        new(
            LoanPrincipal: 240m,
            LoanInterest: 215.79m,
            Insurance: 420m,
            Storage: 250m);

    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.ActivePlayRecurringCostTests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public void ThirtyActiveHoursDefineOneBillingCycle()
    {
        var policy = ActivePlayRecurringCostPolicy.Default;

        Assert.Equal(
            TimeSpan.FromHours(30),
            policy.BillingCycleCareerCreditTime);

        ActivePlayRecurringCostAssessment assessment =
            policy.Assess(
                TimeSpan.Zero,
                TimeSpan.FromHours(30),
                RepresentativeFinancedCosts);

        Assert.True(assessment.CycleCompleted);
        Assert.Equal(RepresentativeFinancedCosts.Total, assessment.Total);
        Assert.Equal(RepresentativeFinancedCosts.LoanPrincipal, assessment.LoanPrincipal);
        Assert.Equal(RepresentativeFinancedCosts.LoanInterest, assessment.LoanInterest);
        Assert.Equal(RepresentativeFinancedCosts.Insurance, assessment.Insurance);
        Assert.Equal(RepresentativeFinancedCosts.Storage, assessment.Storage);
    }

    [Fact]
    public void ManyShortFlightsCostExactlyTheSameAsOneLongFlight()
    {
        var policy = ActivePlayRecurringCostPolicy.Default;
        var costs = RepresentativeFinancedCosts;

        ActivePlayRecurringCostAssessment longFlight =
            policy.Assess(
                TimeSpan.Zero,
                TimeSpan.FromHours(15),
                costs);

        decimal shortFlightTotal = 0m;
        TimeSpan progress = TimeSpan.Zero;

        for (var i = 0; i < 15; i++)
        {
            ActivePlayRecurringCostAssessment oneHour =
                policy.Assess(
                    progress,
                    TimeSpan.FromHours(1),
                    costs);

            shortFlightTotal += oneHour.Total;
            progress = oneHour.CycleProgressAfter;
        }

        Assert.Equal(TimeSpan.FromHours(15), progress);
        Assert.Equal(longFlight.Total, shortFlightTotal);
        Assert.Equal(562.90m, longFlight.Total);
    }

    [Fact]
    public void OneThreeAndFifteenHourSessionsRemainProportional()
    {
        var policy = ActivePlayRecurringCostPolicy.Default;
        var costs = RepresentativeFinancedCosts;

        decimal oneHour =
            policy.Assess(
                TimeSpan.Zero,
                TimeSpan.FromHours(1),
                costs).Total;
        decimal threeHours =
            policy.Assess(
                TimeSpan.Zero,
                TimeSpan.FromHours(3),
                costs).Total;
        decimal fifteenHours =
            policy.Assess(
                TimeSpan.Zero,
                TimeSpan.FromHours(15),
                costs).Total;

        Assert.InRange(oneHour, 37.50m, 37.55m);
        Assert.InRange(threeHours, 112.55m, 112.60m);
        Assert.Equal(562.90m, fifteenHours);

        Assert.InRange(threeHours / 3m, oneHour - 0.02m, oneHour + 0.02m);
        Assert.InRange(fifteenHours / 15m, oneHour - 0.02m, oneHour + 0.02m);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(15)]
    public void FixedOwnershipBurdenDoesNotRequireMarathonFlights(int sessionHours)
    {
        var policy = ActivePlayRecurringCostPolicy.Default;
        decimal recurring =
            policy.Assess(
                TimeSpan.Zero,
                TimeSpan.FromHours(sessionHours),
                RepresentativeFinancedCosts).Total;

        const decimal routineFiftyHourMaintenance = 575m;
        decimal maintenanceReserve =
            routineFiftyHourMaintenance * sessionHours / 50m;
        decimal targetSavings =
            CareerProgressionPolicy.Default.TargetNetSavingsPerFlightHour
            * sessionHours;

        decimal ownershipBurden = recurring + maintenanceReserve;

        Assert.True(
            ownershipBurden <= targetSavings * 0.06m,
            $"{sessionHours}h ownership burden {ownershipBurden:C} exceeded 6% of target savings {targetSavings:C}.");
    }

    [Fact]
    public void TimeAccelerationCannotIncreaseRecurringOwnershipCost()
    {
        FlightTimeLedger normal =
            FlightTimeLedger.Empty.Add(
                OperationalInterval(
                    TimeSpan.FromHours(1),
                    simulationRate: 1));

        FlightTimeLedger accelerated =
            FlightTimeLedger.Empty.Add(
                OperationalInterval(
                    TimeSpan.FromHours(1),
                    simulationRate: 4));

        ActivePlayRecurringCostSettlementSummary normalSettlement =
            ActivePlayRecurringCostSettlementEngine.Create(
                Guid.NewGuid(),
                "owned-1",
                "normal-flight",
                TimeSpan.Zero,
                normal,
                RepresentativeFinancedCosts,
                Now);

        ActivePlayRecurringCostSettlementSummary acceleratedSettlement =
            ActivePlayRecurringCostSettlementEngine.Create(
                Guid.NewGuid(),
                "owned-1",
                "accelerated-flight",
                TimeSpan.Zero,
                accelerated,
                RepresentativeFinancedCosts,
                Now);

        Assert.Equal(TimeSpan.FromHours(1), normal.CareerCreditTime);
        Assert.Equal(TimeSpan.FromHours(1), accelerated.CareerCreditTime);
        Assert.Equal(TimeSpan.FromHours(4), accelerated.MovementFlightTime);
        Assert.Equal(normalSettlement.TotalCost, acceleratedSettlement.TotalCost);
    }

    [Fact]
    public void PauseSlewAndProtectedAbsenceDoNotCreateOwnershipBills()
    {
        FlightTimeLedger inactive =
            FlightTimeLedger.Empty
                .Add(
                    OperationalInterval(
                        TimeSpan.FromHours(5),
                        simulationRate: 1) with
                    {
                        Paused = true
                    })
                .Add(
                    OperationalInterval(
                        TimeSpan.FromHours(5),
                        simulationRate: 1) with
                    {
                        SlewActive = true
                    });

        ActivePlayRecurringCostSettlementSummary settlement =
            ActivePlayRecurringCostSettlementEngine.Create(
                Guid.NewGuid(),
                "owned-1",
                "inactive-session",
                TimeSpan.Zero,
                inactive,
                RepresentativeFinancedCosts,
                Now);

        OfflineLiabilityAssessment absence =
            OfflineLiabilityPolicy.Default.Assess(
                Now,
                Now.AddDays(180),
                RepresentativeFinancedCosts.Total,
                hasPassiveOperations: false);

        Assert.Equal(TimeSpan.Zero, inactive.CareerCreditTime);
        Assert.Equal(0m, settlement.TotalCost);
        Assert.Empty(settlement.Transaction.Postings);
        Assert.Equal(0m, absence.AccruedFixedLiabilities);
        Assert.Equal(absence.Elapsed, absence.ProtectedDuration);
    }

    [Fact]
    public void CrossingBillingCycleRequiresExplicitSplit()
    {
        var policy = ActivePlayRecurringCostPolicy.Default;

        Assert.Throws<InvalidOperationException>(
            () => policy.Assess(
                TimeSpan.FromHours(29),
                TimeSpan.FromHours(2),
                RepresentativeFinancedCosts));

        ActivePlayRecurringCostAssessment finalHour =
            policy.Assess(
                TimeSpan.FromHours(29),
                TimeSpan.FromHours(1),
                RepresentativeFinancedCosts);

        Assert.True(finalHour.CycleCompleted);
        Assert.Equal(
            RepresentativeFinancedCosts.Total,
            policy.Assess(
                TimeSpan.Zero,
                TimeSpan.FromHours(30),
                RepresentativeFinancedCosts).Total);
    }

    [Fact]
    public async Task LedgerSettlementIsExactlyOnceForSameActivity()
    {
        string databasePath =
            Path.Combine(_directory, "career.db");

        var store =
            new SqliteEconomyLedgerStore(databasePath);

        await store.PostAsync(
            new EconomyLedgerTransaction(
                Guid.NewGuid(),
                "opening-balance-active-play-test",
                Now.AddMinutes(-1),
                "Opening balance",
                "Test",
                "opening",
                [
                    LedgerPosting.DebitTo(
                        LedgerAccountCode.Cash,
                        1_000m,
                        "Opening cash"),
                    LedgerPosting.CreditTo(
                        LedgerAccountCode.ContractRevenue,
                        1_000m,
                        "Opening equity fixture")
                ]));

        var service =
            new ActivePlayRecurringCostService(store);

        Guid settlementId = Guid.NewGuid();
        FlightTimeLedger oneHour =
            FlightTimeLedger.Empty.Add(
                OperationalInterval(
                    TimeSpan.FromHours(1),
                    simulationRate: 1));

        ActivePlayRecurringCostSettlementResult first =
            await service.SettleAsync(
                settlementId,
                "owned-1",
                "flight-001",
                TimeSpan.Zero,
                oneHour,
                RepresentativeFinancedCosts,
                Now);

        ActivePlayRecurringCostSettlementResult duplicate =
            await service.SettleAsync(
                settlementId,
                "owned-1",
                "flight-001",
                TimeSpan.Zero,
                oneHour,
                RepresentativeFinancedCosts,
                Now);

        Assert.True(first.WasNewlyPosted);
        Assert.False(duplicate.WasNewlyPosted);
        Assert.Equal(first.CashBalanceAfter, duplicate.CashBalanceAfter);
        Assert.Equal(
            1_000m - first.Settlement.TotalCost,
            first.CashBalanceAfter);
    }

    [Fact]
    public void PersistedEngineCrossesCycleBoundaryUsingNextCostCycle()
    {
        var schedule = new[]
        {
            RepresentativeFinancedCosts,
            RepresentativeFinancedCosts with
            {
                LoanPrincipal = 250m,
                LoanInterest = 205.79m
            }
        };
        var state = new ActivePlayBillingState(
            "owned-cross",
            CycleIndex: 0,
            CycleProgress: TimeSpan.FromHours(29),
            Version: 7,
            UpdatedAt: Now.AddHours(-1));
        FlightTimeLedger threeHours =
            FlightTimeLedger.Empty.Add(
                OperationalInterval(
                    TimeSpan.FromHours(3),
                    simulationRate: 1));

        PersistedActivePlayRecurringCostSettlementSummary settlement =
            ActivePlayRecurringCostSettlementEngine.CreatePersisted(
                Guid.NewGuid(),
                "owned-cross",
                "cross-boundary-flight",
                state,
                threeHours,
                schedule,
                Now);

        Assert.Equal(1, settlement.Accrual.CompletedCycles);
        Assert.Equal(1, settlement.StateAfter.CycleIndex);
        Assert.Equal(TimeSpan.FromHours(2), settlement.StateAfter.CycleProgress);
        Assert.Equal(8, settlement.StateAfter.Version);

        decimal expected =
            ActivePlayRecurringCostPolicy.Default.Assess(
                TimeSpan.FromHours(29),
                TimeSpan.FromHours(1),
                schedule[0]).Total
            + ActivePlayRecurringCostPolicy.Default.Assess(
                TimeSpan.Zero,
                TimeSpan.FromHours(2),
                schedule[1]).Total;
        Assert.Equal(expected, settlement.TotalCost);
    }

    [Fact]
    public async Task BillingProgressAndLedgerSurviveRestartWithoutResettingShortFlights()
    {
        string databasePath =
            Path.Combine(_directory, "persistent-career.db");
        var schedule = Enumerable
            .Repeat(RepresentativeFinancedCosts, 4)
            .ToArray();

        var firstStore = new SqliteEconomyLedgerStore(databasePath);
        await firstStore.PostAsync(
            new EconomyLedgerTransaction(
                Guid.NewGuid(),
                "opening-balance-persisted-active-play",
                Now.AddMinutes(-1),
                "Opening balance",
                "Test",
                "opening-persisted",
                [
                    LedgerPosting.DebitTo(LedgerAccountCode.Cash, 2_000m, "Opening cash"),
                    LedgerPosting.CreditTo(LedgerAccountCode.ContractRevenue, 2_000m, "Opening fixture")
                ]));

        ActivePlayBillingState startingState =
            await firstStore.ReadActivePlayBillingStateAsync("owned-persisted");
        PersistedActivePlayRecurringCostSettlementSummary firstSettlement =
            ActivePlayRecurringCostSettlementEngine.CreatePersisted(
                Guid.NewGuid(),
                "owned-persisted",
                "one-hour-flight",
                startingState,
                FlightTimeLedger.Empty.Add(
                    OperationalInterval(TimeSpan.FromHours(1), 1)),
                schedule,
                Now);

        Assert.Equal(
            LedgerPostResult.Posted,
            await firstStore.PostActivePlayRecurringCostAsync(firstSettlement));
        Assert.Equal(
            LedgerPostResult.AlreadyPosted,
            await firstStore.PostActivePlayRecurringCostAsync(firstSettlement));

        var reopenedStore = new SqliteEconomyLedgerStore(databasePath);
        ActivePlayBillingState reopenedState =
            await reopenedStore.ReadActivePlayBillingStateAsync("owned-persisted");
        Assert.Equal(TimeSpan.FromHours(1), reopenedState.CycleProgress);
        Assert.Equal(1, reopenedState.Version);

        var service = new ActivePlayRecurringCostService(reopenedStore);
        PersistedActivePlayRecurringCostSettlementResult second =
            await service.SettlePersistedAsync(
                Guid.NewGuid(),
                "owned-persisted",
                "two-hour-flight",
                FlightTimeLedger.Empty.Add(
                    OperationalInterval(TimeSpan.FromHours(2), 1)),
                schedule,
                Now.AddHours(2));

        Assert.True(second.WasNewlyPosted);
        Assert.Equal(TimeSpan.FromHours(3), second.CurrentBillingState.CycleProgress);
        Assert.Equal(2, second.CurrentBillingState.Version);

        decimal expectedThreeHourCost =
            ActivePlayRecurringCostPolicy.Default.Assess(
                TimeSpan.Zero,
                TimeSpan.FromHours(3),
                RepresentativeFinancedCosts).Total;
        Assert.Equal(
            2_000m - expectedThreeHourCost,
            second.CashBalanceAfter);

        PersistedActivePlayRecurringCostSettlementResult retry =
            await service.SettlePersistedAsync(
                Guid.NewGuid(),
                "owned-persisted",
                "two-hour-flight",
                FlightTimeLedger.Empty.Add(
                    OperationalInterval(TimeSpan.FromHours(2), 1)),
                schedule,
                Now.AddHours(2));

        Assert.False(retry.WasNewlyPosted);
        Assert.Null(retry.Settlement);
        Assert.Equal(second.Transaction, retry.Transaction);
        Assert.Equal(second.CashBalanceAfter, retry.CashBalanceAfter);
        Assert.Equal(second.CurrentBillingState, retry.CurrentBillingState);
    }

    [Fact]
    public async Task StaleBillingStateCannotDoubleChargeOrOverwriteProgress()
    {
        string databasePath =
            Path.Combine(_directory, "stale-state.db");
        var store = new SqliteEconomyLedgerStore(databasePath);
        var schedule = Enumerable
            .Repeat(RepresentativeFinancedCosts, 2)
            .ToArray();
        ActivePlayBillingState initial =
            await store.ReadActivePlayBillingStateAsync("owned-stale");
        FlightTimeLedger oneHour =
            FlightTimeLedger.Empty.Add(
                OperationalInterval(TimeSpan.FromHours(1), 1));

        PersistedActivePlayRecurringCostSettlementSummary first =
            ActivePlayRecurringCostSettlementEngine.CreatePersisted(
                Guid.NewGuid(),
                "owned-stale",
                "flight-a",
                initial,
                oneHour,
                schedule,
                Now);
        PersistedActivePlayRecurringCostSettlementSummary staleSecond =
            ActivePlayRecurringCostSettlementEngine.CreatePersisted(
                Guid.NewGuid(),
                "owned-stale",
                "flight-b",
                initial,
                oneHour,
                schedule,
                Now.AddMinutes(5));

        Assert.Equal(
            LedgerPostResult.Posted,
            await store.PostActivePlayRecurringCostAsync(first));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.PostActivePlayRecurringCostAsync(staleSecond));

        ActivePlayBillingState currentState =
            await store.ReadActivePlayBillingStateAsync("owned-stale");
        Assert.Equal(TimeSpan.FromHours(1), currentState.CycleProgress);
        Assert.Equal(1, currentState.Version);
        Assert.Single(await store.ReadRecentAsync(10));
    }

    [Fact]
    public void PrincipalReducesLoanPayableAndFixedCostsUseExpenseAccounts()
    {
        FlightTimeLedger fifteenHours =
            FlightTimeLedger.Empty.Add(
                OperationalInterval(
                    TimeSpan.FromHours(15),
                    simulationRate: 1));

        ActivePlayRecurringCostSettlementSummary settlement =
            ActivePlayRecurringCostSettlementEngine.Create(
                Guid.NewGuid(),
                "owned-1",
                "flight-015",
                TimeSpan.Zero,
                fifteenHours,
                RepresentativeFinancedCosts,
                Now);

        Assert.Contains(
            settlement.Transaction.Postings,
            posting =>
                posting.Account == LedgerAccountCode.LoanPayable
                && posting.Debit == 120m);
        Assert.Contains(
            settlement.Transaction.Postings,
            posting =>
                posting.Account == LedgerAccountCode.InterestExpense
                && posting.Debit == 107.90m);
        Assert.Contains(
            settlement.Transaction.Postings,
            posting =>
                posting.Account == LedgerAccountCode.InsuranceExpense
                && posting.Debit == 210m);
        Assert.Contains(
            settlement.Transaction.Postings,
            posting =>
                posting.Account == LedgerAccountCode.StorageExpense
                && posting.Debit == 125m);
        Assert.Equal(-562.90m, settlement.Transaction.CashChange);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static FlightTimeInterval OperationalInterval(
        TimeSpan duration,
        double simulationRate) =>
        new(
            WallDuration: duration,
            SimulationRate: simulationRate,
            ValidOperationalEvidence: true,
            Paused: false,
            SlewActive: false,
            CountsTowardBlockTime: true,
            CountsTowardFlightTime: true,
            Airborne: true,
            TaxiOut: false,
            TaxiIn: false,
            Night: false,
            ActualInstrument: false);
}
