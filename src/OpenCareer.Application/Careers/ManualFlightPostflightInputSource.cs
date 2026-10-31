using OpenCareer.Application.Flights;
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
    AircraftDebriefUnavailable,
    FlightSessionChanged
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

    public bool CanDiscard =>
        SessionId is not null
        && State is (
            ManualFlightPostflightInputState.Ready
            or ManualFlightPostflightInputState.FlightNotCompleted
            or ManualFlightPostflightInputState.FlightNotLoggable
            or ManualFlightPostflightInputState.AircraftIdentityUnavailable
            or ManualFlightPostflightInputState.AircraftIdentityConflict
            or ManualFlightPostflightInputState.AircraftDebriefUnavailable);
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
        CancellationToken cancellationToken = default) =>
        await ReadCurrentCoreAsync(
                requestedAt,
                retryAfterSessionChange: true,
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<ManualFlightPostflightInputSnapshot> ReadCurrentCoreAsync(
        DateTimeOffset requestedAt,
        bool retryAfterSessionChange,
        CancellationToken cancellationToken)
    {
        if (requestedAt == default)
            throw new ArgumentOutOfRangeException(nameof(requestedAt));

        FlightSession? session = _sessions.Current;
        if (session is null)
            return Blocked(ManualFlightPostflightInputState.NoFlightSession, null, "No FlightSession is available for manual postflight processing.");

        if (session.ContractId is not null)
            return Blocked(ManualFlightPostflightInputState.ContractLinked, session.SessionId, "Contract-linked FlightSessions cannot use manual postflight logging.");

        if (!session.IsTerminal)
            return Blocked(
                ManualFlightPostflightInputState.FlightNotCompleted,
                session.SessionId,
                "This contractless flight is still active. It can be discarded without Logbook, experience, or qualification credit so another flight can start.");

        if (session.Status != FlightSessionStatus.Completed
            || session.OperationState != FlightOperationState.Complete
            || session.Tracking.State != FlightTrackingState.Complete
            || session.Tracking.CrashReported)
        {
            return Blocked(ManualFlightPostflightInputState.FlightNotLoggable, session.SessionId, "This unsuccessful terminal FlightSession cannot be logged, but it can be discarded without credit so another flight can start.");
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

        if (_sessions.Current?.SessionId != session.SessionId)
        {
            if (retryAfterSessionChange)
            {
                return await ReadCurrentCoreAsync(
                        requestedAt,
                        retryAfterSessionChange: false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return Blocked(
                ManualFlightPostflightInputState.FlightSessionChanged,
                sessionId: null,
                "The current FlightSession changed while postflight evidence was loading. Reopen Current Flight before logging or discarding.");
        }

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
