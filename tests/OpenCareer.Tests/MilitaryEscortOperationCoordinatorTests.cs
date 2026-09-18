using OpenCareer.Application.Military;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Military;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Tests;

public sealed class MilitaryEscortOperationCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EscortCoordinatorRunsGeometryThreatAndMissionLifecycle()
    {
        var telemetry = new FakeTelemetrySource();
        var actors = new FakeMissionActorService();

        await using var coordinator =
            new MilitaryEscortOperationCoordinator(
                telemetry,
                actors);

        var start = CreateStartRequest();
        MilitaryEscortOperationSnapshot snapshot =
            await coordinator.StartAsync(start);

        Assert.Equal(
            MilitaryOperationPhase.Accepted,
            snapshot.Mission.Phase);
        Assert.True(snapshot.ProtectedAircraftActive);
        Assert.Equal(1, actors.SpawnCount);

        telemetry.LatestValue =
            CreateTelemetry(
                Epoch.AddSeconds(1),
                onGround: true);
        actors.SetTelemetry(
            CreateTelemetry(
                Epoch.AddSeconds(1),
                onGround: false,
                altitudeMslFeet: 10_100));

        snapshot = await coordinator.AdvanceAsync(
            Frame(
                Epoch.AddSeconds(1),
                atOrigin: true,
                airborne: false,
                inObjectiveArea: false,
                deltaSeconds: 1));
        Assert.Equal(
            MilitaryOperationPhase.Preflight,
            snapshot.Mission.Phase);

        telemetry.LatestValue =
            CreateTelemetry(
                Epoch.AddSeconds(2),
                onGround: false);
        snapshot = await coordinator.AdvanceAsync(
            Frame(
                Epoch.AddSeconds(2),
                atOrigin: false,
                airborne: true,
                inObjectiveArea: false,
                deltaSeconds: 1));
        Assert.Equal(
            MilitaryOperationPhase.EnRoute,
            snapshot.Mission.Phase);

        telemetry.LatestValue =
            CreateTelemetry(
                Epoch.AddSeconds(7),
                onGround: false);
        actors.SetTelemetry(
            CreateTelemetry(
                Epoch.AddSeconds(7),
                onGround: false,
                altitudeMslFeet: 10_100));
        snapshot = await coordinator.AdvanceAsync(
            Frame(
                Epoch.AddSeconds(7),
                atOrigin: false,
                airborne: true,
                inObjectiveArea: true,
                deltaSeconds: 5));

        Assert.Equal(
            MilitaryOperationPhase.OnStation,
            snapshot.Mission.Phase);
        Assert.Equal(5, snapshot.Escort.QualifiedSeconds);
        Assert.True(snapshot.ThreatExposure.Pressure > 0);

        telemetry.LatestValue =
            CreateTelemetry(
                Epoch.AddSeconds(12),
                onGround: false);
        actors.SetTelemetry(
            CreateTelemetry(
                Epoch.AddSeconds(12),
                onGround: false,
                altitudeMslFeet: 10_100));
        snapshot = await coordinator.AdvanceAsync(
            Frame(
                Epoch.AddSeconds(12),
                atOrigin: false,
                airborne: true,
                inObjectiveArea: true,
                deltaSeconds: 5));

        Assert.Equal(
            MilitaryOperationPhase.Objective,
            snapshot.Mission.Phase);
        Assert.True(
            snapshot.Escort
                .Assess(start.ObjectiveProfile)
                .IsSatisfied);

        telemetry.LatestValue =
            CreateTelemetry(
                Epoch.AddSeconds(13),
                onGround: false);
        snapshot = await coordinator.AdvanceAsync(
            Frame(
                Epoch.AddSeconds(13),
                atOrigin: false,
                airborne: true,
                inObjectiveArea: false,
                deltaSeconds: 1));
        Assert.Equal(
            MilitaryOperationPhase.Egress,
            snapshot.Mission.Phase);

        telemetry.LatestValue =
            CreateTelemetry(
                Epoch.AddSeconds(14),
                onGround: true);
        snapshot = await coordinator.AdvanceAsync(
            Frame(
                Epoch.AddSeconds(14),
                atOrigin: false,
                airborne: false,
                inObjectiveArea: false,
                deltaSeconds: 1,
                atRecovery: true));
        Assert.Equal(
            MilitaryOperationPhase.Recovery,
            snapshot.Mission.Phase);

        telemetry.LatestValue =
            CreateTelemetry(
                Epoch.AddSeconds(15),
                onGround: true);
        snapshot = await coordinator.AdvanceAsync(
            Frame(
                Epoch.AddSeconds(15),
                atOrigin: false,
                airborne: false,
                inObjectiveArea: false,
                deltaSeconds: 1,
                atRecovery: true,
                parked: true));

        Assert.Equal(
            MilitaryOperationPhase.Complete,
            snapshot.Mission.Phase);
        Assert.False(snapshot.ProtectedAircraftActive);
        Assert.Single(actors.RemovedActors);
    }

    [Fact]
    public async Task StalePlayerTelemetryCannotAdvanceAcceptedEscort()
    {
        var telemetry = new FakeTelemetrySource
        {
            LatestValue =
                CreateTelemetry(
                    Epoch,
                    onGround: true)
        };
        var actors = new FakeMissionActorService();

        await using var coordinator =
            new MilitaryEscortOperationCoordinator(
                telemetry,
                actors);

        await coordinator.StartAsync(
            CreateStartRequest() with
            {
                MaximumPlayerTelemetryAgeSeconds = 2
            });

        var snapshot = await coordinator.AdvanceAsync(
            Frame(
                Epoch.AddSeconds(10),
                atOrigin: true,
                airborne: false,
                inObjectiveArea: false,
                deltaSeconds: 1));

        Assert.Equal(
            MilitaryOperationPhase.Accepted,
            snapshot.Mission.Phase);
    }

    [Fact]
    public async Task StopRemovesPublishedMissionActorAndClearsCurrent()
    {
        var telemetry = new FakeTelemetrySource();
        var actors = new FakeMissionActorService();

        await using var coordinator =
            new MilitaryEscortOperationCoordinator(
                telemetry,
                actors);

        await coordinator.StartAsync(
            CreateStartRequest());

        await coordinator.StopAsync();

        Assert.Null(coordinator.Current);
        Assert.Single(actors.RemovedActors);
    }

    private static MilitaryEscortOperationStartRequest CreateStartRequest()
    {
        var plan = new MilitaryOperationPlan(
            Guid.NewGuid(),
            MilitaryOperationKind.Escort,
            "KRME",
            "KRME",
            "SIM-ESCORT-AREA",
            MilitaryOperationRequirementsCatalog.For(
                MilitaryOperationKind.Escort),
            MinimumOnStationSeconds: 10,
            RequiresObjectiveAction: true,
            CampaignId: "sim-campaign",
            SupportedSideId: "sim-side");

        var threat = new SimulatedThreatZone(
            "sim-ground-pressure",
            SimulatedThreatCategory.GroundBasedOpposition,
            LatitudeDegrees: 43.0,
            LongitudeDegrees: -75.0,
            RadiusNauticalMiles: 30,
            Severity: 0.5,
            Confidence: 0.8,
            ActiveFrom: Epoch.AddHours(-1),
            ActiveUntil: Epoch.AddHours(1));

        return new MilitaryEscortOperationStartRequest(
            plan,
            MilitaryOperationEligibility.Allowed(),
            new SimulatorMissionAircraftSpawnRequest(
                "protected-transport",
                "Test Transport",
                string.Empty,
                "OC001",
                -1,
                "OpenCareer\\escort-test",
                2.5,
                false),
            new EscortObjectiveProfile(
                MaximumHorizontalSeparationNauticalMiles: 3,
                MaximumVerticalSeparationFeet: 2_000,
                MaximumGroundSpeedDifferenceKnots: 150,
                MinimumQualifiedSeconds: 10,
                MinimumQualifiedFraction: 0.75,
                MaximumTelemetrySkewSeconds: 5),
            [threat],
            Epoch);
    }

    private static MilitaryEscortRuntimeFrame Frame(
        DateTimeOffset time,
        bool atOrigin,
        bool airborne,
        bool inObjectiveArea,
        double deltaSeconds,
        bool atRecovery = false,
        bool parked = false) =>
        new(
            time,
            HasStableFlightState: true,
            AtOrigin: atOrigin,
            IsAirborne: airborne,
            InObjectiveArea: inObjectiveArea,
            AtRecoveryAirfield: atRecovery,
            ParkedAndSecured: parked,
            DeltaSeconds: deltaSeconds);

    private static AircraftTelemetrySnapshot CreateTelemetry(
        DateTimeOffset time,
        bool onGround,
        double altitudeMslFeet = 10_000) =>
        new(
            time,
            LatitudeDegrees: 43.0,
            LongitudeDegrees: -75.0,
            AltitudeMslFeet: altitudeMslFeet,
            AltitudeAglFeet: onGround ? 0 : 5_000,
            IndicatedAirspeedKnots: onGround ? 0 : 300,
            GroundSpeedKnots: onGround ? 0 : 310,
            VerticalSpeedFeetPerMinute: 0,
            HeadingDegrees: 90,
            PitchDegrees: 0,
            BankDegrees: 0,
            NormalAccelerationG: 1,
            OnGround: onGround,
            ParkingBrakeSet: onGround,
            EnginesRunning: 2,
            FuelTotalPounds: 1_000,
            PayloadPounds: 1_000,
            FlapsPositionPercent: 0,
            GearDown: onGround,
            Paused: false,
            SlewActive: false);

    private sealed class FakeTelemetrySource
        : ISimulatorTelemetrySource
    {
        public AircraftTelemetrySnapshot? LatestValue { get; set; }

        public AircraftTelemetrySnapshot? Latest =>
            LatestValue;
    }

    private sealed class FakeMissionActorService
        : ISimulatorMissionActorService
    {
        private readonly List<SimulatorMissionActorSnapshot> _actors = [];
        private SimulatorMissionActorHandle? _active;

        public int SpawnCount { get; private set; }
        public List<SimulatorMissionActorHandle> RemovedActors { get; } = [];

        public IReadOnlyList<SimulatorMissionActorSnapshot> Actors =>
            _actors;

        public Task<SimulatorMissionActorHandle> SpawnEnrouteAircraftAsync(
            SimulatorMissionAircraftSpawnRequest request)
        {
            request.Validate();
            SpawnCount++;

            _active =
                new SimulatorMissionActorHandle(
                    request.ActorKey,
                    77);

            _actors.Add(
                new SimulatorMissionActorSnapshot(
                    _active,
                    Telemetry: null,
                    CreatedAt: Epoch,
                    UpdatedAt: Epoch));

            return Task.FromResult(_active);
        }

        public Task RemoveActorAsync(
            SimulatorMissionActorHandle actor)
        {
            RemovedActors.Add(actor);
            _actors.RemoveAll(
                row =>
                    row.Handle.ObjectId == actor.ObjectId);
            return Task.CompletedTask;
        }

        public void SetTelemetry(
            AircraftTelemetrySnapshot telemetry)
        {
            if (_active is null)
                throw new InvalidOperationException();

            int index = _actors.FindIndex(
                row =>
                    row.Handle.ObjectId
                        == _active.ObjectId);

            _actors[index] =
                _actors[index] with
                {
                    Telemetry = telemetry,
                    UpdatedAt = telemetry.Timestamp
                };
        }
    }
}
