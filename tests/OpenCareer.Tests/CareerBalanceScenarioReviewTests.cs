using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Finance;

namespace OpenCareer.Tests;

public sealed class CareerBalanceScenarioReviewTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CasualCareerWithOneMarathonStaysInsideProgressionAndLoanBalanceTargets()
    {
        ContractPayQuote twoHourJob =
            EmployeeQuote(
                ContractKind.Cargo,
                hours: 2,
                distanceNm: 260);

        ContractPayQuote threeHourJob =
            EmployeeQuote(
                ContractKind.Cargo,
                hours: 3,
                distanceNm: 400);

        ContractPayQuote marathonJob =
            EmployeeQuote(
                ContractKind.Ferry,
                hours: 15,
                distanceNm: 1_500);

        Assert.Equal(1_969.23m, twoHourJob.PilotCashCompensation);
        Assert.Equal(2_961.17m, threeHourJob.PilotCashCompensation);
        Assert.Equal(17_774.86m, marathonJob.PilotCashCompensation);
        Assert.Equal(1.25, marathonJob.ExtendedDutyMultiplier, precision: 10);

        decimal cash = 0m;
        double careerHours = 0;
        int completedJobs = 0;
        double? firstCashOwnershipHours = null;

        void CompleteJob(ContractPayQuote quote, double hours)
        {
            cash += quote.PilotCashCompensation;
            careerHours += hours;
            completedJobs++;

            if (firstCashOwnershipHours is null
                && cash >= CareerProgressionPolicy.Default.MinimumAcquisitionCash)
            {
                firstCashOwnershipHours = careerHours;
            }
        }

        // A deliberately casual shape: mostly 2-3 hour sessions,
        // with one optional 15-hour marathon at the end.
        for (var i = 0; i < 20; i++)
            CompleteJob(twoHourJob, 2);

        for (var i = 0; i < 5; i++)
            CompleteJob(threeHourJob, 3);

        CompleteJob(marathonJob, 15);

        Assert.Equal(70d, careerHours);
        Assert.Equal(26, completedJobs);
        Assert.Equal(71_965.31m, cash);
        Assert.Equal(70d, firstCashOwnershipHours);
        Assert.InRange(
            firstCashOwnershipHours!.Value,
            CareerProgressionPolicy.Default.TargetOwnershipHoursLow,
            CareerProgressionPolicy.Default.TargetOwnershipHoursHigh);

        decimal averagePayPerActiveHour =
            decimal.Round(
                cash / (decimal)careerHours,
                2,
                MidpointRounding.AwayFromZero);

        Assert.Equal(1_028.08m, averagePayPerActiveHour);

        decimal marathonHourly =
            marathonJob.PilotCashCompensation / 15m;
        decimal twoHourHourly =
            twoHourJob.PilotCashCompensation / 2m;

        Assert.True(
            marathonHourly >= twoHourHourly * 1.20m);

        // The player may disappear for a year between sessions.
        // Default protected absence must not create catch-up ownership bills.
        OfflineLiabilityAssessment absence =
            OfflineLiabilityPolicy.Default.Assess(
                Start,
                Start.AddYears(1),
                monthlyFixedLiabilities: 1_600m,
                hasPassiveOperations: false);

        Assert.Equal(0m, absence.AccruedFixedLiabilities);
        Assert.Equal(absence.Elapsed, absence.ProtectedDuration);

        // Underwriting uses a 30-active-hour equivalent income in this review,
        // not a literal wall-clock calendar month.
        decimal normalizedThirtyHourIncome =
            decimal.Round(
                cash / (decimal)careerHours * 30m,
                2,
                MidpointRounding.AwayFromZero);

        Assert.Equal(30_842.28m, normalizedThirtyHourIncome);

        var history = new CareerCreditHistory(
            RealFlightHours: (decimal)careerHours,
            CompletedJobs: completedJobs,
            FailedJobs: 1,
            OnTimePayments: 0,
            MissedPayments: 0,
            SafetyScore: 93m,
            EmployerTrust: 85m,
            VerifiedMonthlyNetIncome: normalizedThirtyHourIncome,
            ExistingMonthlyDebtPayments: 0m,
            AvailableCash: cash,
            RequiredOperatingReserve: 4_000m,
            UnresolvedDefault: false);

        LoanDecision decision =
            CareerCredit.Evaluate(
                history,
                InitialLenders.Community,
                new AircraftLoanRequest(
                    Price: 100_000m,
                    AppraisedValue: 100_000m,
                    Deposit: 20_000m,
                    TermMonths: 120,
                    CivilianOwnershipEligible: true));

        Assert.True(decision.Approved);
        Assert.Equal(722, decision.CreditScore);
        Assert.Equal(80_000m, decision.RequestedPrincipal);
        Assert.Equal(80_000m, decision.MaximumAvailablePrincipal);
        Assert.Equal(945.30m, decision.MonthlyPayment);

        var agreement = new AircraftLoanAgreement(
            Guid.NewGuid(),
            "review-career-owned-aircraft",
            InitialLenders.Community.Id,
            decision.RequestedPrincipal,
            decision.AnnualRate,
            120,
            decision.MonthlyPayment,
            Start);

        AircraftLoanAmortizationSchedule schedule =
            AircraftLoanAmortization.Build(agreement);

        RecurringOwnershipCostCycle firstCycle =
            AircraftLoanAmortization.BuildActivePlayCostSchedule(
                schedule,
                new OwnershipFixedCostCycle(
                    Insurance: 420m,
                    Storage: 250m),
                cycleCount: 1)[0];

        Assert.Equal(decision.MonthlyPayment + 670m, firstCycle.Total);

        const decimal fiftyHourMaintenance = 575m;
        decimal thirtyHourMaintenanceReserve =
            fiftyHourMaintenance * 30m / 50m;

        decimal representativeOwnedBurdenPerHour =
            (firstCycle.Total + thirtyHourMaintenanceReserve) / 30m;

        Assert.InRange(
            representativeOwnedBurdenPerHour,
            65m,
            66m);

        Assert.True(
            representativeOwnedBurdenPerHour
            < averagePayPerActiveHour * 0.07m);

        Assert.True(
            cash - 20_000m
            >= history.RequiredOperatingReserve);
    }

    [Fact]
    public void MarathonOnlyControlStillRespectsFirstOwnershipWindow()
    {
        ContractPayQuote marathonJob =
            EmployeeQuote(
                ContractKind.Ferry,
                hours: 15,
                distanceNm: 1_500);

        decimal cash = 0m;
        double hours = 0;
        double? acquisitionHour = null;

        for (var i = 0; i < 4; i++)
        {
            cash += marathonJob.PilotCashCompensation;
            hours += 15;

            if (acquisitionHour is null
                && cash >= CareerProgressionPolicy.Default.MinimumAcquisitionCash)
            {
                acquisitionHour = hours;
            }
        }

        Assert.Equal(60d, hours);
        Assert.Equal(71_099.44m, cash);
        Assert.Equal(60d, acquisitionHour);
        Assert.InRange(
            acquisitionHour!.Value,
            CareerProgressionPolicy.Default.TargetOwnershipHoursLow,
            CareerProgressionPolicy.Default.TargetOwnershipHoursHigh);
    }

    [Fact]
    public void AllShortSessionControlAlsoReachesCashOwnershipInsideTargetWindow()
    {
        ContractPayQuote twoHourJob =
            EmployeeQuote(
                ContractKind.Cargo,
                hours: 2,
                distanceNm: 260);

        decimal cash = 0m;
        double hours = 0;
        double? acquisitionHour = null;

        for (var i = 0; i < 35; i++)
        {
            cash += twoHourJob.PilotCashCompensation;
            hours += 2;

            if (acquisitionHour is null
                && cash >= CareerProgressionPolicy.Default.MinimumAcquisitionCash)
            {
                acquisitionHour = hours;
            }
        }

        Assert.Equal(70d, hours);
        Assert.Equal(68_923.05m, cash);
        Assert.Equal(66d, acquisitionHour);
        Assert.InRange(
            acquisitionHour!.Value,
            CareerProgressionPolicy.Default.TargetOwnershipHoursLow,
            CareerProgressionPolicy.Default.TargetOwnershipHoursHigh);
    }

    private static ContractPayQuote EmployeeQuote(
        ContractKind kind,
        double hours,
        double distanceNm) =>
        ContractPayQuoteEngine.Quote(
            new ContractPayQuoteRequest(
                ServiceTrack.CivilianEmployment,
                kind,
                EstimatedFlightHours: hours,
                DistanceNauticalMiles: distanceNm,
                PayloadPounds: 500,
                DemandAttractiveness: 1,
                Urgency: 0.15,
                Difficulty: 0.15,
                RelationshipStrength: 0.20));
}
