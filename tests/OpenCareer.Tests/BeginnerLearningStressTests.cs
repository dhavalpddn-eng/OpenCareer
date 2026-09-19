using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Tests;

public sealed class BeginnerLearningStressTests
{
    private sealed record BeginnerProfile(
        string Name,
        double RouteErrorChance,
        double RoughLandingChance,
        double UnsafeLandingChance,
        double GoAroundChance,
        double LearningRate);

    private sealed record PlayStyle(
        string Name,
        double AverageFlightHours,
        double NavigationLoad,
        double LandingLoad);

    private sealed record BeginnerResult(
        string Name,
        int Completed,
        int RetryRequired,
        int UnsafeFailures,
        int RoughButCompleted,
        int SafeGoArounds,
        int CareerPenalties,
        int LightGuidanceWhileStillStruggling,
        IReadOnlyList<string> Issues);

    private static readonly BeginnerProfile[] Profiles =
    [
        new("FirstEverFlight", .60, .80, .12, .12, .030),
        new("NoNavigationKnowledge", .82, .58, .07, .10, .028),
        new("RoughLander", .30, .92, .14, .15, .032),
        new("ControllerOnlyNovice", .50, .76, .09, .12, .035),
        new("KeyboardMouseNovice", .58, .84, .11, .10, .030),
        new("AutopilotDependent", .48, .68, .06, .08, .034),
        new("GoAroundAnxious", .38, .72, .05, .32, .030),
        new("ChecklistSkipper", .52, .78, .13, .08, .034),
        new("VisualVfrLearner", .44, .66, .07, .14, .040),
        new("SlowLearner", .70, .88, .15, .16, .018)
    ];

    private static readonly PlayStyle[] Styles =
    [
        new("ShortPractice", 1.0, .75, 1.15),
        new("BalancedJobs", 2.0, 1.0, 1.0),
        new("RouteHeavy", 2.5, 1.30, .85),
        new("LandingPractice", .75, .60, 1.35),
        new("WeekendLongTrip", 4.0, 1.25, 1.05)
    ];

    [Fact]
    public void InitialFiftyBeginnerCareersExposeHarshLearningEdges()
    {
        BeginnerFlightSupportPolicy policy =
            BeginnerFlightSupportPolicy.Default;

        var results = new List<BeginnerResult>(50);
        var issues = new List<string>();

        var seed = 0;
        foreach (BeginnerProfile profile in Profiles)
        {
            foreach (PlayStyle style in Styles)
            {
                BeginnerResult result =
                    Simulate(
                        policy,
                        profile,
                        style,
                        seed++,
                        flights: 30);

                results.Add(result);
                issues.AddRange(
                    result.Issues.Select(
                        issue => $"{result.Name}: {issue}"));
            }
        }

        Assert.Equal(50, results.Count);

        Assert.True(
            issues.Count == 0,
            string.Join(Environment.NewLine, issues));
    }

