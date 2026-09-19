using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Finance;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Tests;

public sealed class LongTermCareerBalanceSimulationTests
{
    private enum CareerTrack
    {
        AirlineEmployee,
        OwnerOperator
    }

    private sealed record SkillProfile(
        string Name,
        double FailureProbability,
        decimal InitialSafety,
        double QualificationPace,
        decimal Difficulty,
        decimal Urgency);

    private sealed record SessionProfile(
        string Name,
        double[] Hours);

    private sealed record OwnerAircraftTier(
        string Name,
        int Qualification,
        decimal Price,
        decimal Reserve,
        decimal InsuranceStoragePerCycle,
        decimal RoutinePerHour,
        decimal RepairReservePerHour,
        decimal MajorRepairBase,
        decimal WearPerHour,
        LenderProfile? Lender,
        decimal DepositRate,
        int TermCycles);

    private sealed record Result(
        string Name,
        CareerTrack Track,
        decimal EndingCash,
        double? FirstOwnershipHour,
        double? RegionalJetHour,
        double? NarrowbodyHour,
        double? WidebodyHour,
        int EndingLevel,
        double EndingTrust,
        decimal LowestCash,
        int MajorRepairs,
        int UncoveredRepairs,
        decimal TotalRepairCost,
        decimal TotalRepairReserveContributed,
        decimal EmployeeEquivalentHourly,
        decimal ActualHourly,
        IReadOnlyList<string> Issues);

    private static readonly SkillProfile[] Skills =
    [
        new("Elite", 0.005, 98m, 0.90, 0.18m, 0.16m),
        new("Strong", 0.012, 95m, 0.97, 0.16m, 0.15m),
        new("Average", 0.025, 91m, 1.05, 0.14m, 0.14m),
        new("Struggling", 0.050, 84m, 1.15, 0.12m, 0.12m),
        new("RiskyRecovering", 0.080, 76m, 1.25, 0.10m, 0.10m)
    ];

    private static readonly SessionProfile[] Sessions =
    [
        new("Short", [1, 2, 2, 3, 1, 2]),
        new("Balanced", [2, 3, 4, 3, 2, 4]),
        new("Long", [4, 5, 6, 4, 6, 5]),
        new("Marathon", [6, 8, 12, 15, 8, 15]),
        new("WeekendMixed", [1, 2, 3, 2, 15, 1, 3])
    ];

    private static readonly OwnerAircraftTier[] OwnerTiers =
    [
        new("Basic piston", 1, 60_000m, 4_000m, 670m, 11.50m, 8m, 3_000m, 0.010m, null, 1m, 0),
        new("Advanced twin", 2, 250_000m, 14_000m, 1_100m, 30m, 25m, 10_000m, 0.012m, InitialLenders.Commercial, 0.20m, 180),
        new("Turboprop", 3, 650_000m, 25_000m, 1_800m, 70m, 55m, 18_000m, 0.014m, InitialLenders.Commercial, 0.20m, 180),
        new("Light jet", 4, 1_500_000m, 60_000m, 3_000m, 140m, 110m, 40_000m, 0.016m, InitialLenders.Commercial, 0.20m, 180)
    ];

    [Fact]
    public void FiftyLongTermCareersHaveNoBalanceViolations()
    {
        var results = new List<Result>(50);
        var issues = new List<string>();

        var scenarioIndex = 0;
        foreach (SkillProfile skill in Skills)
        {
            foreach (SessionProfile session in Sessions)
            {
                foreach (CareerTrack track in Enum.GetValues<CareerTrack>())
                {
                    Result result =
                        Simulate(
                            scenarioIndex++,
                            skill,
                            session,
                            track);

                    results.Add(result);
                    issues.AddRange(
                        result.Issues.Select(
                            issue => $"{result.Name}: {issue}"));
                }
            }
        }

        Assert.Equal(50, results.Count);

        decimal shortAverage =
            results
                .Where(result =>
                    result.Name.Contains("/Short/", StringComparison.Ordinal)
                    && result.Track == CareerTrack.AirlineEmployee)
                .Average(result => result.ActualHourly);

        decimal marathonAverage =
            results
                .Where(result =>
                    result.Name.Contains("/Marathon/", StringComparison.Ordinal)
                    && result.Track == CareerTrack.AirlineEmployee)
                .Average(result => result.ActualHourly);

        decimal marathonAdvantage =
            marathonAverage / shortAverage;

        if (marathonAdvantage is < 1.12m or > 1.30m)
        {
            issues.Add(
                $"Cross-scenario marathon hourly advantage was {marathonAdvantage:P1}; target is 12-30%.");
        }

        if (results.Any(result => result.EndingCash < 0m))
            issues.Add("At least one long-term career ended with negative cash.");

        Assert.True(
            issues.Count == 0,
            string.Join(Environment.NewLine, issues));
    }

