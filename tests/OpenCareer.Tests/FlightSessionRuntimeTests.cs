using OpenCareer.Application.Flights;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Tests;

public sealed class FlightSessionRuntimeTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TelemetryAloneNeverCreatesCareerFlightSession()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore();

        var runtime =
            CreateRuntime(
                coordinator,
                store,
                new TestConnection
                {
                    Current =
                        new SimulatorConnectionSnapshot(
                            SimulatorConnectionState.Connected)
                },
                new TestTelemetrySource
                {
                    Latest =
                        Telemetry(
                            Epoch,
                            32,
                            -97,
                            onGround: true)
                });

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Null(coordinator.Current);
        Assert.Null(store.Checkpoint);
    }

    [Fact]
    public async Task SuccessfulRuntimeObservationPublishesAuthoritativeEvidence()
    {
        FlightSession active =
            PreflightSession();

        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(active);

        var store =
            new MemoryStore
            {
                Checkpoint = active
            };

        DateTimeOffset timestamp =
            Epoch.AddMinutes(1);

        var runtime =
            CreateRuntime(
                coordinator,
                store,
                Connected(),
                new TestTelemetrySource
                {
                    Latest =
                        Telemetry(
                            timestamp,
                            32,
                            -97,
                            onGround: true)
                });

        IFlightStateEvidenceSource source =
            runtime;

        FlightStateEvidence? published = null;
        source.EvidenceChanged +=
            evidence => published = evidence;

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence current =
            Assert.IsType<FlightStateEvidence>(
                source.Current);

        Assert.Same(current, published);
        Assert.Equal(timestamp, current.Timestamp);
        Assert.True(current.Connected);
        Assert.True(current.StableTelemetry);
        Assert.True(current.ValidLoadedAircraft);
        Assert.True(current.ContinuityPlausible);
    }

    [Fact]
    public async Task DisconnectSuspendsAndPersistsActiveSession()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore();

        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        await persistence.StartAsync(Epoch);

        var runtime =
            new FlightSessionRuntime(
                coordinator,
                persistence,
                Processor(),
                new FlightContinuityPolicy(),
                new TestConnection
                {
                    Current =
                        new SimulatorConnectionSnapshot(
                            SimulatorConnectionState.Reconnecting)
                },
                new TestTelemetrySource(),
                new FixedTimeProvider(
                    Epoch.AddMinutes(1)));

        IFlightStateEvidenceSource source =
            runtime;

        FlightStateEvidence? published = null;
        source.EvidenceChanged +=
            evidence => published = evidence;

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            FlightSessionStatus.Suspended,
            coordinator.Current?.Status);

        Assert.Equal(
            FlightSessionStatus.Suspended,
            store.Checkpoint?.Status);

        FlightStateEvidence current =
            Assert.IsType<FlightStateEvidence>(
                source.Current);

        Assert.Same(current, published);
        Assert.Equal(
            Epoch.AddMinutes(1),
            current.Timestamp);
        Assert.False(current.Connected);
        Assert.False(current.ContinuityPlausible);
    }

    [Fact]
    public async Task ReconnectPublishesFirstAcceptedEvidenceOnce()
    {
        FlightSession active =
            PreflightSession();

        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(active);

        var store =
            new MemoryStore
            {
                Checkpoint = active
            };

        var connection =
            new TestConnection
            {
                Current =
                    new SimulatorConnectionSnapshot(
                        SimulatorConnectionState.Reconnecting)
            };

        var telemetry =
            new TestTelemetrySource
            {
                Latest =
                    Telemetry(
                        Epoch.AddHours(1).AddSeconds(1),
                        32,
                        -97,
                        onGround: true)
            };

        var runtime =
            CreateRuntime(
                coordinator,
                store,
                connection,
                telemetry);

        IFlightStateEvidenceSource source =
            runtime;

        var published =
            new List<FlightStateEvidence?>();

        source.EvidenceChanged +=
            evidence => published.Add(evidence);

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence disconnected =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        Assert.Same(
            disconnected,
            source.Current);
        Assert.False(disconnected.Connected);
        Assert.Equal(
            FlightSessionStatus.Suspended,
            coordinator.Current?.Status);

        connection.Current =
            new SimulatorConnectionSnapshot(
                SimulatorConnectionState.Connected);

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(2, published.Count);

        FlightStateEvidence reconnected =
            Assert.IsType<FlightStateEvidence>(
                published[1]);

        Assert.Same(
            reconnected,
            source.Current);
        Assert.NotSame(
            disconnected,
            reconnected);
        Assert.True(reconnected.Connected);
        Assert.True(reconnected.StableTelemetry);
        Assert.True(reconnected.ValidLoadedAircraft);
        Assert.True(reconnected.ContinuityPlausible);
        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current?.Status);

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Equal(2, published.Count);
        Assert.Same(
            reconnected,
            source.Current);
    }

    [Fact]
    public async Task TerminalRuntimeResetClearsPublishedEvidenceOnce()
    {
        FlightSession active =
            AirborneSession();

        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(active);

        var store =
            new MemoryStore
            {
                Checkpoint = active
            };

        var runtime =
            CreateRuntime(
                coordinator,
                store,
                Connected(),
                new TestTelemetrySource
                {
                    Latest =
                        Telemetry(
                            Epoch.AddSeconds(5),
                            32,
                            -97,
                            onGround: false,
                            altitudeMsl: 10_050,
                            groundSpeed: 150)
                });

        IFlightStateEvidenceSource source =
            runtime;

        var changes =
            new List<FlightStateEvidence?>();

        source.EvidenceChanged +=
            evidence => changes.Add(evidence);

        Assert.True(
            await runtime.RefreshAsync());

        Assert.IsType<FlightStateEvidence>(
            source.Current);
        Assert.Single(changes);

        FlightSession current =
            coordinator.Current!;

        FlightSession suspended =
            FlightSessionEngine.Advance(
                current,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        current.UpdatedAt.AddSeconds(1),
                        Connected: false,
                        ContinuityPlausible: false)));

        FlightSession interrupted =
            FlightSessionEngine.Advance(
                suspended,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        suspended.UpdatedAt.AddSeconds(1),
                        Connected: true,
                        StableTelemetry: true,
                        ContinuityPlausible: false)));

        Assert.True(interrupted.IsTerminal);

        coordinator.CommitPersisted(interrupted);

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Null(source.Current);
        Assert.Equal(2, changes.Count);
        Assert.Null(changes[1]);

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Equal(2, changes.Count);

        coordinator.ClearTerminalSession();

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public async Task PlausibleRecoveredGroundSessionResumesAfterStableTelemetry()
    {
        FlightSession suspended =
            Suspend(
                PreflightSession(
                    anchor:
                        new FlightContinuityAnchor(
                            Epoch.AddSeconds(1),
                            32,
                            -97,
                            650,
                            OnGround: true)));

        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(suspended);

        var store =
            new MemoryStore
            {
                Checkpoint = suspended
            };

        var telemetry =
            new TestTelemetrySource
            {
                Latest =
                    Telemetry(
                        Epoch.AddMinutes(1),
                        32.01,
                        -97.01,
                        onGround: true)
            };

        var runtime =
            CreateRuntime(
                coordinator,
                store,
                Connected(),
                telemetry);

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current?.Status);

        Assert.Equal(
            FlightOperationState.ReadyForStart,
            coordinator.Current?.OperationState);

        Assert.NotNull(
            coordinator.Current?.ContinuityAnchor);
    }

    [Fact]
    public async Task ImplausibleRecoveredAirborneSessionBecomesInterrupted()
    {
        FlightSession suspended =
            Suspend(
                AirborneSession());

        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(suspended);

        var store =
            new MemoryStore
            {
                Checkpoint = suspended
            };

        var runtime =
            CreateRuntime(
                coordinator,
                store,
                Connected(),
                new TestTelemetrySource
                {
                    Latest =
                        Telemetry(
                            Epoch.AddMinutes(5),
                            40.7,
                            -74.0,
                            onGround: false,
                            altitudeMsl: 12_000,
                            groundSpeed: 300)
                });

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            FlightSessionStatus.Interrupted,
            coordinator.Current?.Status);

        Assert.Equal(
            FlightTrackingState.Interrupted,
            coordinator.Current?.Tracking.State);
    }

    [Fact]
    public async Task ActiveSessionSuspendsBeforeAcceptingImplausiblePositionJump()
    {
        FlightSession active =
            PreflightSession(
                anchor:
                    new FlightContinuityAnchor(
                        Epoch.AddSeconds(1),
                        32,
                        -97,
                        650,
                        OnGround: true));

        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(active);

        var store =
            new MemoryStore
            {
                Checkpoint = active
            };

        var telemetry =
            new TestTelemetrySource
            {
                Latest =
                    Telemetry(
                        Epoch.AddMinutes(1),
                        34,
                        -95,
                        onGround: true)
            };

        var runtime =
            CreateRuntime(
                coordinator,
                store,
                Connected(),
                telemetry);

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            FlightSessionStatus.Suspended,
            coordinator.Current?.Status);

        Assert.Equal(
            active.ContinuityAnchor,
            coordinator.Current?.ContinuityAnchor);

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            FlightSessionStatus.Interrupted,
            coordinator.Current?.Status);
    }

    [Fact]
    public async Task TakeoffTransitionRemainsActiveAcrossGroundToAirContinuity()
    {
        FlightSession active =
            PreflightSession(
                anchor:
                    new FlightContinuityAnchor(
                        Epoch.AddSeconds(1),
                        32,
                        -97,
                        650,
                        OnGround: true));

        active =
            FlightSessionEngine.Advance(
                active,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(2),
                        Connected: true,
                        ContinuityPlausible: true,
                        SelfPoweredMovementForFlight: true)));

        active =
            FlightSessionEngine.Advance(
                active,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(3),
                        Connected: true,
                        ContinuityPlausible: true,
                        TakeoffCandidate: true)));

        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(active);

        var store =
            new MemoryStore
            {
                Checkpoint = active
            };

        var telemetry =
            new TestTelemetrySource
            {
                Latest =
                    Telemetry(
                        Epoch.AddSeconds(4),
                        32.0005,
                        -97.0005,
                        onGround: false,
                        altitudeMsl: 675,
                        groundSpeed: 140)
            };

        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        var runtime =
            new FlightSessionRuntime(
                coordinator,
                persistence,
                new FlightTelemetryEvidenceProcessor(
                    new FlightEvidenceProcessorOptions(
                        StableTelemetrySamples: 1,
                        AirborneConfirmationSamples: 2,
                        GroundConfirmationSamples: 1)),
                new FlightContinuityPolicy(),
                Connected(),
                telemetry,
                new FixedTimeProvider(
                    Epoch.AddHours(1)));

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current?.Status);

        Assert.Equal(
            FlightTrackingState.TakeoffRoll,
            coordinator.Current?.Tracking.State);

        Assert.False(
            coordinator.Current?.ContinuityAnchor?.OnGround);

        telemetry.Latest =
            Telemetry(
                Epoch.AddSeconds(5),
                32.001,
                -97.001,
                onGround: false,
                altitudeMsl: 725,
                groundSpeed: 155);

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current?.Status);

        Assert.Equal(
            FlightTrackingState.Airborne,
            coordinator.Current?.Tracking.State);

        Assert.Equal(
            1,
            coordinator.Current?.Tracking.TakeoffCount);
    }

    [Fact]
    public async Task DuplicateTelemetryTimestampDoesNotAdvanceTwice()
    {
        FlightSession active =
            PreflightSession();

        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(active);

        var store =
            new MemoryStore
            {
                Checkpoint = active
            };

        var telemetry =
            new TestTelemetrySource
            {
                Latest =
                    Telemetry(
                        Epoch.AddMinutes(1),
                        32,
                        -97,
                        onGround: true)
            };

        var runtime =
            CreateRuntime(
                coordinator,
                store,
                Connected(),
                telemetry);

        IFlightStateEvidenceSource source =
            runtime;

        int publicationCount = 0;
        source.EvidenceChanged +=
            _ => publicationCount++;

        Assert.True(
            await runtime.RefreshAsync());

        DateTimeOffset updated =
            coordinator.Current!.UpdatedAt;

        FlightStateEvidence first =
            Assert.IsType<FlightStateEvidence>(
                source.Current);

        Assert.Equal(1, publicationCount);

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Equal(
            updated,
            coordinator.Current!.UpdatedAt);
        Assert.Equal(1, publicationCount);
        Assert.Same(first, source.Current);
    }

    private static FlightSessionRuntime CreateRuntime(
        FlightSessionCoordinator coordinator,
        MemoryStore store,
        TestConnection connection,
        TestTelemetrySource telemetry)
    {
        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        return new FlightSessionRuntime(
            coordinator,
            persistence,
            Processor(),
            new FlightContinuityPolicy(),
            connection,
            telemetry,
            new FixedTimeProvider(
                Epoch.AddHours(1)));
    }

    private static FlightTelemetryEvidenceProcessor Processor() =>
        new(
            new FlightEvidenceProcessorOptions(
                StableTelemetrySamples: 1,
                AirborneConfirmationSamples: 1,
                GroundConfirmationSamples: 1));

    private static TestConnection Connected() =>
        new()
        {
            Current =
                new SimulatorConnectionSnapshot(
                    SimulatorConnectionState.Connected)
        };

    private static FlightSession PreflightSession(
        FlightContinuityAnchor? anchor = null)
    {
        FlightSession session =
            FlightSession.Start(Epoch);

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(1),
                        Connected: true,
                        StableTelemetry: true,
                        ValidLoadedAircraft: true,
                        ContinuityPlausible: true),
                    ContinuityAnchor:
                        anchor));

        return session;
    }

    private static FlightSession AirborneSession()
    {
        FlightSession session =
            PreflightSession();

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(2),
                        Connected: true,
                        ContinuityPlausible: true,
                        SelfPoweredMovementForFlight: true)));

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(3),
                        Connected: true,
                        ContinuityPlausible: true,
                        TakeoffCandidate: true)));

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(4),
                        Connected: true,
                        ContinuityPlausible: true,
                        AirborneConfirmed: true),
                    ContinuityAnchor:
                        new FlightContinuityAnchor(
                            Epoch.AddSeconds(4),
                            32,
                            -97,
                            10_000,
                            OnGround: false)));

        return session;
    }

    private static FlightSession Suspend(
        FlightSession session) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    session.UpdatedAt.AddSeconds(1),
                    Connected: false,
                    ContinuityPlausible: false)));

    private static AircraftTelemetrySnapshot Telemetry(
        DateTimeOffset timestamp,
        double latitude,
        double longitude,
        bool onGround,
        double altitudeMsl = 650,
        double groundSpeed = 0) =>
        new(
            timestamp,
            latitude,
            longitude,
            altitudeMsl,
            onGround ? 0 : 2_000,
            onGround ? 0 : 150,
            groundSpeed,
            onGround ? 0 : -500,
            90,
            0,
            0,
            1,
            onGround,
            onGround,
            1,
            500,
            200,
            0,
            true,
            false,
            false);

    private sealed class TestConnection :
        ISimulatorConnection
    {
        public SimulatorConnectionSnapshot Current { get; set; } =
            new(
                SimulatorConnectionState.Disconnected);

        public void Start()
        {
        }

        public Task StopAsync() =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }

    private sealed class TestTelemetrySource :
        ISimulatorTelemetrySource
    {
        public AircraftTelemetrySnapshot? Latest { get; set; }
    }

    private sealed class MemoryStore :
        IFlightSessionCheckpointStore
    {
        public FlightSession? Checkpoint { get; set; }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            Checkpoint = session;
            return Task.CompletedTask;
        }

        public Task<FlightSession?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Checkpoint);

        public Task ClearAsync(
            CancellationToken cancellationToken = default)
        {
            Checkpoint = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            utcNow;
    }
}
