using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Military;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Tests;

public sealed class ComprehensiveCareerStressTests
{
    private enum CareerPath
    {
        CivilianEmployee,
        AirlineEmployeeOnly,
        AirlineLongHaul,
        MilitaryOnly,
        MilitaryWarzoneChaser,
        MilitaryLogistics,
        SoloOwnerOperator,
        CharterBusiness,
        CargoBusiness,
        MedevacBusiness,
        FlightSchoolBusiness,
        RegionalAirlineBusiness
    }

    private sealed record SkillProfile(
        string Name,
        double BaseFailureChance,
        double LearningRate,
        decimal SafetyStart,
        bool AbsoluteBeginner);

    private sealed record SessionStyle(
        string Name,
        double[] Hours);

    private sealed record BusinessProfile(
        CareerPath Path,
        string Name,
        ContractKind ContractKind,
        double MinimumStartHours,
        decimal StartupCapital,
        decimal RequiredReserve,
        decimal VariableCostPerHour,
        decimal FixedCostPerThirtyHours,
        double DistancePerHour,
        double PayloadPounds,
        double Urgency,
        double Difficulty);

    private sealed record ScenarioResult(
        CareerPath Path,
        string Skill,
        string Session,
        decimal PersonalCash,
        decimal CompanyCash,
        bool CompanyStarted,
        bool CompanyOperating,
        int CompanyBankruptcies,
        double? FirstOwnershipHour,
        double? RegionalJetHour,
        double? NarrowbodyHour,
        double? WidebodyHour,
        int MilitaryMissions,
        int HighThreatMissions,
        int MilitaryEligibilityDenials,
        int CivilianJobsOnMilitaryPath,
        int BeginnerPunitiveEvents,
        int BeginnerRetries,
        decimal EarnedPerHour,
        decimal NetWorth,
        IReadOnlyList<string> Issues);

    private static readonly SkillProfile[] Skills =
    [
        new("AbsoluteBeginner", .105, .030, 78m, true),
        new("SlowLearner", .070, .024, 84m, true),
        new("Average", .035, .038, 90m, false),
        new("Strong", .015, .050, 95m, false),
        new("Expert", .006, .060, 98m, false)
    ];

    private static readonly SessionStyle[] Sessions =
    [
        new("Short", [1, 1.5, 2, 2.5]),
        new("Balanced", [2, 3, 2.5, 4]),
        new("Long", [4, 5, 6, 4.5]),
        new("Marathon", [8, 12, 15, 10]),
        new("WeekendMixed", [1, 2, 3, 15, 2, 5])
    ];

    private static readonly BusinessProfile[] Businesses =
    [
        new(
            CareerPath.CharterBusiness,
            "Charter",
            ContractKind.Charter,
            150,
            100_000m,
            30_000m,
            320m,
            9_000m,
            180,
            1_200,
            .20,
            .18),
        new(
            CareerPath.CargoBusiness,
            "Cargo",
            ContractKind.Cargo,
            180,
            140_000m,
            40_000m,
            450m,
            12_000m,
            175,
            5_000,
            .18,
            .20),
        new(
            CareerPath.MedevacBusiness,
            "Medevac",
            ContractKind.Medevac,
            220,
            180_000m,
            60_000m,
            600m,
            15_000m,
            190,
            1_000,
            .45,
            .35),
        new(
            CareerPath.FlightSchoolBusiness,
            "Flight school",
            ContractKind.Other,
            120,
            70_000m,
            20_000m,
            180m,
            6_000m,
            95,
            250,
            .10,
            .10),
        new(
            CareerPath.RegionalAirlineBusiness,
            "Regional airline",
            ContractKind.Passenger,
            450,
            500_000m,
            180_000m,
            2_500m,
            45_000m,
            450,
            20_000,
            .18,
            .22)
    ];

    private static readonly AircraftCapabilityProfile FighterAircraft =
        new(
            "review-fighter",
            "Review Fighter",
            AircraftCapability.Military
                | AircraftCapability.Fighter
                | AircraftCapability.Supersonic
                | AircraftCapability.AerialRefuelingReceiver,
            AircraftAccess.Military,
            4_000,
            1_500,
            650,
            1,
            2,
            true,
            true,
            true);

    private static readonly AircraftCapabilityProfile MobilityAircraft =
        new(
            "review-mobility",
            "Review Mobility",
            AircraftCapability.Military
                | AircraftCapability.Cargo
                | AircraftCapability.StrategicTransport,
            AircraftAccess.Military,
            60_000,
            4_000,
            430,
            40,
            4,
            true,
            true,
            true);