    private static Result Simulate(
        int scenarioIndex,
        SkillProfile skill,
        SessionProfile sessions,
        CareerTrack track)
    {
        var random =
            new DeterministicRandom(
                unchecked((ulong)(0xA51C_0000 + scenarioIndex * 7919)));

        decimal cash = 0m;
        decimal lowestCash = 0m;
        decimal totalEarned = 0m;
        decimal totalRepairCost = 0m;
        decimal totalRepairReserveContributed = 0m;
        decimal repairReserve = 0m;

        double hours = 0;
        int jobs = 0;
        int failures = 0;
        int qualifications = 0;
        int routeMilestones = 0;
        int trustMilestones = 0;
        int onTimePayments = 0;
        int majorRepairs = 0;
        int uncoveredRepairs = 0;

        decimal safety = skill.InitialSafety;
        var trust =
            EmployerTrustState.Start(
                Guid.NewGuid(),
                DateTimeOffset.UnixEpoch);

        OwnerAircraftTier? ownedTier = null;
        decimal condition = 100m;
        LoanDecision? activeLoan = null;
        double activeLoanHours = 0;

        double? firstOwnershipHour = null;
        double? regionalHour = null;
        double? narrowbodyHour = null;
        double? widebodyHour = null;

        decimal employeeEquivalentEarned = 0m;
        var issues = new List<string>();

        var sessionIndex = 0;
        while (hours < 1_000)
        {
            double jobHours =
                sessions.Hours[sessionIndex++ % sessions.Hours.Length];

            if (hours + jobHours > 1_000)
                jobHours = 1_000 - hours;

            bool marathon = jobHours > 6;
            ContractKind kind =
                marathon
                    ? ContractKind.Ferry
                    : ContractKind.Cargo;

            double distanceNm =
                jobHours
                * (marathon ? 105 : 130);

            double relationshipStrength =
                trust.RelationshipStrength;

            ContractPayQuote employeeQuote =
                ContractPayQuoteEngine.Quote(
                    new ContractPayQuoteRequest(
                        ServiceTrack.CivilianEmployment,
                        kind,
                        jobHours,
                        distanceNm,
                        PayloadPounds: 500,
                        DemandAttractiveness: 1,
                        Urgency: (double)skill.Urgency,
                        Difficulty: (double)skill.Difficulty,
                        RelationshipStrength: relationshipStrength));

            employeeEquivalentEarned +=
                employeeQuote.PilotCashCompensation;

            bool failed =
                random.Chance(skill.FailureProbability);

            DateTimeOffset eventTime =
                DateTimeOffset.UnixEpoch
                    .AddHours(hours + jobHours + jobs + 1);

            if (failed)
            {
                failures++;
                trust = trust.RecordFailure(eventTime);
                safety = Math.Max(55m, safety - 1.5m);
            }
            else
            {
                jobs++;

                bool safe =
                    random.Chance(
                        Math.Clamp(
                            (double)safety / 100.0,
                            0.55,
                            0.995));

                trust =
                    trust.RecordSuccess(
                        eventTime,
                        onTime: true,
                        safeOperation: safe);

                if (safe)
                    safety = Math.Min(99m, safety + 0.08m);
                else
                    safety = Math.Max(55m, safety - 0.6m);

                decimal playerPay;

                if (track == CareerTrack.OwnerOperator
                    && ownedTier is not null)
                {
                    (decimal routinePerHour,
                     decimal repairPerHour,
                     _) =
                        RepairProjection(
                            ownedTier,
                            condition);

                    decimal estimatedCosts =
                        decimal.Round(
                            (routinePerHour + repairPerHour)
                            * (decimal)jobHours,
                            2,
                            MidpointRounding.AwayFromZero);

                    ContractPayQuote ownerQuote =
                        ContractPayQuoteEngine.Quote(
                            new ContractPayQuoteRequest(
                                ServiceTrack.IndependentContract,
                                kind,
                                jobHours,
                                distanceNm,
                                PayloadPounds: 500,
                                DemandAttractiveness: 1,
                                Urgency: (double)skill.Urgency,
                                Difficulty: (double)skill.Difficulty,
                                RelationshipStrength: relationshipStrength,
                                EstimatedPlayerOperatingCosts: estimatedCosts));

                    playerPay =
                        ownerQuote.PilotCashCompensation;

                    decimal routineCost =
                        decimal.Round(
                            routinePerHour
                            * (decimal)jobHours,
                            2,
                            MidpointRounding.AwayFromZero);

                    decimal reserveContribution =
                        decimal.Round(
                            repairPerHour
                            * (decimal)jobHours,
                            2,
                            MidpointRounding.AwayFromZero);

                    repairReserve += reserveContribution;
                    totalRepairReserveContributed +=
                        reserveContribution;

                    decimal fixedCost =
                        decimal.Round(
                            ownedTier.InsuranceStoragePerCycle
                            * (decimal)jobHours / 30m,
                            2,
                            MidpointRounding.AwayFromZero);

                    decimal loanCost = 0m;
                    if (activeLoan is not null
                        && activeLoanHours
                            < ownedTier.TermCycles * 30d)
                    {
                        loanCost =
                            decimal.Round(
                                activeLoan.MonthlyPayment
                                * (decimal)jobHours / 30m,
                                2,
                                MidpointRounding.AwayFromZero);

                        activeLoanHours += jobHours;
                        if (loanCost > 0m)
                            onTimePayments++;
                    }

                    cash -=
                        routineCost
                        + fixedCost
                        + loanCost;

                    condition =
                        Math.Max(
                            55m,
                            condition
                            - ownedTier.WearPerHour
                                * (decimal)jobHours
                                * (safe ? 0.90m : 1.35m));

                    double hazardPerHour =
                        (double)(
                            0.00035m
                            * (1m
                               + Math.Max(
                                   0m,
                                   85m - condition) / 20m)
                            * (safe ? 0.85m : 1.50m));

                    if (random.Chance(
                            Math.Clamp(
                                hazardPerHour * jobHours,
                                0,
                                0.35)))
                    {
                        (_, _, decimal shock) =
                            RepairProjection(
                                ownedTier,
                                condition);

                        majorRepairs++;
                        totalRepairCost += shock;

                        decimal fromReserve =
                            Math.Min(
                                repairReserve,
                                shock);

                        repairReserve -= fromReserve;
                        decimal uncovered =
                            shock - fromReserve;

                        if (uncovered > 0m)
                        {
                            cash -= uncovered;
                            uncoveredRepairs++;
                        }

                        condition =
                            Math.Min(
                                100m,
                                condition + 12m);
                    }

                    if (condition < 70m)
                    {
                        decimal serviceCost =
                            decimal.Round(
                                ownedTier.MajorRepairBase
                                * 0.20m,
                                2,
                                MidpointRounding.AwayFromZero);

                        decimal fromReserve =
                            Math.Min(
                                repairReserve,
                                serviceCost);

                        repairReserve -= fromReserve;
                        decimal uncovered =
                            serviceCost - fromReserve;

                        if (uncovered > 0m)
                        {
                            cash -= uncovered;
                            uncoveredRepairs++;
                        }

                        totalRepairCost += serviceCost;
                        condition =
                            Math.Min(
                                100m,
                                condition + 15m);
                    }
                }
                else
                {
                    playerPay =
                        employeeQuote.PilotCashCompensation;
                }

                cash += playerPay;
                totalEarned += playerPay;
            }

            hours += jobHours;

            qualifications =
                QualificationCount(
                    hours,
                    skill.QualificationPace);

            routeMilestones =
                (int)Math.Floor(hours / 60);

            trustMilestones =
                (int)Math.Floor(
                    trust.TrustScore / 20);

            CareerLevelSnapshot level =
                CareerLevelPolicy.Default.Evaluate(
                    new CareerProgressEvidence(
                        hours,
                        jobs,
                        routeMilestones,
                        trustMilestones,
                        qualifications));

            if (track == CareerTrack.AirlineEmployee)
            {
                if (regionalHour is null
                    && AirlineCareerPolicy.Evaluate(
                        AirlineAircraftClass.RegionalJet,
                        level,
                        hours,
                        qualifications,
                        trust.Tier)
                        .CanFlyEmployerAircraft)
                {
                    regionalHour = hours;
                }

                if (narrowbodyHour is null
                    && AirlineCareerPolicy.Evaluate(
                        AirlineAircraftClass.Narrowbody,
                        level,
                        hours,
                        qualifications,
                        trust.Tier)
                        .CanFlyEmployerAircraft)
                {
                    narrowbodyHour = hours;
                }

                if (widebodyHour is null
                    && AirlineCareerPolicy.Evaluate(
                        AirlineAircraftClass.Widebody,
                        level,
                        hours,
                        qualifications,
                        trust.Tier)
                        .CanFlyEmployerAircraft)
                {
                    widebodyHour = hours;
                }
            }
            else
            {
                TryAcquireOrUpgrade(
                    skill,
                    hours,
                    jobs,
                    failures,
                    safety,
                    trust,
                    qualifications,
                    ref cash,
                    ref ownedTier,
                    ref condition,
                    ref activeLoan,
                    ref activeLoanHours,
                    ref firstOwnershipHour);
            }

            // Protected absence: some profiles represent sparse real-world play.
            if (sessions.Name == "WeekendMixed"
                && sessionIndex % 5 == 0
                && ownedTier is not null)
            {
                OfflineLiabilityAssessment absence =
                    OfflineLiabilityPolicy.Default.Assess(
                        eventTime,
                        eventTime.AddDays(45),
                        ownedTier.InsuranceStoragePerCycle,
                        hasPassiveOperations: false);

                if (absence.AccruedFixedLiabilities != 0m)
                    issues.Add("Protected absence created an ownership catch-up bill.");
            }

            lowestCash =
                Math.Min(
                    lowestCash,
                    cash);
        }

        CareerLevelSnapshot endingLevel =
            CareerLevelPolicy.Default.Evaluate(
                new CareerProgressEvidence(
                    hours,
                    jobs,
                    routeMilestones,
                    trustMilestones,
                    qualifications));

        decimal employeeEquivalentHourly =
            employeeEquivalentEarned / 1_000m;

        decimal actualHourly =
            totalEarned / 1_000m;

        if (track == CareerTrack.AirlineEmployee)
        {
            double regionalLatest =
                skill.Name switch
                {
                    "Elite" or "Strong" => 500,
                    "Average" => 560,
                    "Struggling" => 650,
                    _ => 800
                };

            double narrowLatest =
                skill.Name switch
                {
                    "Elite" or "Strong" => 650,
                    "Average" => 720,
                    "Struggling" => 850,
                    _ => 950
                };

            double wideLatest =
                skill.Name switch
                {
                    "Elite" or "Strong" => 850,
                    "Average" => 900,
                    "Struggling" => 980,
                    _ => 1_000
                };

            if (regionalHour is null
                || regionalHour > regionalLatest)
            {
                issues.Add(
                    $"Regional-jet access was {FormatHour(regionalHour)}; expected by {regionalLatest:0}h.");
            }

            if (narrowbodyHour is null
                || narrowbodyHour > narrowLatest)
            {
                issues.Add(
                    $"Narrowbody access was {FormatHour(narrowbodyHour)}; expected by {narrowLatest:0}h.");
            }

            if (widebodyHour is null
                || widebodyHour > wideLatest)
            {
                issues.Add(
                    $"Widebody access was {FormatHour(widebodyHour)}; expected by {wideLatest:0}h.");
            }

            if (lowestCash < 0m)
                issues.Add("Employee career went negative despite employer-paid aircraft costs.");
        }
        else
        {
            double ownerLatest =
                skill.Name switch
                {
                    "Elite" or "Strong" => 85,
                    "Average" => 95,
                    "Struggling" => 115,
                    _ => 135
                };

            if (firstOwnershipHour is null
                || firstOwnershipHour > ownerLatest)
            {
                issues.Add(
                    $"First ownership was {FormatHour(firstOwnershipHour)}; expected by {ownerLatest:0}h.");
            }

            if (firstOwnershipHour is < 45)
            {
                issues.Add(
                    $"First ownership arrived too early at {firstOwnershipHour:0.0}h.");
            }

            if (uncoveredRepairs > 1)
            {
                issues.Add(
                    $"{uncoveredRepairs} repair events exceeded accumulated repair reserve.");
            }

            if (lowestCash < -5_000m)
            {
                issues.Add(
                    $"Owner cash floor reached {lowestCash:C}, indicating unrecoverable repair/ownership pressure.");
            }

            if (actualHourly
                < employeeEquivalentHourly * 0.72m)
            {
                issues.Add(
                    $"Owner earned {actualHourly:C}/h versus {employeeEquivalentHourly:C}/h employee equivalent.");
            }
        }

        return new Result(
            $"{skill.Name}/{sessions.Name}/{track}",
            track,
            cash,
            firstOwnershipHour,
            regionalHour,
            narrowbodyHour,
            widebodyHour,
            endingLevel.Level,
            trust.TrustScore,
            lowestCash,
            majorRepairs,
            uncoveredRepairs,
            totalRepairCost,
            totalRepairReserveContributed,
            employeeEquivalentHourly,
            actualHourly,
            issues);
    }

