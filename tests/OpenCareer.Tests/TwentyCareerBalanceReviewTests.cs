using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Finance;

namespace OpenCareer.Tests;

public sealed class TwentyCareerBalanceReviewTests
{
    public enum ReviewRole
    {
        Employee,
        Buyer,
        Owner,
        Military
    }

    public enum ReviewOutcome
    {
        Balanced,
        BlockedAsIntended,
        NeedsTuning
    }

    private sealed record ReviewAircraftTier(
        string Name,
        int MinimumQualifications,
        decimal Price,
        decimal InsuranceAndStoragePerThirtyHours,
        decimal RoutineMaintenancePerHour,
        decimal RepairReservePerHour,
        decimal MajorRepairBase,
        decimal RequiredOperatingReserve,
        LenderProfile? Lender,
        decimal DepositRate,
        int TermCycles);

    public sealed record Scenario(
        string Name,
        double Hours,
        int Jobs,
        int Failures,
        decimal Safety,
        decimal Trust,
        int OnTimePayments,
        int MissedPayments,
        int Qualifications,
        int RouteMilestones,
        int TrustMilestones,
        double MissionHours,
        double DistanceNm,
        double PayloadPounds,
        int AircraftTier,
        decimal ConditionPercent,
        decimal Cash,
        ReviewRole Role,
        ReviewOutcome Expected,
        int ExpectedLevel,
        decimal ExistingDebt = 0m);

    private sealed record ScenarioResult(
        CareerLevelSnapshot Level,
        int CreditScore,
        bool QualificationReady,
        bool FinanceApproved,
        bool CashPurchasePossible,
        LoanDeclineReason LoanReason,
        decimal EmployeePayPerHour,
        decimal ProjectedRepairAndMaintenancePerHour,
        decimal MajorRepairShock,
        decimal OperatingReserve,
        decimal OwnerNetPerHour,
        ReviewOutcome Outcome);

    private static readonly ReviewAircraftTier[] AircraftTiers =
    [
        new(
            "Employer/military assignment",
            0,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            null,
            0m,
            0),
        new(
            "Basic piston",
            1,
            60_000m,
            670m,
            11.50m,
            8m,
            3_000m,
            4_000m,
            InitialLenders.Community,
            0.20m,
            120),
        new(
            "Advanced piston/twin",
            2,
            250_000m,
            1_100m,
            30m,
            25m,
            10_000m,
            14_000m,
            InitialLenders.Commercial,
            0.20m,
            180),
        new(
            "Turboprop",
            3,
            650_000m,
            1_800m,
            70m,
            55m,
            18_000m,
            25_000m,
            InitialLenders.Commercial,
            0.20m,
            180),
        new(
            "Light jet",
            4,
            1_500_000m,
            3_000m,
            140m,
            110m,
            40_000m,
            60_000m,
            InitialLenders.Commercial,
            0.20m,
            180),
        new(
            "Midsize/regional",
            5,
            4_000_000m,
            6_000m,
            260m,
            210m,
            90_000m,
            140_000m,
            InitialLenders.Commercial,
            0.20m,
            180),
        new(
            "Heavy transport",
            6,
            8_000_000m,
            15_000m,
            500m,
            420m,
            220_000m,
            350_000m,
            InitialLenders.Commercial,
            0.375m,
            180)
    ];

