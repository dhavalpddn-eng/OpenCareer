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

        IFlightStateEvidenceSource source =
            runtime;

        var published =
            new List<FlightStateEvidence?>();

        source.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.Null(source.Current);

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Null(coordinator.Current);
        Assert.Null(store.Checkpoint);
        Assert.Null(source.Current);
        Assert.Empty(published);
    }

    [Fact]
    public async Task AcceptedTelemetryPublishesAuthoritativeEvidence()
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

        var published =
            new List<FlightStateEvidence?>();

        source.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence evidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        Assert.Same(
            evidence,
            source.Current);
        Assert.Equal(
            telemetry.Latest.Timestamp,
            evidence.Timestamp);
        Assert.True(evidence.Connected);
        Assert.True(evidence.StableTelemetry);
        Assert.True(evidence.ValidLoadedAircraft);
        Assert.True(evidence.ContinuityPlausible);
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

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            FlightSessionStatus.Suspended,
            coordinator.Current?.Status);

        Assert.Equal(
            FlightSessionStatus.Suspended,
            store.Checkpoint?.Status);

        IFlightStateEvidenceSource source =
            runtime;

        FlightStateEvidence evidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        Assert.Same(
            evidence,
            source.Current);
        Assert.False(evidence.Connected);
        Assert.False(evidence.ContinuityPlausible);
        Assert.Equal(
            Epoch.AddMinutes(1),
            evidence.Timestamp);
    }

    [Fact]
    public async Task SuspendedDisconnectedSessionDoesNotRepublishDisconnectEvidence()
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

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence disconnectEvidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        DateTimeOffset suspendedAt =
            coordinator.Current!.UpdatedAt;

        Assert.Equal(
            FlightSessionStatus.Suspended,
            coordinator.Current.Status);

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Single(published);
        Assert.Same(
            disconnectEvidence,
            runtime.Current);
        Assert.Equal(
            suspendedAt,
            coordinator.Current.UpdatedAt);
        Assert.Equal(
            suspendedAt,
            store.Checkpoint!.UpdatedAt);
    }

    [Fact]
    public async Task ReconnectPublishesFreshConnectedEvidenceAfterDisconnect()
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

        var connection =
            new TestConnection
            {
                Current =
                    new SimulatorConnectionSnapshot(
                        SimulatorConnectionState.Reconnecting)
            };

        var telemetry =
            new TestTelemetrySource();

        var runtime =
            new FlightSessionRuntime(
                coordinator,
                persistence,
                Processor(),
                new FlightContinuityPolicy(),
                connection,
                telemetry,
                new FixedTimeProvider(
                    Epoch.AddMinutes(1)));

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence disconnectEvidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        Assert.False(disconnectEvidence.Connected);
        Assert.Equal(
            FlightSessionStatus.Suspended,
            coordinator.Current?.Status);

        connection.Current =
            new SimulatorConnectionSnapshot(
                SimulatorConnectionState.Connected);

        telemetry.Latest =
            Telemetry(
                Epoch.AddMinutes(2),
                32,
                -97,
                onGround: true);

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            2,
            published.Count);

        FlightStateEvidence reconnectEvidence =
            Assert.IsType<FlightStateEvidence>(
                published[1]);

        Assert.True(reconnectEvidence.Connected);
        Assert.True(reconnectEvidence.StableTelemetry);
        Assert.True(reconnectEvidence.ValidLoadedAircraft);
        Assert.True(reconnectEvidence.ContinuityPlausible);
        Assert.Equal(
            telemetry.Latest.Timestamp,
            reconnectEvidence.Timestamp);
        Assert.Same(
            reconnectEvidence,
            runtime.Current);
        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current?.Status);
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

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.Null(runtime.Current);

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence reconnectEvidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        Assert.True(reconnectEvidence.Connected);
        Assert.True(reconnectEvidence.StableTelemetry);
        Assert.True(reconnectEvidence.ValidLoadedAircraft);
        Assert.True(reconnectEvidence.ContinuityPlausible);
        Assert.Equal(
            telemetry.Latest.Timestamp,
            reconnectEvidence.Timestamp);
        Assert.Same(
            reconnectEvidence,
            runtime.Current);

        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current?.Status);

        Assert.Equal(
            FlightSessionStatus.Active,
            store.Checkpoint?.Status);

        Assert.Equal(
            FlightOperationState.ReadyForStart,
            coordinator.Current?.OperationState);

        Assert.NotNull(
            coordinator.Current?.ContinuityAnchor);
    }

    [Fact]
    public async Task FailedRecoveredResumeDoesNotPublishOrAdvanceEvidence()
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
                Checkpoint = suspended,
                FailWrites = true
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

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        await Assert.ThrowsAsync<IOException>(
            () => runtime.RefreshAsync());

        Assert.Empty(published);
        Assert.Null(runtime.Current);
        Assert.Equal(
            FlightSessionStatus.Suspended,
            coordinator.Current!.Status);
        Assert.Equal(
            suspended.UpdatedAt,
            coordinator.Current.UpdatedAt);
        Assert.Equal(
            FlightSessionStatus.Suspended,
            store.Checkpoint!.Status);
        Assert.Equal(
            suspended.UpdatedAt,
            store.Checkpoint.UpdatedAt);

        store.FailWrites = false;

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence reconnectEvidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        Assert.True(reconnectEvidence.Connected);
        Assert.True(reconnectEvidence.StableTelemetry);
        Assert.True(reconnectEvidence.ContinuityPlausible);
        Assert.True(reconnectEvidence.EngineStartObserved);
        Assert.Equal(
            telemetry.Latest.Timestamp,
            reconnectEvidence.Timestamp);
        Assert.Same(
            reconnectEvidence,
            runtime.Current);
        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current!.Status);
        Assert.Equal(
            FlightSessionStatus.Active,
            store.Checkpoint!.Status);
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
    public async Task NewFlightSessionClearsPriorEvidenceBeforeNewTelemetry()
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
            new FlightSessionRuntime(
                coordinator,
                persistence,
                Processor(),
                new FlightContinuityPolicy(),
                Connected(),
                telemetry,
                new FixedTimeProvider(
                    Epoch.AddHours(1)));

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence priorEvidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        Guid priorSessionId =
            coordinator.Current!.SessionId;

        await persistence.AdvanceAsync(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddMinutes(2),
                    Connected: true),
                CancelRequested: true));

        await persistence.ClearTerminalAsync();

        await persistence.StartAsync(
            Epoch.AddMinutes(3));

        Assert.NotEqual(
            priorSessionId,
            coordinator.Current!.SessionId);

        telemetry.Latest = null;

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Null(runtime.Current);
        Assert.Equal(
            2,
            published.Count);
        Assert.Same(
            priorEvidence,
            published[0]);
        Assert.Null(published[1]);

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Equal(
            2,
            published.Count);
    }

    [Fact]
    public async Task TelemetryOlderThanSessionStateDoesNotPublishOrReplaceEvidence()
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
            new FlightSessionRuntime(
                coordinator,
                persistence,
                Processor(),
                new FlightContinuityPolicy(),
                Connected(),
                telemetry,
                new FixedTimeProvider(
                    Epoch.AddHours(1)));

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence acceptedEvidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        await persistence.AdvanceAsync(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddMinutes(2),
                    Connected: true,
                    ContinuityPlausible: true)));

        DateTimeOffset sessionUpdatedAt =
            coordinator.Current!.UpdatedAt;

        telemetry.Latest =
            Telemetry(
                Epoch.AddMinutes(1).AddSeconds(30),
                32,
                -97,
                onGround: true);

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Single(published);
        Assert.Same(
            acceptedEvidence,
            runtime.Current);
        Assert.Equal(
            Epoch.AddMinutes(2),
            sessionUpdatedAt);
        Assert.Equal(
            sessionUpdatedAt,
            coordinator.Current.UpdatedAt);
        Assert.Equal(
            sessionUpdatedAt,
            store.Checkpoint!.UpdatedAt);
    }

    [Fact]
    public async Task FailedDisconnectSuspensionDoesNotPublishEvidence()
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
            Connected();

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
                connection,
                telemetry);

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence acceptedEvidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        DateTimeOffset acceptedAt =
            coordinator.Current!.UpdatedAt;

        store.FailWrites = true;

        connection.Current =
            new SimulatorConnectionSnapshot(
                SimulatorConnectionState.Reconnecting);

        await Assert.ThrowsAsync<IOException>(
            () => runtime.RefreshAsync());

        Assert.Single(published);
        Assert.Same(
            acceptedEvidence,
            runtime.Current);
        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current!.Status);
        Assert.Equal(
            acceptedAt,
            coordinator.Current.UpdatedAt);
        Assert.Equal(
            FlightSessionStatus.Active,
            store.Checkpoint!.Status);
        Assert.Equal(
            acceptedAt,
            store.Checkpoint.UpdatedAt);
    }

    [Fact]
    public async Task FailedDisconnectSuspensionCanRetryAndPublishOnce()
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
            Connected();

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
                connection,
                telemetry);

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence acceptedEvidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        store.FailWrites = true;

        connection.Current =
            new SimulatorConnectionSnapshot(
                SimulatorConnectionState.Reconnecting);

        await Assert.ThrowsAsync<IOException>(
            () => runtime.RefreshAsync());

        Assert.Single(published);
        Assert.Same(
            acceptedEvidence,
            runtime.Current);
        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current!.Status);

        store.FailWrites = false;

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            2,
            published.Count);

        FlightStateEvidence disconnectEvidence =
            Assert.IsType<FlightStateEvidence>(
                published[1]);

        Assert.False(disconnectEvidence.Connected);
        Assert.False(
            disconnectEvidence.ContinuityPlausible);
        Assert.Same(
            disconnectEvidence,
            runtime.Current);
        Assert.Equal(
            FlightSessionStatus.Suspended,
            coordinator.Current.Status);
        Assert.Equal(
            FlightSessionStatus.Suspended,
            store.Checkpoint!.Status);

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Equal(
            2,
            published.Count);
        Assert.Same(
            disconnectEvidence,
            runtime.Current);
    }

    [Fact]
    public async Task FailedContinuitySuspensionDoesNotPublishEvidence()
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
                        32.001,
                        -97.001,
                        onGround: true)
            };

        var runtime =
            CreateRuntime(
                coordinator,
                store,
                Connected(),
                telemetry);

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence acceptedEvidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        DateTimeOffset acceptedAt =
            coordinator.Current!.UpdatedAt;

        store.FailWrites = true;

        telemetry.Latest =
            Telemetry(
                Epoch.AddMinutes(2),
                34,
                -95,
                onGround: true);

        await Assert.ThrowsAsync<IOException>(
            () => runtime.RefreshAsync());

        Assert.Single(published);
        Assert.Same(
            acceptedEvidence,
            runtime.Current);
        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current!.Status);
        Assert.Equal(
            acceptedAt,
            coordinator.Current.UpdatedAt);
        Assert.Equal(
            FlightSessionStatus.Active,
            store.Checkpoint!.Status);
        Assert.Equal(
            acceptedAt,
            store.Checkpoint.UpdatedAt);
    }

    [Fact]
    public async Task FailedContinuitySuspensionCanRetryAndPublishOnce()
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
                        32.001,
                        -97.001,
                        onGround: true)
            };

        var runtime =
            CreateRuntime(
                coordinator,
                store,
                Connected(),
                telemetry);

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence acceptedEvidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        store.FailWrites = true;

        telemetry.Latest =
            Telemetry(
                Epoch.AddMinutes(2),
                34,
                -95,
                onGround: true);

        await Assert.ThrowsAsync<IOException>(
            () => runtime.RefreshAsync());

        Assert.Single(published);
        Assert.Same(
            acceptedEvidence,
            runtime.Current);
        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current!.Status);

        store.FailWrites = false;

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            2,
            published.Count);

        FlightStateEvidence continuityEvidence =
            Assert.IsType<FlightStateEvidence>(
                published[1]);

        Assert.False(continuityEvidence.Connected);
        Assert.False(
            continuityEvidence.ContinuityPlausible);
        Assert.Equal(
            telemetry.Latest.Timestamp,
            continuityEvidence.Timestamp);
        Assert.Same(
            continuityEvidence,
            runtime.Current);
        Assert.Equal(
            FlightSessionStatus.Suspended,
            coordinator.Current!.Status);
        Assert.Equal(
            FlightSessionStatus.Suspended,
            store.Checkpoint!.Status);
    }

    [Fact]
    public async Task FailedPersistenceDoesNotPublishOrReplaceAcceptedEvidence()
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

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence acceptedEvidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        DateTimeOffset acceptedSessionTimestamp =
            coordinator.Current!.UpdatedAt;

        store.FailWrites = true;

        telemetry.Latest =
            Telemetry(
                Epoch.AddMinutes(1).AddSeconds(31),
                32,
                -97,
                onGround: true);

        await Assert.ThrowsAsync<IOException>(
            () => runtime.RefreshAsync());

        Assert.Single(published);
        Assert.Same(
            acceptedEvidence,
            runtime.Current);
        Assert.Equal(
            acceptedSessionTimestamp,
            coordinator.Current!.UpdatedAt);
        Assert.Equal(
            acceptedSessionTimestamp,
            store.Checkpoint!.UpdatedAt);
    }

    [Fact]
    public async Task FailedPersistenceRetryDoesNotAdvanceEvidenceTwice()
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
                        StableTelemetrySamples: 3,
                        AirborneConfirmationSamples: 1,
                        GroundConfirmationSamples: 1)),
                new FlightContinuityPolicy(),
                Connected(),
                telemetry,
                new FixedTimeProvider(
                    Epoch.AddHours(1)));

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        FlightStateEvidence firstEvidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        Assert.False(firstEvidence.StableTelemetry);

        store.FailWrites = true;

        telemetry.Latest =
            Telemetry(
                Epoch.AddMinutes(1).AddSeconds(31),
                32,
                -97,
                onGround: true);

        await Assert.ThrowsAsync<IOException>(
            () => runtime.RefreshAsync());

        Assert.Single(published);
        Assert.Same(
            firstEvidence,
            runtime.Current);

        store.FailWrites = false;

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            2,
            published.Count);

        FlightStateEvidence retryEvidence =
            Assert.IsType<FlightStateEvidence>(
                published[1]);

        Assert.False(retryEvidence.StableTelemetry);
        Assert.Equal(
            telemetry.Latest.Timestamp,
            retryEvidence.Timestamp);
        Assert.Same(
            retryEvidence,
            runtime.Current);
    }

    [Fact]
    public async Task RuntimeResetClearsPublishedEvidenceExactlyOnce()
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

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        Assert.NotNull(runtime.Current);
        Assert.Single(published);

        coordinator.Advance(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddMinutes(2),
                    Connected: true),
                CancelRequested: true));

        Assert.True(
            coordinator.Current!.IsTerminal);

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Null(runtime.Current);
        Assert.Equal(
            2,
            published.Count);
        Assert.Null(published[1]);

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Equal(
            2,
            published.Count);
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

        var published =
            new List<FlightStateEvidence?>();

        runtime.EvidenceChanged +=
            (_, args) =>
                published.Add(args.Evidence);

        Assert.True(
            await runtime.RefreshAsync());

        DateTimeOffset updated =
            coordinator.Current!.UpdatedAt;

        FlightStateEvidence firstEvidence =
            Assert.IsType<FlightStateEvidence>(
                Assert.Single(published));

        Assert.Same(
            firstEvidence,
            runtime.Current);

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Equal(
            updated,
            coordinator.Current!.UpdatedAt);
        Assert.Single(published);
        Assert.Same(
            firstEvidence,
            runtime.Current);
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

        public bool FailWrites { get; set; }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            if (FailWrites)
            {
                throw new IOException(
                    "Synthetic checkpoint write failure.");
            }

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
