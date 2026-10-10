using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Flights;

public enum LiveFlightChecklistPhase
{
    PreflightPreparation = 0,
    EngineStart = 1,
    Taxi = 2,
    Takeoff = 3,
    Airborne = 4,
    ApproachLanding = 5,
    Parking = 6,
    ShutdownCompletion = 7
}

public enum LiveFlightChecklistVerificationMode
{
    Automatic = 0,
    Manual = 1,
    Unavailable = 2
}

public enum LiveFlightChecklistStepState
{
    Pending = 0,
    Satisfied = 1,
    Unavailable = 2
}

public sealed record LiveFlightChecklistStep(
    string Id,
    LiveFlightChecklistPhase Phase,
    string Title,
    LiveFlightChecklistVerificationMode VerificationMode,
    LiveFlightChecklistStepState State,
    string EvidenceDetail);

public sealed record LiveFlightChecklistSnapshot(
    Guid? SessionId,
    LiveFlightChecklistPhase? CurrentPhase,
    FlightSessionStatus? SessionStatus,
    IReadOnlyList<LiveFlightChecklistStep> Steps,
    string Detail)
{
    public static LiveFlightChecklistSnapshot NoSession { get; } =
        new(
            null,
            null,
            null,
            Array.Empty<LiveFlightChecklistStep>(),
            "No current FlightSession is available for a live checklist.");
}

public interface ILiveFlightChecklistSource
{
    LiveFlightChecklistSnapshot Read();
}