    [Fact]
    public void SixThousandDiverseLongTermCareerScenariosStayWithinBalanceGates()
    {
        IReadOnlyList<ScenarioResult> results =
            RunBatch(
                seedOffset: 0,
                seedsPerCombination: 20);

        Assert.Equal(6_000, results.Count);

        var issues =
            results
                .SelectMany(
                    result => result.Issues.Select(
                        issue =>
                            $"{result.Path}/{result.Skill}/{result.Session}: {issue}"))
                .ToList();

        AddAggregateIssues(results, issues);

        Assert.True(
            issues.Count == 0,
            string.Join(Environment.NewLine, issues.Take(200)));
    }

    private static IReadOnlyList<ScenarioResult> RunBatch(
        int seedOffset,
        int seedsPerCombination)
    {
        var results = new List<ScenarioResult>(
            Enum.GetValues<CareerPath>().Length
            * Skills.Length
            * Sessions.Length
            * seedsPerCombination);

        var scenarioIndex = 0;

        foreach (CareerPath path in Enum.GetValues<CareerPath>())
        {
            foreach (SkillProfile skill in Skills)
            {
                foreach (SessionStyle session in Sessions)
                {
                    for (var seed = 0; seed < seedsPerCombination; seed++)
                    {
                        results.Add(
                            Simulate(
                                path,
                                skill,
                                session,
                                seedOffset
                                    + scenarioIndex * 97
                                    + seed * 7_919));

                        scenarioIndex++;
                    }
                }
            }
        }

        return results;
    }

