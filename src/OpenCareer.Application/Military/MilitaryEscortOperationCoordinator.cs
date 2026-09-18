using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Military;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Application.Military;

public sealed record MilitaryEscortOperationStartRequest(
    MilitaryOperationPlan Plan,
    MilitaryOperationEligibility Eligibility,
    SimulatorMissionAircraftSpawnRequest ProtectedAircraft,
    EscortObjectiveProfile ObjectiveProfile,
    IReadOnlyList<SimulatedThreatZone> ThreatZones,
    DateTimeOffset StartTime,
    double MaximumPlayerTelemetryAgeSeconds = 5)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Plan);
        ArgumentNullException.ThrowIfNull(Eligibility);
        ArgumentNullException.ThrowIfNull(ProtectedAircraft);
        ArgumentNullException.ThrowIfNull(ObjectiveProfile);
        ArgumentNullException.ThrowIfNull(ThreatZones);

        Plan.Validate();
        ProtectedAircraft.Validate();
        ObjectiveProfile.Validate();

        if (Plan.Kind != MilitaryOperationKind.Escort)
            throw new ArgumentException("The escort coordinator requires an escort operation plan.", nameof(Plan));

        if (!Eligibility.IsEligible)
            throw new InvalidOperationException("Military operation eligibility must be verified before starting an escort.");

        if (!double.IsFinite(MaximumPlayerTelemetryAgeSeconds)
            || MaximumPlayerTelemetryAgeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumPlayerTelemetryAgeSeconds));
        }

        foreach (var zone in ThreatZones)
            zone.Validate();
    }
}

public sealed record MilitaryEscortRuntimeFrame(
    DateTimeOffset Time,
    bool HasStableFlightState,
    bool AtOrigin,
    bool IsAirborne,
    bool InObjectiveArea,
    bool AtRecoveryAirfield,
    bool ParkedAndSecured,
    double DeltaSeconds,
    bool AbortRequested = false,
    bool FailureDetected = false)
{
    public void Validate(DateTimeOffset previousTime)
    {
        if (Time < previousTime)
            throw new ArgumentException("Escort runtime frames cannot move backward in time.");

        if (!double.IsFinite(DeltaSeconds) || DeltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(DeltaSeconds));

        if (AtOrigin && IsAirborne)
            throw new ArgumentException("An origin-ground frame cannot also be airborne.");

        if (ParkedAndSecured && IsAirborne)
            throw new ArgumentException("A parked escort aircraft cannot also be airborne.");
    }
}

public sealed record MilitaryEscortOperationSnapshot(
    MilitaryMissionProgress Mission,
    EscortObjectiveProgress Escort,
    MilitaryThreatExposure ThreatExposure,
    SimulatorMissionActorHandle ProtectedAircraft,
    bool ProtectedAircraftHasTelemetry,
    bool ProtectedAircraftActive,
    DateTimeOffset UpdatedAt);

public sealed class MilitaryEscortOperationCoordinator : IAsyncDisposable
{
    private readonly ISimulatorTelemetrySource _telemetrySource;
    private readonly ISimulatorMissionActorService _actorService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveEscort? _active;
    private MilitaryEscortOperationSnapshot? _current;

    public MilitaryEscortOperationCoordinator(
        ISimulatorTelemetrySource telemetrySource,
        ISimulatorMissionActorService actorService)
    {
        ArgumentNullException.ThrowIfNull(telemetrySource);
        ArgumentNullException.ThrowIfNull(actorService);

        _telemetrySource = telemetrySource;
        _actorService = actorService;
    }

    public MilitaryEscortOperationSnapshot? Current =>
        Volatile.Read(ref _current);

    public async Task<MilitaryEscortOperationSnapshot> StartAsync(
        MilitaryEscortOperationStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_active is not null)
                throw new InvalidOperationException("A military escort operation is already active.");

            SimulatorMissionActorHandle actor =
                await _actorService
                    .SpawnEnrouteAircraftAsync(request.ProtectedAircraft)
                    .ConfigureAwait(false);

            MilitaryMissionProgress mission =
                MilitaryMissionProgress
                    .Briefed(request.Plan, request.StartTime)
                    .Accept(
                        request.Plan,
                        request.Eligibility,
                        request.StartTime);

            var active = new ActiveEscort(
                request,
                actor,
                mission,
                EscortObjectiveProgress.Create(request.StartTime),
                ZeroThreat(),
                ProtectedAircraftActive: true);