public sealed class LiveFlightChecklistSource(
    FlightSessionCoordinator flightSessions,
    IFlightStateEvidenceSource evidenceSource) : ILiveFlightChecklistSource
{
    private readonly FlightSessionCoordinator _flightSessions =
        flightSessions ?? throw new ArgumentNullException(nameof(flightSessions));

    private readonly IFlightStateEvidenceSource _evidenceSource =
        evidenceSource ?? throw new ArgumentNullException(nameof(evidenceSource));

    public LiveFlightChecklistSnapshot Read()
    {
        FlightSession? session = _flightSessions.Current;
        if (session is null)
            return LiveFlightChecklistSnapshot.NoSession;

        DateTimeOffset legStartedAt =
            session.EffectiveLegs[^1].StartedAt;

        LiveFlightChecklistPhase phase = ResolvePhase(session);
        var steps = new List<LiveFlightChecklistStep>(17);

        AddAutomatic(steps, "aircraft-ready", LiveFlightChecklistPhase.PreflightPreparation,
            "Aircraft readiness established", session.Milestones.AircraftReadyAt, legStartedAt, session);
        AddUnavailable(steps, "cockpit-preflight", LiveFlightChecklistPhase.PreflightPreparation,
            "Aircraft-specific cockpit preflight");

        AddAutomatic(steps, "engine-start", LiveFlightChecklistPhase.EngineStart,
            "Engine start observed", session.Milestones.EngineStartAt, legStartedAt, session);
        AddManual(steps, "before-taxi", LiveFlightChecklistPhase.EngineStart,
            "Before-taxi configuration reviewed");

        AddAutomatic(steps, "taxi-out", LiveFlightChecklistPhase.Taxi,
            "Taxi-out movement observed", session.Milestones.TaxiOutAt, legStartedAt, session);
        AddManual(steps, "flight-controls", LiveFlightChecklistPhase.Taxi,
            "Flight-controls check completed");

        AddAutomatic(steps, "takeoff-roll", LiveFlightChecklistPhase.Takeoff,
            "Takeoff roll observed", session.Milestones.TakeoffRollAt, legStartedAt, session);
        AddAutomatic(steps, "takeoff", LiveFlightChecklistPhase.Takeoff,
            "Takeoff confirmed", session.Milestones.TakeoffAt, legStartedAt, session);

        AddAutomatic(steps, "airborne", LiveFlightChecklistPhase.Airborne,
            "Airborne flight confirmed", session.Milestones.TakeoffAt, legStartedAt, session);
        AddManual(steps, "enroute-procedures", LiveFlightChecklistPhase.Airborne,
            "Enroute procedures reviewed");

        AddAutomatic(steps, "approach", LiveFlightChecklistPhase.ApproachLanding,
            "Approach confirmed", session.Milestones.ApproachAt, legStartedAt, session);
        AddManual(steps, "landing-configuration", LiveFlightChecklistPhase.ApproachLanding,
            "Landing configuration reviewed");
        AddAutomatic(steps, "landing-rollout", LiveFlightChecklistPhase.ApproachLanding,
            "Landing rollout confirmed", session.Milestones.LandingAt, legStartedAt, session);

        AddAutomatic(steps, "taxi-in", LiveFlightChecklistPhase.Parking,
            "Taxi-in confirmed", session.Milestones.TaxiInAt, legStartedAt, session);
        AddAutomatic(steps, "parked", LiveFlightChecklistPhase.Parking,
            "Parking confirmed", session.Milestones.ParkedAt, legStartedAt, session);

        AddAutomatic(steps, "shutdown", LiveFlightChecklistPhase.ShutdownCompletion,
            "Shutdown confirmed", session.Milestones.ShutdownAt, legStartedAt, session);
        AddAutomatic(steps, "operation-complete", LiveFlightChecklistPhase.ShutdownCompletion,
            "Operation completion confirmed", session.Milestones.CompletedAt, legStartedAt, session);

        return new LiveFlightChecklistSnapshot(
            session.SessionId,
            phase,
            session.Status,
            steps,
            BuildDetail(session));
    }

    private void AddAutomatic(
        ICollection<LiveFlightChecklistStep> steps,
        string id,
        LiveFlightChecklistPhase phase,
        string title,
        DateTimeOffset? milestone,
        DateTimeOffset legStartedAt,
        FlightSession session)
    {
        DateTimeOffset? verifiedAt =
            milestone is { } candidate && candidate >= legStartedAt
                ? candidate
                : null;
        bool satisfied = verifiedAt is not null;
        steps.Add(
            new LiveFlightChecklistStep(
                id,
                phase,
                title,
                LiveFlightChecklistVerificationMode.Automatic,
                satisfied
                    ? LiveFlightChecklistStepState.Satisfied
                    : LiveFlightChecklistStepState.Pending,
                satisfied
                    ? $"Verified from persisted flight evidence at {verifiedAt.Value:O}."
                    : PendingDetail(session)));
    }

    private static void AddManual(
        ICollection<LiveFlightChecklistStep> steps,
        string id,
        LiveFlightChecklistPhase phase,
        string title) =>
        steps.Add(
            new LiveFlightChecklistStep(
                id,
                phase,
                title,
                LiveFlightChecklistVerificationMode.Manual,
                LiveFlightChecklistStepState.Pending,
                "Manual pilot action; no trustworthy universal automatic evidence is available."));

    private static void AddUnavailable(
        ICollection<LiveFlightChecklistStep> steps,
        string id,
        LiveFlightChecklistPhase phase,
        string title) =>
        steps.Add(
            new LiveFlightChecklistStep(
                id,
                phase,
                title,
                LiveFlightChecklistVerificationMode.Unavailable,
                LiveFlightChecklistStepState.Unavailable,
                "Automatic verification is unavailable; use aircraft-specific simulator guidance."));

    private string PendingDetail(FlightSession session)
    {
        if (session.Status is FlightSessionStatus.Suspended or FlightSessionStatus.Interrupted
            || session.Tracking.State is FlightTrackingState.Suspended or FlightTrackingState.Interrupted)
        {
            return "Pending; the saved session is suspended or interrupted and its verified progress is retained.";
        }

        FlightStateEvidence? evidence = _evidenceSource.Current;
        if (evidence is null || !evidence.Connected || !evidence.StableTelemetry)
            return "Pending; trustworthy live flight evidence is unavailable.";

        return "Pending authoritative flight evidence.";
    }

    private string BuildDetail(FlightSession session)
    {
        if (session.Status is FlightSessionStatus.Suspended or FlightSessionStatus.Interrupted
            || session.Tracking.State is FlightTrackingState.Suspended or FlightTrackingState.Interrupted)
        {
            return "Saved checklist progress is retained. Automatic verification resumes only with authoritative flight evidence.";
        }

        FlightStateEvidence? evidence = _evidenceSource.Current;
        return evidence is { Connected: true, StableTelemetry: true }
            ? "Automatic items use persisted FlightSession milestones; aircraft-specific procedures remain manual."
            : "Persisted checklist progress is shown; trustworthy live evidence is currently unavailable.";
    }

    private static LiveFlightChecklistPhase ResolvePhase(FlightSession session)
    {
        if (session.Status == FlightSessionStatus.Completed
            || session.OperationState is FlightOperationState.Shutdown or FlightOperationState.Complete)
        {
            return LiveFlightChecklistPhase.ShutdownCompletion;
        }

        FlightTrackingState state =
            session.Tracking.State == FlightTrackingState.Suspended
                && session.Tracking.SuspendedFrom is { } suspendedFrom
                    ? suspendedFrom
                    : session.Tracking.State;

        if (state == FlightTrackingState.Interrupted)
            return ResolveOperationPhase(session.OperationState);

        return state switch
        {
            FlightTrackingState.Observing or FlightTrackingState.Preflight =>
                LiveFlightChecklistPhase.PreflightPreparation,
            FlightTrackingState.EngineStart => LiveFlightChecklistPhase.EngineStart,
            FlightTrackingState.TaxiOut => LiveFlightChecklistPhase.Taxi,
            FlightTrackingState.TakeoffRoll => LiveFlightChecklistPhase.Takeoff,
            FlightTrackingState.Airborne => LiveFlightChecklistPhase.Airborne,
            FlightTrackingState.Approach or FlightTrackingState.LandingEpisode =>
                LiveFlightChecklistPhase.ApproachLanding,
            FlightTrackingState.TaxiIn or FlightTrackingState.Parked =>
                LiveFlightChecklistPhase.Parking,
            FlightTrackingState.Complete => LiveFlightChecklistPhase.ShutdownCompletion,
            _ => ResolveOperationPhase(session.OperationState)
        };
    }

    private static LiveFlightChecklistPhase ResolveOperationPhase(FlightOperationState state) =>
        state switch
        {
            FlightOperationState.Accepted or
            FlightOperationState.Preparation or
            FlightOperationState.Servicing or
            FlightOperationState.Loading or
            FlightOperationState.ReadyForStart => LiveFlightChecklistPhase.PreflightPreparation,
            FlightOperationState.EngineStart or FlightOperationState.Ramp =>
                LiveFlightChecklistPhase.EngineStart,
            FlightOperationState.TaxiOut => LiveFlightChecklistPhase.Taxi,
            FlightOperationState.DepartureReady => LiveFlightChecklistPhase.Takeoff,
            FlightOperationState.Airborne => LiveFlightChecklistPhase.Airborne,
            FlightOperationState.Landed => LiveFlightChecklistPhase.ApproachLanding,
            FlightOperationState.TaxiIn or FlightOperationState.Parked or FlightOperationState.Unloading =>
                LiveFlightChecklistPhase.Parking,
            FlightOperationState.Shutdown or FlightOperationState.Complete =>
                LiveFlightChecklistPhase.ShutdownCompletion,
            _ => LiveFlightChecklistPhase.PreflightPreparation
        };
}
