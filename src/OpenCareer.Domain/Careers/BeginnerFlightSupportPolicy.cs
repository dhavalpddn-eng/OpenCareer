namespace OpenCareer.Domain.Careers;

public enum BeginnerFlightOutcome
{
    Completed,
    CompletedWithCoaching,
    RetryRecommended,
    FailedUnsafe
}

public enum BeginnerGuidanceIntensity
{
    Full,
    Standard,
    Light
}

public sealed record BeginnerFlightAttempt(
    int PriorCompletedFlights,
    double PriorCareerCreditHours,
    bool IsTrainingOrPractice,
    bool ReachedPlannedDestination,
    bool RecoveredToPlannedDestination,
    bool RouteDeviationObserved,
    bool MissedPlannedWaypoint,
    bool UsedNavigationAssist,
    bool UsedAutopilot,
    int GoAroundCount,
    int BounceCount,
    double? TouchdownVerticalSpeedFeetPerMinute,
    double? TouchdownG,
    bool AircraftDamageObserved,
    bool RunwayExcursionObserved,
    bool CrashObserved)
{
    public void Validate()
    {
        if (PriorCompletedFlights < 0
            || !double.IsFinite(PriorCareerCreditHours)
            || PriorCareerCreditHours < 0
            || GoAroundCount < 0
            || BounceCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BeginnerFlightAttempt));
        }

        if (TouchdownVerticalSpeedFeetPerMinute is { } fpm
            && !double.IsFinite(fpm))
        {
            throw new ArgumentOutOfRangeException(
                nameof(TouchdownVerticalSpeedFeetPerMinute));
        }

        if (TouchdownG is { } g
            && (!double.IsFinite(g) || g < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(TouchdownG));
        }
    }
}

public sealed record BeginnerFlightAssessment(
    BeginnerFlightOutcome Outcome,
    BeginnerGuidanceIntensity GuidanceIntensity,
    bool CareerFailure,
    bool ReputationPenaltyAllowed,
    bool EconomicPenaltyAllowed,
    bool MissionCompletionAllowed,
    bool SmoothLandingRequired,
    bool RoutePerfectionRequired,
    bool NavigationAssistRecommended,
    bool LandingCoachRecommended,
    bool GoAroundWasSafeChoice,
    string CoachingCode);

public sealed record BeginnerFlightSupportPolicy(
    int FullGuidanceCompletedFlights,
    int StandardGuidanceCompletedFlights,
    double FullGuidanceCareerHours,
    double StandardGuidanceCareerHours,
    int TrainingFailureProtectionFlights)
{
    public static BeginnerFlightSupportPolicy Default { get; } =
        new(
            FullGuidanceCompletedFlights: 10,
            StandardGuidanceCompletedFlights: 25,
            FullGuidanceCareerHours: 20,
            StandardGuidanceCareerHours: 50,
            TrainingFailureProtectionFlights: 12);

    public void Validate()
    {
        if (FullGuidanceCompletedFlights < 0
            || StandardGuidanceCompletedFlights < FullGuidanceCompletedFlights
            || !double.IsFinite(FullGuidanceCareerHours)
            || !double.IsFinite(StandardGuidanceCareerHours)
            || FullGuidanceCareerHours < 0
            || StandardGuidanceCareerHours < FullGuidanceCareerHours
            || TrainingFailureProtectionFlights < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BeginnerFlightSupportPolicy));
        }
    }

    public BeginnerGuidanceIntensity GuidanceFor(
        int completedFlights,
        double careerCreditHours)
    {
        Validate();

        if (completedFlights < 0
            || !double.IsFinite(careerCreditHours)
            || careerCreditHours < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedFlights));
        }

        if (completedFlights < FullGuidanceCompletedFlights
            || careerCreditHours < FullGuidanceCareerHours)
        {
            return BeginnerGuidanceIntensity.Full;
        }

        if (completedFlights < StandardGuidanceCompletedFlights
            || careerCreditHours < StandardGuidanceCareerHours)
        {
            return BeginnerGuidanceIntensity.Standard;
        }

        return BeginnerGuidanceIntensity.Light;
    }

    public BeginnerFlightAssessment Assess(
        BeginnerFlightAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        attempt.Validate();
        Validate();

        BeginnerGuidanceIntensity guidance =
            GuidanceFor(
                attempt.PriorCompletedFlights,
                attempt.PriorCareerCreditHours);

        bool protectedTraining =
            attempt.IsTrainingOrPractice
            && attempt.PriorCompletedFlights
                < TrainingFailureProtectionFlights;

        bool unsafeFailure =
            attempt.CrashObserved
            || attempt.RunwayExcursionObserved;

        if (unsafeFailure)
        {
            return new BeginnerFlightAssessment(
                BeginnerFlightOutcome.FailedUnsafe,
                guidance,
                CareerFailure: !protectedTraining,
                ReputationPenaltyAllowed: !protectedTraining,
                EconomicPenaltyAllowed: !protectedTraining,
                MissionCompletionAllowed: false,
                SmoothLandingRequired: false,
                RoutePerfectionRequired: false,
                NavigationAssistRecommended: true,
                LandingCoachRecommended: true,
                GoAroundWasSafeChoice: false,
                CoachingCode:
                    protectedTraining
                        ? "training-unsafe-retry-protected"
                        : "unsafe-flight-failure");
        }

        bool destinationRecovered =
            attempt.ReachedPlannedDestination
            || attempt.RecoveredToPlannedDestination;

        if (!destinationRecovered)
        {
            return new BeginnerFlightAssessment(
                BeginnerFlightOutcome.RetryRecommended,
                guidance,
                CareerFailure: false,
                ReputationPenaltyAllowed: false,
                EconomicPenaltyAllowed: false,
                MissionCompletionAllowed: false,
                SmoothLandingRequired: false,
                RoutePerfectionRequired: false,
                NavigationAssistRecommended: true,
                LandingCoachRecommended: false,
                GoAroundWasSafeChoice: attempt.GoAroundCount > 0,
                CoachingCode: "route-recovery-required");
        }

        bool roughLanding =
            attempt.BounceCount > 0
            || attempt.AircraftDamageObserved
            || attempt.TouchdownVerticalSpeedFeetPerMinute is <= -450
            || attempt.TouchdownG is >= 1.75;

        bool navigationLearning =
            attempt.RouteDeviationObserved
            || attempt.MissedPlannedWaypoint;

        bool coaching =
            roughLanding
            || navigationLearning
            || attempt.GoAroundCount > 0;

        return new BeginnerFlightAssessment(
            coaching
                ? BeginnerFlightOutcome.CompletedWithCoaching
                : BeginnerFlightOutcome.Completed,
            guidance,
            CareerFailure: false,
            ReputationPenaltyAllowed:
                attempt.AircraftDamageObserved
                && !protectedTraining,
            EconomicPenaltyAllowed:
                attempt.AircraftDamageObserved
                && !protectedTraining,
            MissionCompletionAllowed: true,
            SmoothLandingRequired: false,
            RoutePerfectionRequired: false,
            NavigationAssistRecommended:
                navigationLearning
                || guidance != BeginnerGuidanceIntensity.Light,
            LandingCoachRecommended:
                roughLanding
                || guidance != BeginnerGuidanceIntensity.Light,
            GoAroundWasSafeChoice: attempt.GoAroundCount > 0,
            CoachingCode:
                roughLanding
                    ? "safe-rough-landing-coaching"
                    : navigationLearning
                        ? "route-coaching"
                        : attempt.GoAroundCount > 0
                            ? "safe-go-around"
                            : "normal-completion");
    }
}