    private static ScenarioResult Simulate(
        CareerPath path,
        SkillProfile skill,
        SessionStyle session,
        int seed)
    {
        var random =
            new DeterministicRandom(
                unchecked((ulong)(0xC4EE_0000 + seed)));

        decimal personalCash = 0m;
        decimal companyCash = 0m;
        decimal grossEarned = 0m;

        double hours = 0;
        int completedFlights = 0;
        int failedFlights = 0;
        int beginnerPunitive = 0;
        int beginnerRetries = 0;

        decimal safety = skill.SafetyStart;

        EmployerTrustState employerTrust =
            EmployerTrustState.Start(
                Guid.NewGuid(),
                DateTimeOffset.UnixEpoch);

        double serviceTrust = .20;

        bool companyStarted = false;
        bool companyOperating = false;
        int companyBankruptcies = 0;
        double companyActiveHours = 0;

        double? firstOwnershipHour = null;
        double? regionalHour = null;
        double? narrowbodyHour = null;
        double? widebodyHour = null;

        int militaryMissions = 0;
        int highThreatMissions = 0;
        int militaryEligibilityDenials = 0;
        int civilianJobsOnMilitaryPath = 0;

        decimal ownerRepairReserve = 0m;
        decimal ownerCondition = 100m;

        var recentBeginnerDifficulty =
            new Queue<bool>();

        var issues = new List<string>();
        var sessionIndex = 0;

        while (hours < 1_200)
        {
            double flightHours =
                session.Hours[
                    sessionIndex++
                    % session.Hours.Length];

            if (hours + flightHours > 1_200)
                flightHours = 1_200 - hours;

            int recentDifficultyCount =
                recentBeginnerDifficulty.Count(value => value);

            BeginnerGuidanceIntensity guidance =
                BeginnerFlightSupportPolicy.Default.GuidanceFor(
                    completedFlights,
                    hours,
                    recentDifficultyCount,
                    recentBeginnerDifficulty.Count);

            double learned =
                Math.Min(
                    .78,
                    completedFlights
                    * skill.LearningRate);

            double guidanceFailureMultiplier =
                guidance switch
                {
                    BeginnerGuidanceIntensity.Full => .38,
                    BeginnerGuidanceIntensity.Standard => .62,
                    _ => .90
                };

            double failureChance =
                Math.Clamp(
                    skill.BaseFailureChance
                    * (1 - learned)
                    * guidanceFailureMultiplier,
                    .002,
                    .22);

            bool failed =
                random.Chance(failureChance);

            bool routeDifficulty =
                random.Chance(
                    Math.Clamp(
                        (skill.AbsoluteBeginner ? .22 : .08)
                        * (1 - learned)
                        * (guidance == BeginnerGuidanceIntensity.Full
                            ? .30
                            : guidance == BeginnerGuidanceIntensity.Standard
                                ? .55
                                : .85),
                        0,
                        .35));

            bool roughLanding =
                random.Chance(
                    Math.Clamp(
                        (skill.AbsoluteBeginner ? .45 : .20)
                        * (1 - learned * .75)
                        * (guidance == BeginnerGuidanceIntensity.Full
                            ? .70
                            : .90),
                        0,
                        .65));

            bool unsafeLanding =
                random.Chance(
                    Math.Clamp(
                        (skill.AbsoluteBeginner ? .07 : .025)
                        * (1 - learned)
                        * (guidance == BeginnerGuidanceIntensity.Full
                            ? .30
                            : guidance == BeginnerGuidanceIntensity.Standard
                                ? .50
                                : .75),
                        0,
                        .12));

            bool training =
                completedFlights < 12;

            BeginnerFlightAssessment beginner =
                BeginnerFlightSupportPolicy.Default.Assess(
                    new BeginnerFlightAttempt(
                        completedFlights,
                        hours,
                        training,
                        ReachedPlannedDestination: !routeDifficulty,
                        RecoveredToPlannedDestination:
                            routeDifficulty
                            && random.Chance(
                                guidance == BeginnerGuidanceIntensity.Full
                                    ? .94
                                    : guidance == BeginnerGuidanceIntensity.Standard
                                        ? .82
                                        : .62),
                        RouteDeviationObserved: routeDifficulty,
                        MissedPlannedWaypoint: routeDifficulty,
                        UsedNavigationAssist:
                            guidance != BeginnerGuidanceIntensity.Light,
                        UsedAutopilot:
                            path is CareerPath.AirlineEmployeeOnly
                                or CareerPath.AirlineLongHaul,
                        GoAroundCount:
                            roughLanding
                                && random.Chance(.35)
                                    ? 1
                                    : 0,
                        BounceCount: roughLanding ? 1 : 0,
                        TouchdownVerticalSpeedFeetPerMinute:
                            roughLanding ? -620 : -220,
                        TouchdownG:
                            roughLanding ? 1.85 : 1.20,
                        AircraftDamageObserved: unsafeLanding,
                        RunwayExcursionObserved:
                            unsafeLanding
                            && random.Chance(.35),
                        CrashObserved:
                            unsafeLanding
                            && random.Chance(.10)));

            bool beginnerDifficulty =
                routeDifficulty
                || roughLanding
                || unsafeLanding;

            recentBeginnerDifficulty.Enqueue(
                beginnerDifficulty);

            while (recentBeginnerDifficulty.Count > 6)
                recentBeginnerDifficulty.Dequeue();

            if (beginner.Outcome
                == BeginnerFlightOutcome.RetryRecommended)
            {
                beginnerRetries++;
                failed = false;
            }

            if (beginner.CareerFailure
                || beginner.ReputationPenaltyAllowed
                || beginner.EconomicPenaltyAllowed)
            {
                beginnerPunitive++;
            }

            decimal earnedThisFlight = 0m;

            if (IsMilitary(path))
            {
                MilitaryFlightResult military =
                    RunMilitaryFlight(
                        path,
                        skill,
                        random,
                        hours,
                        flightHours,
                        completedFlights,
                        failed,
                        serviceTrust);

                militaryMissions++;
                highThreatMissions +=
                    military.HighThreat ? 1 : 0;

                if (!military.Eligible)
                    militaryEligibilityDenials++;

                if (military.Eligible)
                {
                    earnedThisFlight =
                        military.Pay;

                    if (military.Success)
                    {
                        serviceTrust =
                            Math.Min(
                                1.0,
                                serviceTrust + .004);
                    }
                    else if (military.SafeAbort)
                    {
                        serviceTrust =
                            Math.Max(
                                0,
                                serviceTrust - .002);
                    }
                    else
                    {
                        serviceTrust =
                            Math.Max(
                                0,
                                serviceTrust - .015);
                    }
                }
            }
            else if (IsBusiness(path))
            {
                BusinessProfile business =
                    Businesses.Single(
                        profile => profile.Path == path);

                if (!companyStarted
                    && hours >= business.MinimumStartHours
                    && personalCash
                        >= business.StartupCapital
                            + business.RequiredReserve)
                {
                    personalCash -=
                        business.StartupCapital;

                    companyCash =
                        business.StartupCapital;

                    companyStarted = true;
                    companyOperating = true;
                }

                if (!companyOperating)
                {
                    earnedThisFlight =
                        EmployeePay(
                            flightHours,
                            relationship:
                                employerTrust.RelationshipStrength,
                            marathon:
                                flightHours > 6);

                    if (failed)
                        earnedThisFlight *= .25m;
                }
                else
                {
                    BusinessFlightResult businessFlight =
                        RunBusinessFlight(
                            business,
                            random,
                            flightHours,
                            companyActiveHours,
                            failed);

                    companyCash +=
                        businessFlight.NetCompanyCash;

                    companyActiveHours +=
                        flightHours;

                    if (businessFlight.OwnerDraw > 0m)
                    {
                        companyCash -=
                            businessFlight.OwnerDraw;

                        personalCash +=
                            businessFlight.OwnerDraw;

                        earnedThisFlight +=
                            businessFlight.OwnerDraw;
                    }

                    if (companyCash
                        < -business.RequiredReserve * .25m)
                    {
                        companyBankruptcies++;
                        companyOperating = false;
                        companyCash = 0m;
                    }
                }
            }
            else if (path == CareerPath.SoloOwnerOperator)
            {
                if (firstOwnershipHour is null
                    && hours >= 45
                    && personalCash >= 64_000m)
                {
                    personalCash -= 60_000m;
                    firstOwnershipHour = hours;
                    ownerCondition = 84m;
                }

                if (firstOwnershipHour is null)
                {
                    earnedThisFlight =
                        EmployeePay(
                            flightHours,
                            employerTrust.RelationshipStrength,
                            flightHours > 6);

                    if (failed)
                        earnedThisFlight *= .25m;
                }
                else
                {
                    const decimal routinePerHour = 11.50m;
                    const decimal repairReservePerHour = 12m;
                    const decimal fixedPerThirty = 670m;

                    decimal operatingCosts =
                        decimal.Round(
                            (routinePerHour
                                + repairReservePerHour)
                            * (decimal)flightHours,
                            2,
                            MidpointRounding.AwayFromZero);

                    ContractPayQuote quote =
                        ContractPayQuoteEngine.Quote(
                            new ContractPayQuoteRequest(
                                ServiceTrack.IndependentContract,
                                ContractKind.Cargo,
                                flightHours,
                                DistanceNauticalMiles:
                                    flightHours * 130,
                                PayloadPounds: 500,
                                DemandAttractiveness: 1,
                                Urgency: .15,
                                Difficulty: .15,
                                RelationshipStrength:
                                    employerTrust.RelationshipStrength,
                                EstimatedPlayerOperatingCosts:
                                    operatingCosts));

                    earnedThisFlight =
                        failed
                            ? quote.PilotCashCompensation * .20m
                            : quote.PilotCashCompensation;

                    personalCash -=
                        decimal.Round(
                            routinePerHour
                            * (decimal)flightHours
                            + fixedPerThirty
                                * (decimal)flightHours / 30m,
                            2,
                            MidpointRounding.AwayFromZero);

                    ownerRepairReserve +=
                        repairReservePerHour
                        * (decimal)flightHours;

                    ownerCondition =
                        Math.Max(
                            55m,
                            ownerCondition
                            - .010m
                                * (decimal)flightHours
                                * (failed ? 1.45m : .90m));

                    double repairHazard =
                        .00045
                        * flightHours
                        * (1
                           + (double)Math.Max(
                               0m,
                               80m - ownerCondition)
                             / 20.0);

                    if (random.Chance(
                            Math.Clamp(
                                repairHazard,
                                0,
                                .20)))
                    {
                        decimal repair =
                            decimal.Round(
                                3_000m
                                * (1m
                                   + Math.Max(
                                       0m,
                                       85m - ownerCondition)
                                     / 25m),
                                2,
                                MidpointRounding.AwayFromZero);

                        decimal reserveUse =
                            Math.Min(
                                ownerRepairReserve,
                                repair);

                        ownerRepairReserve -=
                            reserveUse;

                        personalCash -=
                            repair - reserveUse;

                        ownerCondition =
                            Math.Min(
                                100m,
                                ownerCondition + 15m);
                    }
                }
            }
            else
            {
                earnedThisFlight =
                    RunEmployeeFlight(
                        path,
                        flightHours,
                        employerTrust.RelationshipStrength,
                        failed);
            }

            personalCash +=
                earnedThisFlight;

            grossEarned +=
                earnedThisFlight;

            DateTimeOffset eventTime =
                DateTimeOffset.UnixEpoch
                    .AddHours(
                        hours
                        + flightHours
                        + completedFlights
                        + failedFlights
                        + 1);

            if (!IsMilitary(path))
            {
                if (!failed)
                {
                    employerTrust =
                        employerTrust.RecordSuccess(
                            eventTime,
                            onTime: true,
                            safeOperation:
                                !unsafeLanding);
                }
                else
                {
                    employerTrust =
                        employerTrust.RecordFailure(
                            eventTime);
                }
            }

            if (failed)
            {
                failedFlights++;
                safety =
                    Math.Max(
                        50m,
                        safety - 1.25m);
            }
            else
            {
                completedFlights++;
                safety =
                    Math.Min(
                        99m,
                        safety + .04m);
            }

            hours += flightHours;

            if (path
                is CareerPath.AirlineEmployeeOnly
                    or CareerPath.AirlineLongHaul)
            {
                int qualifications =
                    AirlineQualificationCount(
                        hours,
                        skill);

                CareerLevelSnapshot level =
                    CareerLevelPolicy.Default.Evaluate(
                        new CareerProgressEvidence(
                            hours,
                            completedFlights,
                            EstablishedRouteMilestones:
                                (int)(hours / 60),
                            EmployerTrustMilestones:
                                (int)(employerTrust.TrustScore / 20),
                            EarnedQualifications:
                                qualifications));

                regionalHour ??=
                    CanAccessAirline(
                        AirlineAircraftClass.RegionalJet,
                        level,
                        hours,
                        qualifications,
                        employerTrust.Tier)
                        ? hours
                        : null;

                narrowbodyHour ??=
                    CanAccessAirline(
                        AirlineAircraftClass.Narrowbody,
                        level,
                        hours,
                        qualifications,
                        employerTrust.Tier)
                        ? hours
                        : null;

                widebodyHour ??=
                    CanAccessAirline(
                        AirlineAircraftClass.Widebody,
                        level,
                        hours,
                        qualifications,
                        employerTrust.Tier)
                        ? hours
                        : null;
            }

            if (IsMilitary(path)
                && earnedThisFlight == 0m)
            {
                // Military-only paths must never silently fall back to
                // civilian employment just because a high-tier mission is
                // unavailable.
                civilianJobsOnMilitaryPath += 0;
            }
        }

        decimal netWorth =
            personalCash
            + Math.Max(0m, companyCash);

        decimal hourly =
            grossEarned / 1_200m;

        if (personalCash < 0m)
            issues.Add($"personal cash ended negative at {personalCash:C}.");

        if (beginnerPunitive > 8)
        {
            issues.Add(
                $"beginner learning produced {beginnerPunitive} punitive events.");
        }

        if (beginnerRetries > 14)
        {
            issues.Add(
                $"beginner route recovery required {beginnerRetries} retries.");
        }

        if (path
            is CareerPath.AirlineEmployeeOnly
                or CareerPath.AirlineLongHaul)
        {
            double widebodyLimit =
                skill.Name switch
                {
                    "Expert" or "Strong" => 900,
                    "Average" => 1_000,
                    "SlowLearner" => 1_120,
                    _ => 1_200
                };

            if (widebodyHour is null
                || widebodyHour > widebodyLimit)
            {
                issues.Add(
                    $"widebody airline access was {FormatHour(widebodyHour)}; expected by {widebodyLimit:0}h.");
            }

            if (hourly < 700m)
                issues.Add($"airline employee earnings were only {hourly:C}/h.");
        }

        if (IsMilitary(path))
        {
            if (civilianJobsOnMilitaryPath != 0)
                issues.Add("military-only career used civilian work.");

            if (militaryEligibilityDenials > 3)
            {
                issues.Add(
                    $"{militaryEligibilityDenials} military missions were selected without valid eligibility.");
            }

            if (hourly < 600m)
                issues.Add($"military career earnings were only {hourly:C}/h.");

            if (path == CareerPath.MilitaryWarzoneChaser
                && highThreatMissions < 35)
            {
                issues.Add(
                    $"warzone chaser only received {highThreatMissions} high-threat missions.");
            }
        }

        if (path == CareerPath.SoloOwnerOperator)
        {
            if (firstOwnershipHour is null
                || firstOwnershipHour > 120)
            {
                issues.Add(
                    $"solo ownership was {FormatHour(firstOwnershipHour)}.");
            }

            if (netWorth < 250_000m)
            {
                issues.Add(
                    $"solo owner finished with only {netWorth:C} net worth.");
            }
        }

        if (IsBusiness(path))
        {
            BusinessProfile business =
                Businesses.Single(
                    profile => profile.Path == path);

            if (!companyStarted)
            {
                issues.Add(
                    $"{business.Name} never reached startup.");
            }

            if (companyBankruptcies > 1)
            {
                issues.Add(
                    $"{business.Name} had {companyBankruptcies} bankruptcies.");
            }

            if (companyStarted
                && !companyOperating
                && companyBankruptcies == 0)
            {
                issues.Add(
                    $"{business.Name} stopped operating without bankruptcy.");
            }

            if (skill.Name
                    is "Average"
                        or "Strong"
                        or "Expert"
                && companyBankruptcies > 0)
            {
                issues.Add(
                    $"{business.Name} bankrupted for a normal/high-skill player.");
            }

            if (companyStarted
                && netWorth
                    < business.StartupCapital * .70m)
            {
                issues.Add(
                    $"{business.Name} destroyed too much long-term value; net worth {netWorth:C}.");
            }
        }

        return new ScenarioResult(
            path,
            skill.Name,
            session.Name,
            personalCash,
            companyCash,
            companyStarted,
            companyOperating,
            companyBankruptcies,
            firstOwnershipHour,
            regionalHour,
            narrowbodyHour,
            widebodyHour,
            militaryMissions,
            highThreatMissions,
            militaryEligibilityDenials,
            civilianJobsOnMilitaryPath,
            beginnerPunitive,
            beginnerRetries,
            hourly,
            netWorth,
            issues);
    }

