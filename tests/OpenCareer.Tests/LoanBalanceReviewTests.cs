using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Finance;

namespace OpenCareer.Tests;

public sealed class LoanBalanceReviewTests
{
    private static CareerCreditHistory CasualQualifiedHistory =>
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

    [Fact]
    public void RepresentativeStarterFinancingKeepsActiveHourBurdenLow()
    {
        LoanDecision decision =
            CareerCredit.Evaluate(
                CasualQualifiedHistory,
                InitialLenders.Community,
                new AircraftLoanRequest(
                    Price: 100_000m,
                    AppraisedValue: 100_000m,
                    Deposit: 20_000m,
                    TermMonths: 120,
                    CivilianOwnershipEligible: true));

        Assert.True(decision.Approved);
        Assert.Equal(80_000m, decision.RequestedPrincipal);

        var agreement = new AircraftLoanAgreement(
            Guid.NewGuid(),
            "owned-review",
            InitialLenders.Community.Id,
            decision.RequestedPrincipal,
            decision.AnnualRate,
            120,
            decision.MonthlyPayment,
            DateTimeOffset.UnixEpoch);

        AircraftLoanAmortizationSchedule amortization =
            AircraftLoanAmortization.Build(agreement);

        IReadOnlyList<RecurringOwnershipCostCycle> costs =
            AircraftLoanAmortization.BuildActivePlayCostSchedule(
                amortization,
                new OwnershipFixedCostCycle(
                    Insurance: 420m,
                    Storage: 250m),
                cycleCount: amortization.Installments.Count);

        const decimal routineFiftyHourMaintenance = 575m;
        decimal maintenancePerThirtyHours =
            routineFiftyHourMaintenance * 30m / 50m;

        decimal burdenPerActiveHour =
            (costs[0].Total + maintenancePerThirtyHours) / 30m;

        Assert.InRange(burdenPerActiveHour, 60m, 70m);
        Assert.True(
            burdenPerActiveHour
            <= CareerProgressionPolicy.Default.TargetNetSavingsPerFlightHour * 0.08m);
    }

    [Fact]
    public void OneHourAndFifteenHourSessionShapesProduceSameLoanCycleCost()
    {
        RecurringOwnershipCostCycle cost =
            RepresentativeFirstCycle();

        decimal thirtyOneHourFlights =
            AccrueSessionShape(
                Enumerable.Repeat(TimeSpan.FromHours(1), 30),
                cost);

        decimal tenThreeHourFlights =
            AccrueSessionShape(
                Enumerable.Repeat(TimeSpan.FromHours(3), 10),
                cost);

        decimal fiveSixHourFlights =
            AccrueSessionShape(
                Enumerable.Repeat(TimeSpan.FromHours(6), 5),
                cost);

        decimal twoFifteenHourFlights =
            AccrueSessionShape(
                Enumerable.Repeat(TimeSpan.FromHours(15), 2),
                cost);

        Assert.Equal(cost.Total, thirtyOneHourFlights);
        Assert.Equal(cost.Total, tenThreeHourFlights);
        Assert.Equal(cost.Total, fiveSixHourFlights);
        Assert.Equal(cost.Total, twoFifteenHourFlights);
    }