    private static BeginnerResult Simulate(
        BeginnerFlightSupportPolicy policy,
        BeginnerProfile profile,
        PlayStyle style,
        int seed,
        int flights)
    {
        var random =
            new DeterministicRandom(
                unchecked((ulong)(0xBEE0_1000 + seed * 3571)));

        int completed = 0;
        int retries = 0;
        int unsafeFailures = 0;
        int roughCompleted = 0;
        int safeGoArounds = 0;
        int careerPenalties = 0;
        int lightWhileStruggling = 0;
        double hours = 0;

        var recentDifficulty = new Queue<bool>();
        var issues = new List<string>();

        for (var attemptIndex = 0; attemptIndex < flights; attemptIndex++)
        {
            BeginnerGuidanceIntensity guidance =
                policy.GuidanceFor(
                    completed,
                    hours);

            double progress =
                Math.Min(
                    .65,
                    completed * profile.LearningRate);

            double guidanceRouteMultiplier =
                guidance switch
                {
                    BeginnerGuidanceIntensity.Full => .35,
                    BeginnerGuidanceIntensity.Standard => .60,
                    _ => .88
                };

            double guidanceLandingMultiplier =
                guidance switch
                {
                    BeginnerGuidanceIntensity.Full => .72,
                    BeginnerGuidanceIntensity.Standard => .84,
                    _ => .95
                };

            double routeErrorChance =
                Math.Clamp(
                    profile.RouteErrorChance
                    * style.NavigationLoad
                    * (1 - progress)
                    * guidanceRouteMultiplier,
                    0,
                    .98);

            double roughChance =
                Math.Clamp(
                    profile.RoughLandingChance
                    * style.LandingLoad
                    * (1 - progress * .80)
                    * guidanceLandingMultiplier,
                    0,
                    .98);

            double unsafeChance =
                Math.Clamp(
                    profile.UnsafeLandingChance
                    * style.LandingLoad
                    * (1 - progress)
                    * guidanceLandingMultiplier,
                    0,
                    .45);

            bool routeError =
                random.Chance(routeErrorChance);

            bool recoveredRoute =
                !routeError
                || random.Chance(
                    guidance switch
                    {
                        BeginnerGuidanceIntensity.Full => .82,
                        BeginnerGuidanceIntensity.Standard => .62,
                        _ => .38
                    });

            bool goAround =
                random.Chance(profile.GoAroundChance);

            bool unsafeLanding =
                recoveredRoute
                && random.Chance(unsafeChance);

            bool roughLanding =
                recoveredRoute
                && !unsafeLanding
                && random.Chance(roughChance);

            var attempt =
                new BeginnerFlightAttempt(
                    PriorCompletedFlights: completed,
                    PriorCareerCreditHours: hours,
                    IsTrainingOrPractice: attemptIndex < 8,
                    ReachedPlannedDestination:
                        !routeError,
                    RecoveredToPlannedDestination:
                        routeError && recoveredRoute,
                    RouteDeviationObserved: routeError,
                    MissedPlannedWaypoint: routeError,
                    UsedNavigationAssist:
                        guidance != BeginnerGuidanceIntensity.Light,
                    UsedAutopilot:
                        profile.Name == "AutopilotDependent",
                    GoAroundCount: goAround ? 1 : 0,
                    BounceCount: roughLanding ? 1 : 0,
                    TouchdownVerticalSpeedFeetPerMinute:
                        roughLanding ? -620 : -220,
                    TouchdownG:
                        roughLanding ? 1.85 : 1.25,
                    AircraftDamageObserved:
                        unsafeLanding,
                    RunwayExcursionObserved:
                        unsafeLanding
                        && random.Chance(.45),
                    CrashObserved:
                        unsafeLanding
                        && random.Chance(.18));

            BeginnerFlightAssessment assessment =
                policy.Assess(attempt);

            bool difficulty =
                routeError
                || roughLanding
                || unsafeLanding;

            recentDifficulty.Enqueue(difficulty);
            while (recentDifficulty.Count > 6)
                recentDifficulty.Dequeue();

            if (assessment.Outcome
                is BeginnerFlightOutcome.Completed
                or BeginnerFlightOutcome.CompletedWithCoaching)
            {
                completed++;
                hours += style.AverageFlightHours;
            }
            else if (assessment.Outcome
                     == BeginnerFlightOutcome.RetryRecommended)
            {
                retries++;
            }
            else
            {
                unsafeFailures++;
            }

            if (roughLanding
                && assessment.MissionCompletionAllowed)
            {
                roughCompleted++;
            }

            if (goAround
                && assessment.GoAroundWasSafeChoice)
            {
                safeGoArounds++;
            }

            if (assessment.CareerFailure
                || assessment.ReputationPenaltyAllowed
                || assessment.EconomicPenaltyAllowed)
            {
                careerPenalties++;
            }

            double recentDifficultyRate =
                recentDifficulty.Count == 0
                    ? 0
                    : recentDifficulty.Count(value => value)
                      / (double)recentDifficulty.Count;

            if (guidance == BeginnerGuidanceIntensity.Light
                && recentDifficultyRate >= .50)
            {
                lightWhileStruggling++;
            }
        }

        double completionRate =
            completed / (double)flights;

        if (completionRate < .65)
        {
            issues.Add(
                $"completion rate {completionRate:P0} is too low for an assisted beginner.");
        }

        if (retries > 8)
        {
            issues.Add(
                $"{retries} route-recovery retries are too taxing.");
        }

        if (careerPenalties > 3)
        {
            issues.Add(
                $"{careerPenalties} punitive career consequences occurred during the learning run.");
        }

        if (lightWhileStruggling > 2)
        {
            issues.Add(
                $"guidance fell to Light while the player was still struggling on {lightWhileStruggling} attempts.");
        }

        if (roughCompleted == 0
            && profile.RoughLandingChance >= .70)
        {
            issues.Add(
                "rough landings never reached a successful coached completion.");
        }

        return new BeginnerResult(
            $"{profile.Name}/{style.Name}",
            completed,
            retries,
            unsafeFailures,
            roughCompleted,
            safeGoArounds,
            careerPenalties,
            lightWhileStruggling,
            issues);
    }
}