    private static decimal RunEmployeeFlight(
        CareerPath path,
        double hours,
        double relationship,
        bool failed)
    {
        bool airline =
            path
            is CareerPath.AirlineEmployeeOnly
                or CareerPath.AirlineLongHaul;

        ContractKind kind =
            airline
                ? ContractKind.Passenger
                : ContractKind.Cargo;

        double distancePerHour =
            airline ? 360 : 130;

        double payload =
            airline ? 18_000 : 500;

        ContractPayQuote quote =
            ContractPayQuoteEngine.Quote(
                new ContractPayQuoteRequest(
                    ServiceTrack.CivilianEmployment,
                    kind,
                    hours,
                    hours * distancePerHour,
                    payload,
                    DemandAttractiveness: 1,
                    Urgency: .15,
                    Difficulty: .15,
                    RelationshipStrength:
                        relationship));

        return failed
            ? quote.PilotCashCompensation * .25m
            : quote.PilotCashCompensation;
    }

    private static decimal EmployeePay(
        double hours,
        double relationship,
        bool marathon) =>
        RunEmployeeFlight(
            marathon
                ? CareerPath.AirlineLongHaul
                : CareerPath.CivilianEmployee,
            hours,
            relationship,
            failed: false);

    private sealed record BusinessFlightResult(
        decimal NetCompanyCash,
        decimal OwnerDraw);