    [Fact]
    public void ShortJobFrequencyDoesNotCreateLargeLoanPricingAdvantage()
    {
        CareerCreditHistory oneHourStyle =
            CasualQualifiedHistory with
            {
                RealFlightHours = 60,
                CompletedJobs = 60,
                FailedJobs = 2,
                OnTimePayments = 6
            };

        CareerCreditHistory threeHourStyle =
            CasualQualifiedHistory with
            {
                RealFlightHours = 60,
                CompletedJobs = 20,
                FailedJobs = 1,
                OnTimePayments = 6
            };

        AircraftLoanRequest request =
            new(
                Price: 100_000m,
                AppraisedValue: 100_000m,
                Deposit: 20_000m,
                TermMonths: 120,
                CivilianOwnershipEligible: true);

        LoanDecision shortSessions =
            CareerCredit.Evaluate(
                oneHourStyle,
                InitialLenders.Community,
                request);

        LoanDecision longerSessions =
            CareerCredit.Evaluate(
                threeHourStyle,
                InitialLenders.Community,
                request);

        Assert.True(shortSessions.Approved);
        Assert.True(longerSessions.Approved);
        Assert.InRange(
            Math.Abs(shortSessions.CreditScore - longerSessions.CreditScore),
            0,
            15);
        Assert.True(
            Math.Abs(shortSessions.AnnualRate - longerSessions.AnnualRate)
            < 0.002m);
        Assert.True(
            Math.Abs(shortSessions.MonthlyPayment - longerSessions.MonthlyPayment)
            < 10m);
    }

    [Fact]
    public void RepresentativeCommunityLoanMatchesIndependentReference()
    {
        LoanDecision decision =
            CareerCredit.Evaluate(
                CasualQualifiedHistory,
                InitialLenders.Community,
                new AircraftLoanRequest(
                    Price: 100_000m,
                    AppraisedValue: 100_000m,
                    Deposit: 20_000m,
                    TermMonths: 120,
                    CivilianOwnershipEligible: true));

        Assert.True(decision.Approved);
        Assert.Equal(785, decision.CreditScore);
        Assert.Equal(
            0.0670909090909090909090909091m,
            decision.AnnualRate);
        Assert.Equal(916.92m, decision.MonthlyPayment);

        var agreement = new AircraftLoanAgreement(
            Guid.NewGuid(),
            "owned-reference",
            InitialLenders.Community.Id,
            decision.RequestedPrincipal,
            decision.AnnualRate,
            120,
            decision.MonthlyPayment,
            DateTimeOffset.UnixEpoch);

        AircraftLoanAmortizationSchedule schedule =
            AircraftLoanAmortization.Build(agreement);

        Assert.Equal(80_000m, schedule.TotalPrincipal);
        Assert.Equal(30_030.06m, schedule.TotalInterest);
        Assert.Equal(110_030.06m, schedule.TotalPayments);
        Assert.Equal(0m, schedule.Installments[^1].ClosingPrincipal);
        Assert.Equal(916.58m, schedule.Installments[^1].Payment);
    }

    [Fact]
    public void LongRealWorldAbsenceDoesNotCreateLoanOrFixedCostCatchUpBill()
    {
        RecurringOwnershipCostCycle cost =
            RepresentativeFirstCycle();

        OfflineLiabilityAssessment absence =
            OfflineLiabilityPolicy.Default.Assess(
                new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 9, 19, 0, 0, 0, TimeSpan.Zero),
                cost.Total,
                hasPassiveOperations: false);