    public static TheoryData<Scenario> Scenarios => new()
    {
        New("New weekend learner", 10, 6, 0, 88, 35, 0, 0, 0, 0, 0, 1, 120, 500, 1, 85, 9_500, ReviewRole.Employee, ReviewOutcome.BlockedAsIntended, 5),
        New("Casual learner", 30, 15, 0, 92, 55, 0, 0, 1, 1, 1, 2, 260, 500, 1, 85, 29_000, ReviewRole.Buyer, ReviewOutcome.NeedsTuning, 8),
        New("Careful saver before first plane", 55, 22, 0, 95, 75, 0, 0, 1, 2, 2, 3, 400, 500, 1, 85, 56_000, ReviewRole.Buyer, ReviewOutcome.Balanced, 11),
        New("First cash-aircraft pilot", 67, 23, 0, 95, 80, 0, 0, 1, 2, 2, 3, 400, 500, 1, 85, 68_000, ReviewRole.Buyer, ReviewOutcome.Balanced, 11),
        New("Marathon-focused early pilot", 60, 4, 0, 94, 70, 0, 0, 1, 1, 1, 15, 1_500, 500, 1, 85, 71_000, ReviewRole.Buyer, ReviewOutcome.Balanced, 10),
        New("Sloppy used-piston owner", 70, 25, 5, 65, 50, 0, 1, 1, 1, 1, 3, 400, 500, 1, 65, 42_000, ReviewRole.Owner, ReviewOutcome.NeedsTuning, 11),
        New("Returning piston owner after year away", 70, 26, 1, 93, 85, 3, 0, 1, 2, 2, 2, 260, 500, 1, 80, 50_000, ReviewRole.Owner, ReviewOutcome.Balanced, 12),
        New("Trusted twin-qualified employee", 80, 35, 0, 96, 95, 4, 0, 2, 3, 3, 6, 800, 2_000, 2, 85, 82_000, ReviewRole.Employee, ReviewOutcome.Balanced, 13),
        New("Financed twin owner", 100, 40, 1, 94, 88, 6, 0, 2, 4, 4, 6, 800, 2_000, 2, 82, 125_000, ReviewRole.Owner, ReviewOutcome.Balanced, 14),
        New("Cash-rich but unqualified twin buyer", 35, 12, 0, 91, 65, 0, 0, 1, 1, 1, 2, 260, 500, 2, 85, 300_000, ReviewRole.Buyer, ReviewOutcome.BlockedAsIntended, 9),
        New("Experienced pilot with poor credit", 120, 45, 3, 85, 70, 4, 4, 2, 4, 4, 6, 800, 2_000, 2, 80, 170_000, ReviewRole.Buyer, ReviewOutcome.BlockedAsIntended, 15, 1_200),
        New("Turboprop-qualified employee", 150, 60, 1, 96, 92, 8, 0, 3, 6, 5, 8, 1_000, 5_000, 3, 85, 210_000, ReviewRole.Employee, ReviewOutcome.Balanced, 17),
        New("Turboprop owner-operator", 170, 65, 1, 96, 92, 10, 0, 3, 6, 5, 8, 1_000, 5_000, 3, 85, 260_000, ReviewRole.Owner, ReviewOutcome.Balanced, 18),
        New("Marathon turboprop owner", 180, 30, 1, 94, 85, 8, 0, 3, 5, 4, 15, 1_500, 500, 3, 80, 280_000, ReviewRole.Owner, ReviewOutcome.Balanced, 18),
        New("Charter pilot not yet jet-qualified", 200, 100, 3, 92, 90, 10, 0, 3, 8, 6, 6, 800, 2_000, 4, 88, 400_000, ReviewRole.Buyer, ReviewOutcome.BlockedAsIntended, 20),
        New("Light-jet-qualified employee", 240, 85, 1, 97, 95, 12, 0, 4, 10, 8, 10, 1_800, 3_000, 4, 85, 480_000, ReviewRole.Employee, ReviewOutcome.Balanced, 22),
        New("Financed light-jet owner", 260, 90, 1, 97, 95, 14, 0, 4, 10, 8, 10, 1_800, 3_000, 4, 85, 520_000, ReviewRole.Owner, ReviewOutcome.Balanced, 22),
        New("Midsize access with repair stress", 420, 145, 2, 97, 96, 18, 0, 5, 15, 12, 12, 2_500, 12_000, 5, 72, 1_150_000, ReviewRole.Employee, ReviewOutcome.Balanced, 28),
        New("Heavy-cargo veteran employee", 650, 200, 3, 98, 98, 24, 0, 6, 20, 16, 15, 4_500, 40_000, 6, 90, 2_000_000, ReviewRole.Employee, ReviewOutcome.Balanced, 34),
        New("Military fighter-qualified veteran", 350, 100, 2, 96, 95, 12, 0, 5, 10, 10, 6, 800, 2_000, 0, 100, 500_000, ReviewRole.Military, ReviewOutcome.Balanced, 25)
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void TwentyCareerShapesMatchExpectedBalanceClassification(Scenario scenario)
    {
        ScenarioResult result = Evaluate(scenario);

        Assert.Equal(scenario.ExpectedLevel, result.Level.Level);
        Assert.Equal(scenario.Expected, result.Outcome);

        if (scenario.Role == ReviewRole.Employee)
        {
            Assert.Equal(0m, result.ProjectedRepairAndMaintenancePerHour);
            Assert.Equal(result.EmployeePayPerHour, result.OwnerNetPerHour);
        }

        if (scenario.Role == ReviewRole.Owner
            && scenario.Expected == ReviewOutcome.Balanced)
        {
            Assert.True(result.OwnerNetPerHour >= 850m);
            Assert.True(result.MajorRepairShock <= result.OperatingReserve);
        }
    }

    [Fact]
    public void LowConditionStarterRepairShockExceedsCurrentStarterReserve()
    {
        ReviewAircraftTier tier = AircraftTiers[1];
        (decimal routine, decimal repair, decimal shock) =
            RepairProjection(tier, conditionPercent: 65m);

        Assert.True(routine + repair > 25m);
        Assert.Equal(5_400m, shock);
        Assert.Equal(4_000m, tier.RequiredOperatingReserve);
        Assert.True(shock > tier.RequiredOperatingReserve);
    }

    [Fact]
    public void MidsizeConditionStressBarelyFitsProvisionalRepairReserve()
    {
        ReviewAircraftTier tier = AircraftTiers[5];
        (_, _, decimal shock) =
            RepairProjection(tier, conditionPercent: 72m);

        Assert.Equal(136_800m, shock);
        Assert.Equal(140_000m, tier.RequiredOperatingReserve);
        Assert.True(shock <= tier.RequiredOperatingReserve);
    }

    private static ScenarioResult Evaluate(Scenario scenario)
    {
        var level = CareerLevelPolicy.Default.Evaluate(
            new CareerProgressEvidence(
                scenario.Hours,
                scenario.Jobs,
                scenario.RouteMilestones,
                scenario.TrustMilestones,
                scenario.Qualifications));

        ContractPayQuote employeeQuote =
            ContractPayQuoteEngine.Quote(
                new ContractPayQuoteRequest(
                    ServiceTrack.CivilianEmployment,
                    ContractKind.Cargo,
                    scenario.MissionHours,
                    scenario.DistanceNm,
                    scenario.PayloadPounds,
                    DemandAttractiveness: 1,
                    Urgency: 0.15,
                    Difficulty: 0.15,
                    RelationshipStrength: 0.20));

        decimal employeePayPerHour =
            employeeQuote.PilotCashCompensation
            / (decimal)scenario.MissionHours;

        if (scenario.Role == ReviewRole.Military)
        {
            bool authorizedReadiness =
                scenario.Qualifications >= 4
                && scenario.Hours >= 200
                && scenario.Trust >= 70;

            return new ScenarioResult(
                level,
                CreditScore(scenario, employeePayPerHour, 0m),
                authorizedReadiness,
                FinanceApproved: false,
                CashPurchasePossible: false,
                LoanDeclineReason.RestrictedAsset,
                employeePayPerHour,
                0m,
                0m,
                0m,
                employeePayPerHour,
                authorizedReadiness
                    ? ReviewOutcome.Balanced
                    : ReviewOutcome.BlockedAsIntended);
        }

        ReviewAircraftTier tier = AircraftTiers[scenario.AircraftTier];
        bool qualificationReady =
            scenario.Qualifications >= tier.MinimumQualifications;

        (decimal routinePerHour, decimal repairPerHour, decimal majorShock) =
            RepairProjection(tier, scenario.ConditionPercent);

        decimal projectedVariablePerHour =
            routinePerHour + repairPerHour;

        decimal normalizedThirtyHourIncome =
            decimal.Round(
                employeePayPerHour * 30m,
                2,
                MidpointRounding.AwayFromZero);

        var history = new CareerCreditHistory(
            (decimal)scenario.Hours,
            scenario.Jobs,
            scenario.Failures,
            scenario.OnTimePayments,
            scenario.MissedPayments,
            scenario.Safety,
            scenario.Trust,
            normalizedThirtyHourIncome,
            scenario.ExistingDebt,
            scenario.Cash,
            tier.RequiredOperatingReserve,
            UnresolvedDefault: false);

        decimal deposit =
            decimal.Round(
                tier.Price * tier.DepositRate,
                2,
                MidpointRounding.AwayFromZero);

        LoanDecision loan =
            CareerCredit.Evaluate(
                history,
                tier.Lender!,
                new AircraftLoanRequest(
                    tier.Price,
                    tier.Price,
                    deposit,
                    tier.TermCycles,
                    CivilianOwnershipEligible: true));

        bool financeApproved =
            qualificationReady && loan.Approved;

        bool cashPurchasePossible =
            qualificationReady
            && scenario.Cash
                >= tier.Price + tier.RequiredOperatingReserve;

        decimal ownerNetPerHour = employeePayPerHour;

        if (scenario.Role == ReviewRole.Owner)
        {
            ContractPayQuote ownerQuote =
                ContractPayQuoteEngine.Quote(
                    new ContractPayQuoteRequest(
                        ServiceTrack.IndependentContract,
                        ContractKind.Cargo,
                        scenario.MissionHours,
                        scenario.DistanceNm,
                        scenario.PayloadPounds,
                        DemandAttractiveness: 1,
                        Urgency: 0.15,
                        Difficulty: 0.15,
                        RelationshipStrength: 0.20,
                        EstimatedPlayerOperatingCosts:
                            decimal.Round(
                                projectedVariablePerHour
                                * (decimal)scenario.MissionHours,
                                2,
                                MidpointRounding.AwayFromZero)));

            decimal variableActual =
                decimal.Round(
                    projectedVariablePerHour
                    * (decimal)scenario.MissionHours,
                    2,
                    MidpointRounding.AwayFromZero);

            decimal netBeforeFixed =
                ownerQuote.PilotCashCompensation
                - variableActual;

            decimal fixedPerHour =
                tier.InsuranceAndStoragePerThirtyHours / 30m;

            decimal loanPerHour =
                financeApproved
                    ? loan.MonthlyPayment / 30m
                    : 0m;

            ownerNetPerHour =
                netBeforeFixed / (decimal)scenario.MissionHours
                - fixedPerHour
                - loanPerHour;
        }

        ReviewOutcome outcome;

        if (!qualificationReady)
        {
            outcome = ReviewOutcome.BlockedAsIntended;
        }
        else if (scenario.Role == ReviewRole.Buyer
            && scenario.Hours < 50
            && (loan.Approved || cashPurchasePossible))
        {
            outcome = ReviewOutcome.NeedsTuning;
        }
        else if (scenario.Role == ReviewRole.Buyer
            && !loan.Approved
            && !cashPurchasePossible)
        {
            outcome = ReviewOutcome.BlockedAsIntended;
        }
        else if (scenario.Role == ReviewRole.Owner
            && majorShock > tier.RequiredOperatingReserve)
        {
            outcome = ReviewOutcome.NeedsTuning;
        }
        else
        {
            outcome = ReviewOutcome.Balanced;
        }

        return new ScenarioResult(
            level,
            history.CreditScore,
            qualificationReady,
            financeApproved,
            cashPurchasePossible,
            loan.Reason,
            employeePayPerHour,
            scenario.Role == ReviewRole.Owner
                ? projectedVariablePerHour
                : 0m,
            majorShock,
            tier.RequiredOperatingReserve,
            ownerNetPerHour,
            outcome);
    }

    private static int CreditScore(
        Scenario scenario,
        decimal employeePayPerHour,
        decimal requiredReserve)
    {
        var history = new CareerCreditHistory(
            (decimal)scenario.Hours,
            scenario.Jobs,
            scenario.Failures,
            scenario.OnTimePayments,
            scenario.MissedPayments,
            scenario.Safety,
            scenario.Trust,
            decimal.Round(
                employeePayPerHour * 30m,
                2,
                MidpointRounding.AwayFromZero),
            scenario.ExistingDebt,
            scenario.Cash,
            requiredReserve,
            UnresolvedDefault: false);

        return history.CreditScore;
    }

    private static (decimal Routine, decimal RepairReserve, decimal MajorShock)
        RepairProjection(
            ReviewAircraftTier tier,
            decimal conditionPercent)
    {
        if (conditionPercent is <= 0m or > 100m)
            throw new ArgumentOutOfRangeException(nameof(conditionPercent));

        decimal routineMultiplier =
            1m
            + Math.Max(0m, 80m - conditionPercent) / 40m;

        decimal repairMultiplier =
            1m
            + Math.Max(0m, 85m - conditionPercent) / 20m;

        decimal shockMultiplier =
            1m
            + Math.Max(0m, 85m - conditionPercent) / 25m;

        return (
            decimal.Round(
                tier.RoutineMaintenancePerHour
                * routineMultiplier,
                2,
                MidpointRounding.AwayFromZero),
            decimal.Round(
                tier.RepairReservePerHour
                * repairMultiplier,
                2,
                MidpointRounding.AwayFromZero),
            decimal.Round(
                tier.MajorRepairBase
                * shockMultiplier,
                2,
                MidpointRounding.AwayFromZero));
    }

    private static Scenario New(
        string name,
        double hours,
        int jobs,
        int failures,
        decimal safety,
        decimal trust,
        int onTimePayments,
        int missedPayments,
        int qualifications,
        int routeMilestones,
        int trustMilestones,
        double missionHours,
        double distanceNm,
        double payloadPounds,
        int aircraftTier,
        decimal conditionPercent,
        decimal cash,
        ReviewRole role,
        ReviewOutcome expected,
        int expectedLevel,
        decimal existingDebt = 0m) =>
        new(
            name,
            hours,
            jobs,
            failures,
            safety,
            trust,
            onTimePayments,
            missedPayments,
            qualifications,
            routeMilestones,
            trustMilestones,
            missionHours,
            distanceNm,
            payloadPounds,
            aircraftTier,
            conditionPercent,
            cash,
            role,
            expected,
            expectedLevel,
            existingDebt);
}