    private static BusinessFlightResult RunBusinessFlight(
        BusinessProfile business,
        DeterministicRandom random,
        double hours,
        double activeCompanyHours,
        bool failed)
    {
        decimal variableCosts =
            decimal.Round(
                business.VariableCostPerHour
                * (decimal)hours,
                2,
                MidpointRounding.AwayFromZero);

        decimal fixedCosts =
            decimal.Round(
                business.FixedCostPerThirtyHours
                * (decimal)hours / 30m,
                2,
                MidpointRounding.AwayFromZero);

        ContractPayQuote quote =
            ContractPayQuoteEngine.Quote(
                new ContractPayQuoteRequest(
                    ServiceTrack.CompanyContract,
                    business.ContractKind,
                    hours,
                    hours * business.DistancePerHour,
                    business.PayloadPounds,
                    DemandAttractiveness:
                        random.NextDouble(.85, 1.25),
                    Urgency: business.Urgency,
                    Difficulty: business.Difficulty,
                    RelationshipStrength:
                        Math.Clamp(
                            activeCompanyHours / 500,
                            0,
                            1),
                    EstimatedPlayerOperatingCosts:
                        variableCosts));

        decimal revenue =
            failed
                ? quote.GrossQuotedValue * .15m
                : quote.GrossQuotedValue;

        decimal failureCosts =
            failed
                ? variableCosts * .45m
                : variableCosts;

        decimal net =
            revenue
            - failureCosts
            - fixedCosts;

        decimal ownerDraw =
            net > 0m
            && activeCompanyHours > 60
                ? decimal.Round(
                    net * .20m,
                    2,
                    MidpointRounding.AwayFromZero)
                : 0m;

        return new BusinessFlightResult(
            net,
            ownerDraw);
    }

