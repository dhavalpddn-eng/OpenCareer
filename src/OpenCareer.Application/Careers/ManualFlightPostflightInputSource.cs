using OpenCareer.Application.Logbook;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Careers;

public enum ManualFlightPostflightInputState
{
    Ready = 0,
    NoFlightSession,
    FlightNotCompleted,
    ContractLinked,
    FlightNotLoggable,
    AircraftIdentityUnavailable,
    AircraftIdentityConflict,
    AircraftDebriefUnavailable
}

public sealed record ManualFlightPostflightInputSnapshot(
    ManualFlightPostflightInputState State,
    Guid? SessionId,
    ManualFlightPostflightLogRequest? Request,
    string Detail)
{
    public bool IsReady =>
        State == ManualFlightPostflightInputState.Ready
        && Request is not null;
}

public sealed class ManualFlightPostflightInputSource
{
    private readonly FlightSessionCoordinator _sessions;
    private readonly ICurrentLoadedAircraftIdentitySource _loadedAircraft;
    private readonly IAircraftRegistrySource _aircraftRegistry;

    public ManualFlightPostflightInputSource(
        FlightSessionCoordinator sessions,
        ICurrentLoadedAircraftIdentitySource loadedAircraft,
        IAircraftRegistrySource aircraftRegistry)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _loadedAircraft = loadedAircraft ?? throw new ArgumentNullException(nameof(loadedAircraft));
        _aircraftRegistry = aircraftRegistry ?? throw new ArgumentNullException(nameof(aircraftRegistry));
    }

    public async Task<ManualFlightPostflightInputSnapshot> ReadCurrentAsync(
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default)
    {
        if (requestedAt == default)
            throw new ArgumentOutOfRangeException(nameof(requestedAt));

        FlightSession? session = _sessions.Current;
        if (session is null)
            return Blocked(ManualFlightPostflightInputState.NoFlightSession, null, "No FlightSession is available for manual postflight processing.");

        if (session.ContractId is not null)
            return Blocked(ManualFlightPostflightInputState.ContractLinked, session.SessionId, "Contract-linked FlightSessions cannot use manual postflight logging.");

        if (!session.IsTerminal)
            return Blocked(ManualFlightPostflightInputState.FlightNotCompleted, session.SessionId, "The FlightSession has not reached a terminal state.");

        if (session.Status != FlightSessionStatus.Completed
            || session.OperationState != FlightOperationState.Complete
            || session.Tracking.State != FlightTrackingState.Complete
            || session.Tracking.CrashReported)
        {
            return Blocked(ManualFlightPostflightInputState.FlightNotLoggable, session.SessionId, "Crashed, interrupted, cancelled, or otherwise unsuccessful FlightSessions cannot be manually logged.");
        }

        DateTimeOffset endedAt = session.Milestones.CompletedAt ?? session.UpdatedAt;
        if (requestedAt < endedAt)
            throw new ArgumentOutOfRangeException(nameof(requestedAt), "Postflight request time cannot precede FlightSession completion.");

        CurrentLoadedAircraftIdentitySnapshot loaded = _loadedAircraft.Current;
        loaded.Validate();
        if (loaded.Status != CurrentLoadedAircraftIdentityStatus.Identified
            || string.IsNullOrWhiteSpace(loaded.CanonicalAircraftId))
        {
            return Blocked(ManualFlightPostflightInputState.AircraftIdentityUnavailable, session.SessionId, "Authoritative loaded-aircraft identity is unavailable.");
        }

        string canonicalAircraftId = loaded.CanonicalAircraftId.Trim();
        string? plannedAircraftId = session.Plan?.ExpectedCanonicalAircraftId;
        if (!string.IsNullOrWhiteSpace(plannedAircraftId)
            && !string.Equals(plannedAircraftId.Trim(), canonicalAircraftId, StringComparison.OrdinalIgnoreCase))
        {
            return Blocked(ManualFlightPostflightInputState.AircraftIdentityConflict, session.SessionId, "Loaded-aircraft identity conflicts with the FlightSession plan.");
        }

        AircraftRegistryResolution? aircraft = await _aircraftRegistry
            .FindAircraftAsync(canonicalAircraftId, cancellationToken)
            .ConfigureAwait(false);
        string? displayName = aircraft?.CapabilityValues.DisplayName;
        if (aircraft is null
            || aircraft.InstallationStatus != AircraftInstallationStatus.Installed
            || !string.Equals(aircraft.CanonicalAircraftId, canonicalAircraftId, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(displayName))
        {
            return Blocked(ManualFlightPostflightInputState.AircraftDebriefUnavailable, session.SessionId, "Aircraft registry evidence cannot resolve one authoritative installed-aircraft display identity.");
        }

        var context = new FlightSessionDebriefContext(
            LogbookEntryKind.FreeFlight,
            new AircraftDebrief(displayName.Trim()),
            ActualDeparture: null,
            ActualArrival: null,
            DiversionLocation: null,
            new PayloadDebrief(null, null, null, null, EvidenceQuality.Unavailable),
            FlightSafetyOutcome.CompletedNormally,
            MissionOutcome.NotApplicable,
            FlightSettlementRecord.NotApplicable);

        return new(
            ManualFlightPostflightInputState.Ready,
            session.SessionId,
            new ManualFlightPostflightLogRequest(context, requestedAt, requestedAt),
            "Manual free-flight postflight evidence is ready.");
    }

    private static ManualFlightPostflightInputSnapshot Blocked(
        ManualFlightPostflightInputState state,
        Guid? sessionId,
        string detail) =>
        new(state, sessionId, Request: null, detail);
}
