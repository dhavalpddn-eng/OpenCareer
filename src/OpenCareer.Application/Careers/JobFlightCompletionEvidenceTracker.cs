using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Careers;

public sealed record JobFlightCompletionEvidenceSnapshot(
    Guid ContractId,
    Guid FlightSessionId,
    FlightSessionStatus SessionStatus,
    FlightOperationState OperationState,
    DateTimeOffset SessionUpdatedAt,
    DateTimeOffset? LatestEvidenceAt,
    bool LatestEvidenceConnected,
    bool LatestEvidenceStableTelemetry,
    bool LatestEvidenceValidLoadedAircraft,
    bool LatestEvidenceContinuityPlausible,
    bool TakeoffObserved,
    bool AirborneObserved,
    bool TouchdownObserved,
    bool ParkingObserved,
    bool ShutdownObserved,
    bool OperationCompleteObserved,
    bool CrashReported)
{
    public bool HasTrustworthyLatestEvidence =>
        LatestEvidenceConnected
        && LatestEvidenceStableTelemetry
        && LatestEvidenceValidLoadedAircraft
        && LatestEvidenceContinuityPlausible;

    public bool CoreFlightSequenceObserved =>
        TakeoffObserved
        && AirborneObserved
        && TouchdownObserved
        && ParkingObserved
        && ShutdownObserved
        && !CrashReported;

    public bool IsFailedOrCancelled =>
        SessionStatus
            is FlightSessionStatus.Interrupted
                or FlightSessionStatus.Cancelled
        || CrashReported;
}

public sealed class JobFlightCompletionEvidenceTracker
{
    private readonly FlightSessionCoordinator _flightSessions;
    private readonly IFlightStateEvidenceSource _evidenceSource;
    private readonly object _gate = new();

    private JobFlightCompletionEvidenceSnapshot? _current;
    private Guid? _currentSessionId;

    public JobFlightCompletionEvidenceTracker(
        FlightSessionCoordinator flightSessions,
        IFlightStateEvidenceSource evidenceSource)
    {
        _flightSessions =
            flightSessions
            ?? throw new ArgumentNullException(nameof(flightSessions));
        _evidenceSource =
            evidenceSource
            ?? throw new ArgumentNullException(nameof(evidenceSource));

        _flightSessions.SessionChanged +=
            OnSessionChanged;
        _evidenceSource.EvidenceChanged +=
            OnEvidenceChanged;

        RefreshForSessionChange(
            _flightSessions.Current);
    }

    public JobFlightCompletionEvidenceSnapshot? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public event Action<JobFlightCompletionEvidenceSnapshot?>? EvidenceChanged;

    private void OnSessionChanged(
        object? sender,
        FlightSessionChangedEventArgs args) =>
        RefreshForSessionChange(
            args.Session);

    private void OnEvidenceChanged(
        FlightStateEvidence? evidence)
    {
        FlightSession? session =
            _flightSessions.Current;

        if (session?.ContractId is null)
        {
            PublishIfChanged(null);
            return;
        }

        if (_currentSessionId
            != session.SessionId)
        {
            RefreshForSessionChange(session);
            return;
        }

        PublishIfChanged(
            CreateSnapshot(
                session,
                evidence));
    }

    private void RefreshForSessionChange(
        FlightSession? session)
    {
        if (session?.ContractId is null)
        {
            _currentSessionId = null;
            PublishIfChanged(null);
            return;
        }

        bool sameSession =
            _currentSessionId
            == session.SessionId;

        _currentSessionId =
            session.SessionId;

        FlightStateEvidence? evidence =
            sameSession
                ? _evidenceSource.Current
                : null;

        PublishIfChanged(
            CreateSnapshot(
                session,
                evidence));
    }

    private void PublishIfChanged(
        JobFlightCompletionEvidenceSnapshot? next)
    {
        JobFlightCompletionEvidenceSnapshot? previous;

        lock (_gate)
        {
            previous = _current;

            if (previous == next)
                return;

            _current = next;
        }

        EvidenceChanged?.Invoke(next);
    }

    private static JobFlightCompletionEvidenceSnapshot CreateSnapshot(
        FlightSession session,
        FlightStateEvidence? evidence)
    {
        Guid contractId =
            session.ContractId
            ?? throw new InvalidOperationException(
                "Job completion evidence requires a contract-linked FlightSession.");

        bool takeoffObserved =
            session.Tracking.TakeoffCount > 0
            || session.Milestones.TakeoffAt is not null
            || evidence?.AirborneConfirmed == true;

        bool airborneObserved =
            session.Tracking.TakeoffCount > 0
            || session.Milestones.TakeoffAt is not null
            || evidence?.AirborneConfirmed == true;

        bool touchdownObserved =
            session.Tracking.LandingEpisodeCount > 0
            || session.Milestones.FirstTouchdownAt is not null
            || evidence?.TouchdownConfirmed == true;

        bool parkingObserved =
            session.Milestones.ParkedAt is not null
            || session.OperationState
                is FlightOperationState.Parked
                    or FlightOperationState.Unloading
                    or FlightOperationState.Shutdown
                    or FlightOperationState.Complete
            || evidence?.ParkingConfirmed == true;

        bool shutdownObserved =
            session.Milestones.ShutdownAt is not null
            || session.OperationState
                is FlightOperationState.Shutdown
                    or FlightOperationState.Complete;

        bool operationCompleteObserved =
            session.Status
                == FlightSessionStatus.Completed
            || session.OperationState
                == FlightOperationState.Complete
            || session.Milestones.CompletedAt is not null
            || evidence?.OperationCompleteConfirmed == true;

        bool crashReported =
            session.Tracking.CrashReported
            || evidence?.CrashReported == true;

        return new(
            contractId,
            session.SessionId,
            session.Status,
            session.OperationState,
            session.UpdatedAt,
            evidence?.Timestamp,
            evidence?.Connected == true,
            evidence?.StableTelemetry == true,
            evidence?.ValidLoadedAircraft == true,
            evidence?.ContinuityPlausible == true,
            takeoffObserved,
            airborneObserved,
            touchdownObserved,
            parkingObserved,
            shutdownObserved,
            operationCompleteObserved,
            crashReported);
    }
}