            _active = active;
            return Publish(active, protectedAircraftHasTelemetry: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MilitaryEscortOperationSnapshot> AdvanceAsync(
        MilitaryEscortRuntimeFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ActiveEscort active = _active
                ?? throw new InvalidOperationException("No military escort operation is active.");

            frame.Validate(active.Mission.UpdatedAt);

            AircraftTelemetrySnapshot? player = _telemetrySource.Latest;
            SimulatorMissionActorSnapshot? protectedActor =
                _actorService.Actors.FirstOrDefault(
                    actor => actor.Handle.ObjectId == active.Actor.ObjectId
                        && string.Equals(
                            actor.Handle.ActorKey,
                            active.Actor.ActorKey,
                            StringComparison.Ordinal));

            bool playerFresh = IsPlayerTelemetryFresh(
                player,
                frame.Time,
                active.Request.MaximumPlayerTelemetryAgeSeconds);

            EscortObjectiveProgress escort = active.Escort;
            if (frame.InObjectiveArea
                && frame.IsAirborne
                && playerFresh
                && player is not null
                && protectedActor?.Telemetry is { } protectedTelemetry)
            {
                DateTimeOffset newestTelemetry =
                    player.Timestamp >= protectedTelemetry.Timestamp
                        ? player.Timestamp
                        : protectedTelemetry.Timestamp;

                if (newestTelemetry >= escort.UpdatedAt)
                {
                    escort = EscortObjectiveValidator.Advance(
                        active.Request.ObjectiveProfile,
                        escort,
                        player,
                        protectedTelemetry,
                        frame.DeltaSeconds);
                }
            }

            MilitaryThreatExposure threat = player is null
                ? ZeroThreat()
                : MilitaryThreatEvaluator.Evaluate(
                    new MilitaryThreatSample(
                        player.Timestamp,
                        player.LatitudeDegrees,
                        player.LongitudeDegrees,
                        player.AltitudeMslFeet),
                    active.Request.ThreatZones);

            MilitaryObjectiveAssessment? objectiveAssessment =
                protectedActor?.Telemetry is null
                    ? null
                    : escort.Assess(active.Request.ObjectiveProfile);

            bool stableTelemetry = frame.HasStableFlightState
                && playerFresh
                && player is not null
                && !player.Paused
                && !player.SlewActive;

            MilitaryMissionProgress mission = MilitaryMissionEngine.Advance(
                active.Request.Plan,
                active.Mission,
                new MilitaryMissionEvidence(
                    frame.Time,
                    HasStableTelemetry: stableTelemetry,
                    AtOrigin: frame.AtOrigin,
                    Airborne: frame.IsAirborne,
                    InObjectiveArea: frame.InObjectiveArea,
                    ObjectiveActionVerified: false,
                    AtRecoveryAirfield: frame.AtRecoveryAirfield,
                    ParkedAndSecured: frame.ParkedAndSecured,
                    AbortRequested: frame.AbortRequested,
                    FailureDetected: frame.FailureDetected,
                    DeltaSeconds: frame.DeltaSeconds,
                    ObjectiveAssessment: objectiveAssessment));

            active = active with
            {
                Mission = mission,
                Escort = escort,
                Threat = threat
            };
            _active = active;

            MilitaryEscortOperationSnapshot snapshot =
                Publish(
                    active,
                    protectedActor?.Telemetry is not null);

            if (mission.IsTerminal && active.ProtectedAircraftActive)
            {
                await RemoveProtectedAircraftAsync(active)
                    .ConfigureAwait(false);
                active = _active!;
                snapshot = Publish(
                    active,
                    protectedAircraftHasTelemetry: false);
            }

            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_active is null)
                return;

            if (_active.ProtectedAircraftActive)
                await RemoveProtectedAircraftAsync(_active).ConfigureAwait(false);

            _active = null;
            Volatile.Write(ref _current, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task RemoveProtectedAircraftAsync(ActiveEscort active)
    {
        bool stillPublished = _actorService.Actors.Any(
            actor => actor.Handle.ObjectId == active.Actor.ObjectId
                && string.Equals(
                    actor.Handle.ActorKey,
                    active.Actor.ActorKey,
                    StringComparison.Ordinal));

        if (stillPublished)
        {
            await _actorService
                .RemoveActorAsync(active.Actor)
                .ConfigureAwait(false);
        }

        _active = active with { ProtectedAircraftActive = false };
    }

    private MilitaryEscortOperationSnapshot Publish(
        ActiveEscort active,
        bool protectedAircraftHasTelemetry)
    {
        var snapshot = new MilitaryEscortOperationSnapshot(
            active.Mission,
            active.Escort,
            active.Threat,
            active.Actor,
            protectedAircraftHasTelemetry,
            active.ProtectedAircraftActive,
            active.Mission.UpdatedAt);

        Volatile.Write(ref _current, snapshot);
        return snapshot;
    }

    private static bool IsPlayerTelemetryFresh(
        AircraftTelemetrySnapshot? telemetry,
        DateTimeOffset frameTime,
        double maximumAgeSeconds)
    {
        if (telemetry is null)
            return false;

        double age = Math.Abs(
            (frameTime - telemetry.Timestamp).TotalSeconds);

        return age <= maximumAgeSeconds;
    }

    private static MilitaryThreatExposure ZeroThreat() =>
        new(
            0,
            SimulatedThreatLevel.None,
            Array.Empty<string>());

    private sealed record ActiveEscort(
        MilitaryEscortOperationStartRequest Request,
        SimulatorMissionActorHandle Actor,
        MilitaryMissionProgress Mission,
        EscortObjectiveProgress Escort,
        MilitaryThreatExposure Threat,
        bool ProtectedAircraftActive);
}