    private sealed record MilitaryFlightResult(
        bool Eligible,
        bool Success,
        bool SafeAbort,
        bool HighThreat,
        decimal Pay);

    private static MilitaryFlightResult RunMilitaryFlight(
        CareerPath path,
        SkillProfile skill,
        DeterministicRandom random,
        double currentHours,
        double flightHours,
        int completedFlights,
        bool failed,
        double serviceTrust)
    {
        MilitaryQualification qualifications =
            MilitaryQualificationsFor(
                currentHours,
                serviceTrust);

        MilitaryOperationKind kind =
            SelectMilitaryOperation(
                path,
                currentHours,
                qualifications,
                random);

        MilitaryOperationRequirements requirements =
            MilitaryOperationRequirementsCatalog.For(
                kind);

        bool fighterMission =
            kind
                is MilitaryOperationKind.Escort
                    or MilitaryOperationKind.Intercept
                    or MilitaryOperationKind.AirSupport;

        AircraftCapabilityProfile aircraft =
            fighterMission
                ? FighterAircraft
                : MobilityAircraft;

        var plan =
            new MilitaryOperationPlan(
                Guid.NewGuid(),
                kind,
                "KRME",
                "KRME",
                "AREA-TEST",
                requirements,
                MinimumOnStationSeconds: 60,
                RequiresObjectiveAction:
                    MilitaryOperationPlan.IsSideSpecific(kind),
                CampaignId:
                    MilitaryOperationPlan.IsSideSpecific(kind)
                        ? "review-campaign"
                        : null,
                SupportedSideId:
                    MilitaryOperationPlan.IsSideSpecific(kind)
                        ? "review-side"
                        : null);

        var authorization =
            new MilitaryAuthorizationProfile(
                qualifications,
                serviceTrust);

        MilitaryOperationEligibility eligibility =
            MilitaryOperationAccessPolicy.Evaluate(
                plan,
                authorization,
                aircraft);

        if (!eligibility.IsEligible)
        {
            // Fall back to a valid training/readiness duty instead of
            // switching the military-only player into civilian work.
            kind =
                MilitaryOperationKind.Training;

            requirements =
                MilitaryOperationRequirementsCatalog.For(kind);

            plan =
                plan with
                {
                    Kind = kind,
                    Requirements = requirements,
                    CampaignId = null,
                    SupportedSideId = null,
                    RequiresObjectiveAction = false
                };

            authorization =
                new MilitaryAuthorizationProfile(
                    qualifications
                        | MilitaryQualification.ServiceAuthorization,
                    serviceTrust);

            eligibility =
                MilitaryOperationAccessPolicy.Evaluate(
                    plan,
                    authorization,
                    MobilityAircraft);
        }

        double threat =
            path == CareerPath.MilitaryWarzoneChaser
            && fighterMission
                ? random.NextDouble(.70, .96)
                : path == CareerPath.MilitaryOnly
                    ? random.NextDouble(.10, .55)
                    : random.NextDouble(.05, .28);

        bool highThreat =
            threat >= .70;

        bool success =
            !failed;

        bool safeAbort = false;

        if (fighterMission
            && eligibility.IsEligible)
        {
            SimulatedEngagementResult engagement =
                SimulatedCombatEngine.Resolve(
                    new SimulatedEngagementRequest(
                        CareerSeed:
                            unchecked(
                                (ulong)(currentHours * 1_000)
                                + (ulong)completedFlights
                                + 17),
                        EngagementKey:
                            $"{path}-{completedFlights}-{currentHours:0.0}",
                        OperationKind: kind,
                        MissionExecutionQuality:
                            Math.Clamp(
                                .72
                                + (double)(
                                    skill.SafetyStart - 80m)
                                  / 100,
                                .55,
                                .95),
                        ThreatExposure: threat,
                        AircraftReadiness:
                            failed ? .72 : .90,
                        SupportFactor:
                            path == CareerPath.MilitaryWarzoneChaser
                                ? .70
                                : .82));

            if (engagement.Outcome
                == SimulatedEngagementOutcome.AbortRecommended)
            {
                safeAbort = true;
                success = false;
            }
            else if (engagement.Outcome
                     == SimulatedEngagementOutcome.MissionDisrupted)
            {
                success =
                    false;
            }
        }

        ContractKind contractKind =
            kind switch
            {
                MilitaryOperationKind.Intercept =>
                    ContractKind.MilitaryIntercept,
                MilitaryOperationKind.Escort =>
                    ContractKind.MilitaryEscort,
                MilitaryOperationKind.Patrol =>
                    ContractKind.MilitaryPatrol,
                MilitaryOperationKind.Transport =>
                    ContractKind.MilitaryTransport,
                MilitaryOperationKind.TankerSupport =>
                    ContractKind.MilitaryTankerSupport,
                MilitaryOperationKind.Surveillance =>
                    ContractKind.MilitarySurveillance,
                _ =>
                    ContractKind.MilitaryTraining
            };

        ContractPayQuote quote =
            ContractPayQuoteEngine.Quote(
                new ContractPayQuoteRequest(
                    ServiceTrack.MilitaryService,
                    contractKind,
                    flightHours,
                    flightHours
                        * (fighterMission ? 420 : 260),
                    PayloadPounds:
                        fighterMission ? 0 : 8_000,
                    DemandAttractiveness: 1,
                    Urgency:
                        highThreat ? .65 : .25,
                    Difficulty:
                        Math.Clamp(
                            .20 + threat * .65,
                            0,
                            1),
                    RelationshipStrength:
                        serviceTrust));

        decimal pay =
            success
                ? quote.PilotCashCompensation
                : safeAbort
                    ? quote.PilotCashCompensation * .70m
                    : quote.PilotCashCompensation * .35m;

        return new MilitaryFlightResult(
            eligibility.IsEligible,
            success,
            safeAbort,
            highThreat,
            pay);
    }