        Assert.Equal(0m, absence.AccruedFixedLiabilities);
        Assert.Equal(absence.Elapsed, absence.ProtectedDuration);
    }

    [Fact]
    public void MaximumSupportedRateAndTermRemainFiniteAndAmortizeToZero()
    {
        var stressLender = new LenderProfile(
            "stress",
            "Stress Fixture",
            MinimumScore: 300,
            BaseAnnualRate: 0.50m,
            MaximumRiskPremium: 0m,
            MaximumLoanToValue: 1m,
            MaximumDebtServiceRatio: 1m,
            MaximumPrincipal: 1_000_000m,
            MaximumTermMonths: 240);

        CareerCreditHistory history =
            CasualQualifiedHistory with
            {
                VerifiedMonthlyNetIncome = 1_000_000m,
                ExistingMonthlyDebtPayments = 0m,
                AvailableCash = 10_000m,
                RequiredOperatingReserve = 0m
            };

        LoanDecision decision =
            CareerCredit.Evaluate(
                history,
                stressLender,
                new AircraftLoanRequest(
                    Price: 100_001m,
                    AppraisedValue: 100_001m,
                    Deposit: 1m,
                    TermMonths: 240,
                    CivilianOwnershipEligible: true));

        Assert.True(decision.Approved);
        Assert.Equal(0.50m, decision.AnnualRate);
        Assert.True(decision.MonthlyPayment > 0m);

        var agreement = new AircraftLoanAgreement(
            Guid.NewGuid(),
            "owned-stress",
            stressLender.Id,
            decision.RequestedPrincipal,
            decision.AnnualRate,
            240,
            decision.MonthlyPayment,
            DateTimeOffset.UnixEpoch);

        AircraftLoanAmortizationSchedule schedule =
            AircraftLoanAmortization.Build(agreement);

        Assert.Equal(240, schedule.Installments.Count);
        Assert.Equal(100_000m, schedule.TotalPrincipal);
        Assert.Equal(0m, schedule.Installments[^1].ClosingPrincipal);
        Assert.All(
            schedule.Installments,
            installment => Assert.True(installment.Payment > 0m));
    }

    [Fact]
    public void AffordabilityInputMustBeNormalizedBeforeCallingCreditEngine()
    {
        AircraftLoanRequest request =
            new(
                Price: 100_000m,
                AppraisedValue: 100_000m,
                Deposit: 20_000m,
                TermMonths: 120,
                CivilianOwnershipEligible: true);

        LoanDecision activeCycleIncome =
            CareerCredit.Evaluate(
                CasualQualifiedHistory with
                {
                    VerifiedMonthlyNetIncome = 10_000m
                },
                InitialLenders.Community,
                request);

        LoanDecision wallClockSparseIncome =
            CareerCredit.Evaluate(
                CasualQualifiedHistory with
                {
                    VerifiedMonthlyNetIncome = 2_000m
                },
                InitialLenders.Community,
                request);

        Assert.True(activeCycleIncome.Approved);
        Assert.False(wallClockSparseIncome.Approved);
        Assert.Equal(
            LoanDeclineReason.NoAffordableCapacity,
            wallClockSparseIncome.Reason);

        // Integration guard: the caller must not derive VerifiedMonthlyNetIncome
        // from a sparse player's wall-clock calendar month while repayment uses
        // 30 active career-credit hours. That would punish players for being away.
    }

    private static RecurringOwnershipCostCycle RepresentativeFirstCycle()
    {
        LoanDecision decision =
            CareerCredit.Evaluate(
                CasualQualifiedHistory,
                InitialLenders.Community,
                new AircraftLoanRequest(
                    Price: 100_000m,
                    AppraisedValue: 100_000m,
                    Deposit: 20_000m,
                    TermMonths: 120,
                    CivilianOwnershipEligible: true));

        var agreement = new AircraftLoanAgreement(
            Guid.NewGuid(),
            "owned-session-shape",
            InitialLenders.Community.Id,
            decision.RequestedPrincipal,
            decision.AnnualRate,
            120,
            decision.MonthlyPayment,
            DateTimeOffset.UnixEpoch);

        AircraftLoanAmortizationSchedule amortization =
            AircraftLoanAmortization.Build(agreement);

        return AircraftLoanAmortization
            .BuildActivePlayCostSchedule(
                amortization,
                new OwnershipFixedCostCycle(
                    Insurance: 420m,
                    Storage: 250m),
                cycleCount: 1)[0];
    }

    private static decimal AccrueSessionShape(
        IEnumerable<TimeSpan> sessions,
        RecurringOwnershipCostCycle cost)
    {
        ActivePlayRecurringCostPolicy policy =
            ActivePlayRecurringCostPolicy.Default;

        TimeSpan progress = TimeSpan.Zero;
        decimal total = 0m;

        foreach (TimeSpan session in sessions)
        {
            ActivePlayRecurringCostAssessment assessment =
                policy.Assess(
                    progress,
                    session,
                    cost);

            total += assessment.Total;
            progress = assessment.CycleProgressAfter;

            if (assessment.CycleCompleted)
                progress = TimeSpan.Zero;
        }

        return total;
    }
}