    private static void TryAcquireOrUpgrade(
        SkillProfile skill,
        double hours,
        int jobs,
        int failures,
        decimal safety,
        EmployerTrustState trust,
        int qualifications,
        ref decimal cash,
        ref OwnerAircraftTier? ownedTier,
        ref decimal condition,
        ref LoanDecision? activeLoan,
        ref double activeLoanHours,
        ref double? firstOwnershipHour)
    {
        int currentIndex =
            ownedTier is null
                ? -1
                : Array.IndexOf(OwnerTiers, ownedTier);

        int desiredIndex =
            qualifications switch
            {
                >= 4 when hours >= 450 => 3,
                >= 3 when hours >= 300 => 2,
                >= 2 when hours >= 140 => 1,
                >= 1 => 0,
                _ => -1
            };

        if (desiredIndex <= currentIndex)
            return;

        OwnerAircraftTier target =
            OwnerTiers[desiredIndex];

        if (qualifications < target.Qualification)
            return;

        // Preserve the intended 50-80h first-aircraft pace through evidence,
        // not a level gate: sufficient verified work history must exist before
        // the player commits capital to ownership.
        if (ownedTier is null
            && (jobs < 18 || hours < 45))
        {
            return;
        }

        decimal purchaseCash;

        LoanDecision? financing = null;

        if (target.Lender is null)
        {
            purchaseCash =
                target.Price;
        }
        else
        {
            decimal deposit =
                decimal.Round(
                    target.Price * target.DepositRate,
                    2,
                    MidpointRounding.AwayFromZero);

            var history =
                new CareerCreditHistory(
                    (decimal)hours,
                    jobs,
                    failures,
                    OnTimePayments: activeLoan is null ? 0 : 6,
                    MissedPayments: 0,
                    safety,
                    (decimal)trust.TrustScore,
                    VerifiedMonthlyNetIncome:
                        Math.Max(
                            15_000m,
                            cash / Math.Max(
                                1m,
                                (decimal)hours) * 30m),
                    ExistingMonthlyDebtPayments:
                        activeLoan?.MonthlyPayment ?? 0m,
                    AvailableCash: cash,
                    RequiredOperatingReserve: target.Reserve,
                    UnresolvedDefault: false);

            financing =
                CareerCredit.Evaluate(
                    history,
                    target.Lender,
                    new AircraftLoanRequest(
                        target.Price,
                        target.Price,
                        deposit,
                        target.TermCycles,
                        CivilianOwnershipEligible: true));

            if (!financing.Approved)
                return;

            purchaseCash =
                deposit;
        }

        decimal saleProceeds =
            ownedTier is null
                ? 0m
                : decimal.Round(
                    ownedTier.Price
                    * Math.Clamp(
                        condition / 100m,
                        0.55m,
                        0.90m)
                    * 0.72m,
                    2,
                    MidpointRounding.AwayFromZero);

        decimal cashAfter =
            cash
            + saleProceeds
            - purchaseCash;

        if (cashAfter < target.Reserve)
            return;

        cash = cashAfter;
        ownedTier = target;
        condition =
            target == OwnerTiers[0]
                ? 82m
                : 88m;

        activeLoan = financing;
        activeLoanHours = 0;

        firstOwnershipHour ??=
            hours;
    }

    private static int QualificationCount(
        double hours,
        double pace)
    {
        double[] thresholds =
            [40, 90, 150, 240, 350, 500, 700];

        var count = 0;
        foreach (double threshold in thresholds)
        {
            if (hours >= threshold * pace)
                count++;
        }

        return count;
    }

    private static (
        decimal Routine,
        decimal RepairReserve,
        decimal MajorShock)
        RepairProjection(
            OwnerAircraftTier tier,
            decimal condition)
    {
        decimal routineMultiplier =
            1m
            + Math.Max(
                0m,
                80m - condition) / 40m;

        decimal repairMultiplier =
            1m
            + Math.Max(
                0m,
                85m - condition) / 20m;

        decimal shockMultiplier =
            1m
            + Math.Max(
                0m,
                85m - condition) / 25m;

        return (
            decimal.Round(
                tier.RoutinePerHour
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

    private static string FormatHour(double? value) =>
        value is null
            ? "never"
            : $"{value:0.0}h";
}