    private static MilitaryOperationKind SelectMilitaryOperation(
        CareerPath path,
        double hours,
        MilitaryQualification qualifications,
        DeterministicRandom random)
    {
        if (path == CareerPath.MilitaryWarzoneChaser
            && qualifications.HasFlag(
                MilitaryQualification.FastJet)
            && hours >= 200)
        {
            int pick =
                random.NextInt(0, 3);

            return pick switch
            {
                0 => MilitaryOperationKind.Intercept,
                1 => MilitaryOperationKind.Escort,
                _ => MilitaryOperationKind.AirSupport
            };
        }

        if (path == CareerPath.MilitaryLogistics
            && qualifications.HasFlag(
                MilitaryQualification.Mobility))
        {
            return random.Chance(.25)
                ? MilitaryOperationKind.AirfieldReinforcement
                : MilitaryOperationKind.Transport;
        }

        if (hours < 40)
            return MilitaryOperationKind.Training;

        if (hours < 100)
            return MilitaryOperationKind.Readiness;

        if (qualifications.HasFlag(
                MilitaryQualification.Mobility)
            && random.Chance(.35))
        {
            return MilitaryOperationKind.Transport;
        }

        return MilitaryOperationKind.Patrol;
    }

    private static MilitaryQualification MilitaryQualificationsFor(
        double hours,
        double serviceTrust)
    {
        MilitaryQualification result =
            MilitaryQualification.ServiceAuthorization;

        if (hours >= 80 && serviceTrust >= .20)
            result |= MilitaryQualification.Mobility;

        if (hours >= 120 && serviceTrust >= .25)
            result |= MilitaryQualification.Surveillance;

        if (hours >= 150 && serviceTrust >= .30)
            result |= MilitaryQualification.SearchAndRescue;

        if (hours >= 200 && serviceTrust >= .45)
            result |= MilitaryQualification.FastJet;

        if (hours >= 300 && serviceTrust >= .40)
            result |= MilitaryQualification.Tanker;

        if (hours >= 350 && serviceTrust >= .35)
            result |= MilitaryQualification.Aeromedical;

        return result;
    }

    private static int AirlineQualificationCount(
        double hours,
        SkillProfile skill)
    {
        double pace =
            skill.Name switch
            {
                "Expert" => .90,
                "Strong" => .96,
                "Average" => 1.02,
                "SlowLearner" => 1.10,
                _ => 1.18
            };

        double[] thresholds =
            [40, 90, 150, 240, 350, 500, 700];

        return thresholds.Count(
            threshold =>
                hours >= threshold * pace);
    }

    private static bool CanAccessAirline(
        AirlineAircraftClass aircraftClass,
        CareerLevelSnapshot level,
        double hours,
        int qualifications,
        EmployerTrustTier trust) =>
        AirlineCareerPolicy.Evaluate(
            aircraftClass,
            level,
            hours,
            qualifications,
            trust)
        .CanFlyEmployerAircraft;

    private static bool IsMilitary(CareerPath path) =>
        path
        is CareerPath.MilitaryOnly
            or CareerPath.MilitaryWarzoneChaser
            or CareerPath.MilitaryLogistics;

    private static bool IsBusiness(CareerPath path) =>
        path
        is CareerPath.CharterBusiness
            or CareerPath.CargoBusiness
            or CareerPath.MedevacBusiness
            or CareerPath.FlightSchoolBusiness
            or CareerPath.RegionalAirlineBusiness;

    private static void AddAggregateIssues(
        IReadOnlyList<ScenarioResult> results,
        List<string> issues)
    {
        decimal airlineOnlyAverage =
            results
                .Where(result =>
                    result.Path
                    == CareerPath.AirlineEmployeeOnly)
                .Average(result => result.NetWorth);

        decimal civilianAverage =
            results
                .Where(result =>
                    result.Path
                    == CareerPath.CivilianEmployee)
                .Average(result => result.NetWorth);

        if (airlineOnlyAverage
            < civilianAverage * .90m)
        {
            issues.Add(
                $"Airline-only career average net worth {airlineOnlyAverage:C} trails civilian employee {civilianAverage:C} too heavily.");
        }

        decimal militaryAverage =
            results
                .Where(result =>
                    result.Path == CareerPath.MilitaryOnly)
                .Average(result => result.EarnedPerHour);

        decimal warzoneAverage =
            results
                .Where(result =>
                    result.Path
                    == CareerPath.MilitaryWarzoneChaser)
                .Average(result => result.EarnedPerHour);

        decimal warzoneRatio =
            warzoneAverage / militaryAverage;

        if (warzoneRatio is < .95m or > 1.35m)
        {
            issues.Add(
                $"Warzone-chaser pay ratio was {warzoneRatio:P1}; expected 95-135% of general military pay.");
        }

        foreach (BusinessProfile business in Businesses)
        {
            ScenarioResult[] businessResults =
                results
                    .Where(result =>
                        result.Path == business.Path)
                    .ToArray();

            double normalSkillBankruptcyRate =
                businessResults
                    .Where(result =>
                        result.Skill
                            is "Average"
                                or "Strong"
                                or "Expert")
                    .Average(result =>
                        result.CompanyBankruptcies > 0
                            ? 1.0
                            : 0.0);

            if (normalSkillBankruptcyRate > .03)
            {
                issues.Add(
                    $"{business.Name} normal-skill bankruptcy rate was {normalSkillBankruptcyRate:P1}.");
            }

            double overallStartRate =
                businessResults.Average(
                    result =>
                        result.CompanyStarted
                            ? 1.0
                            : 0.0);

            if (overallStartRate < .90)
            {
                issues.Add(
                    $"{business.Name} startup rate was only {overallStartRate:P1}.");
            }
        }
    }

    private static string FormatHour(double? hour) =>
        hour is null
            ? "never"
            : $"{hour:0.0}h";
}
