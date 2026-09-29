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
    public async Task MatchingLoadedAircraftAllowsContractSessionProgress()
    {
        FlightSession active = ContractSession(
            FlightSession.Start(Epoch),
            "aircraft-a");
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(active);
        var store = new MemoryStore { Checkpoint = active };
        var telemetry = new TestTelemetrySource
        {
            Latest = Telemetry(Epoch.AddSeconds(1), 32, -97, onGround: true)
        };

        var runtime = new FlightSessionRuntime(
            coordinator,
            new FlightSessionPersistenceService(coordinator, store),
            Processor(),
            new FlightContinuityPolicy(),
            Connected(),
            telemetry,
            new TestLoadedAircraftIdentitySource("aircraft-a"),
            new FixedTimeProvider(Epoch.AddSeconds(1)));

        Assert.True(await runtime.RefreshAsync());
        Assert.Equal(FlightSessionStatus.Active, coordinator.Current?.Status);
        Assert.Equal(FlightTrackingState.Preflight, coordinator.Current?.Tracking.State);
    }

    [Fact]
    public async Task UnknownLoadedAircraftSuspendsWithoutAdvancingContractSession()
    {
        FlightSession active = ContractSession(ApproachSession(), "aircraft-a");
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(active);
        var store = new MemoryStore { Checkpoint = active };
        var telemetry = new BufferedTestTelemetrySource();
        telemetry.Add(Telemetry(Epoch.AddSeconds(6), 32, -97, onGround: true, groundSpeed: 55));
        telemetry.Add(Telemetry(Epoch.AddSeconds(7), 32, -97, onGround: false, altitudeAgl: 8));
        telemetry.Add(Telemetry(Epoch.AddSeconds(8), 32, -97, onGround: true, groundSpeed: 45));
        var identity = new TestLoadedAircraftIdentitySource();

        var runtime = new FlightSessionRuntime(
            coordinator,
            new FlightSessionPersistenceService(coordinator, store),
            Processor(),
            new FlightContinuityPolicy(),
            Connected(),
            telemetry,
            identity,
            new FixedTimeProvider(Epoch.AddSeconds(8)));

        Assert.True(await runtime.RefreshAsync());
        FlightSession suspended = Assert.IsType<FlightSession>(coordinator.Current);
        Assert.Equal(FlightSessionStatus.Suspended, suspended.Status);
        Assert.Equal(active.OperationState, suspended.OperationState);
        Assert.Equal(active.TimeLedger, suspended.TimeLedger);
        Assert.Equal(active.EffectiveStatistics, suspended.EffectiveStatistics);
        Assert.Equal(active.Tracking.TakeoffCount, suspended.Tracking.TakeoffCount);
        Assert.Equal(active.Tracking.LandingEpisodeCount, suspended.Tracking.LandingEpisodeCount);
        Assert.Equal(active.Tracking.BounceCount, suspended.Tracking.BounceCount);
        Assert.Empty(telemetry.ReadAfter(null));

        identity.CanonicalAircraftId = "aircraft-a";
        Assert.False(await runtime.RefreshAsync());
        telemetry.Add(Telemetry(Epoch.AddSeconds(9), 32, -97, onGround: false, altitudeMsl: 700));
        Assert.True(await runtime.RefreshAsync());
        Assert.Equal(FlightSessionStatus.Active, coordinator.Current?.Status);
        Assert.Equal(0, coordinator.Current?.Tracking.LandingEpisodeCount);
        Assert.Equal(0, coordinator.Current?.Tracking.BounceCount);
    }

    [Fact]
    public async Task PositiveAircraftMismatchCannotTransferBufferedFlightEvidence()
    {
        FlightSession active = ContractSession(ApproachSession(), "aircraft-a");
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(active);
        var store = new MemoryStore { Checkpoint = active };
        var telemetry = new BufferedTestTelemetrySource();
        telemetry.Add(Telemetry(Epoch.AddSeconds(6), 32, -97, onGround: true, groundSpeed: 55));
        telemetry.Add(Telemetry(Epoch.AddSeconds(7), 32, -97, onGround: false, altitudeAgl: 8));
        telemetry.Add(Telemetry(Epoch.AddSeconds(8), 32, -97, onGround: true, groundSpeed: 45));
        var identity = new TestLoadedAircraftIdentitySource("aircraft-b");

        var runtime = new FlightSessionRuntime(
            coordinator,
            new FlightSessionPersistenceService(coordinator, store),
            Processor(),
            new FlightContinuityPolicy(),
            Connected(),
            telemetry,
            identity,
            new FixedTimeProvider(Epoch.AddSeconds(8)));

        Assert.True(await runtime.RefreshAsync());
        FlightSession suspended = Assert.IsType<FlightSession>(coordinator.Current);
        Assert.Equal(FlightSessionStatus.Suspended, suspended.Status);
        Assert.Equal(0, suspended.Tracking.LandingEpisodeCount);
        Assert.Equal(0, suspended.Tracking.BounceCount);

        identity.CanonicalAircraftId = "aircraft-a";
        Assert.False(await runtime.RefreshAsync());
        telemetry.Add(Telemetry(Epoch.AddSeconds(9), 32, -97, onGround: false, altitudeMsl: 700));
        Assert.True(await runtime.RefreshAsync());
        Assert.Equal(FlightSessionStatus.Active, coordinator.Current?.Status);
        Assert.Equal(0, coordinator.Current?.Tracking.LandingEpisodeCount);
        Assert.Equal(0, coordinator.Current?.Tracking.BounceCount);
    }

    [Fact]
    public async Task LegacyContractSessionWithoutExpectedAircraftFailsClosed()
    {
        FlightSession legacy = FlightSession.Start(
            Epoch,
            contractId: Guid.Parse("97000000-0000-0000-0000-000000000001"));
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(legacy);
        var store = new MemoryStore { Checkpoint = legacy };

        var runtime = new FlightSessionRuntime(
            coordinator,
            new FlightSessionPersistenceService(coordinator, store),
            Processor(),
            new FlightContinuityPolicy(),
            Connected(),
            new TestTelemetrySource
            {
                Latest = Telemetry(Epoch.AddSeconds(1), 32, -97, onGround: true)
            },
            new TestLoadedAircraftIdentitySource("aircraft-a"),
            new FixedTimeProvider(Epoch.AddSeconds(1)));

        Assert.True(await runtime.RefreshAsync());
        Assert.Equal(FlightSessionStatus.Suspended, coordinator.Current?.Status);
        Assert.Null(coordinator.Current?.Plan?.ExpectedCanonicalAircraftId);
        Assert.Equal(FlightTrackingState.Observing, coordinator.Current?.Tracking.SuspendedFrom);
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

        Assert.False(
            await runtime.RefreshAsync());

        Assert.Equal(
            FlightSessionStatus.Suspended,
            coordinator.Current?.Status);

        telemetry.Latest =
            Telemetry(
                Epoch.AddMinutes(2),
                34,
                -95,
                onGround: true);

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
    public async Task ProvisionalGroundContactAnchorAllowsRepeatedBriefAirborneTransitions()
    {
        FlightSession active =
            ApproachSession();

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
                        Epoch.AddSeconds(6),
                        32.0002,
                        -97.0002,
                        onGround: true,
                        altitudeMsl: 650,
                        groundSpeed: 55)
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
                        AirborneConfirmationSamples: 1,
                        GroundConfirmationSamples: 2)),
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
            FlightTrackingState.Approach,
            coordinator.Current?.Tracking.State);
        Assert.True(
            coordinator.Current?.ContinuityAnchor?.OnGround);

        telemetry.Latest =
            Telemetry(
                Epoch.AddSeconds(7),
                32.0004,
                -97.0004,
                onGround: false,
                altitudeMsl: 675,
                groundSpeed: 65,
                altitudeAgl: 25);

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current?.Status);
        Assert.Equal(
            FlightTrackingState.Approach,
            coordinator.Current?.Tracking.State);
        Assert.False(
            coordinator.Current?.ContinuityAnchor?.OnGround);

        telemetry.Latest =
            Telemetry(
                Epoch.AddSeconds(8),
                32.0006,
                -97.0006,
                onGround: true,
                altitudeMsl: 650,
                groundSpeed: 52);

        Assert.True(
            await runtime.RefreshAsync());

        Assert.True(
            coordinator.Current?.ContinuityAnchor?.OnGround);

        telemetry.Latest =
            Telemetry(
                Epoch.AddSeconds(9),
                32.0008,
                -97.0008,
                onGround: false,
                altitudeMsl: 670,
                groundSpeed: 62,
                altitudeAgl: 20);

        Assert.True(
            await runtime.RefreshAsync());

        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current?.Status);
        Assert.Equal(
            FlightTrackingState.Approach,
            coordinator.Current?.Tracking.State);
        Assert.False(
            coordinator.Current?.ContinuityAnchor?.OnGround);
    }

    [Fact]
    public async Task OrderedCriticalTelemetryPreservesBounceBetweenRuntimePolls()
    {
        FlightSession active = ApproachSession();
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(active);

        var store = new MemoryStore { Checkpoint = active };
        var telemetry = new BufferedTestTelemetrySource();
        var runtime =
            new FlightSessionRuntime(
                coordinator,
                new FlightSessionPersistenceService(coordinator, store),
                new FlightTelemetryEvidenceProcessor(
                    new FlightEvidenceProcessorOptions(
                        StableTelemetrySamples: 1,
                        AirborneConfirmationSamples: 1,
                        GroundConfirmationSamples: 1)),
                new FlightContinuityPolicy(),
                Connected(),
                telemetry,
                new FixedTimeProvider(Epoch.AddHours(1)));

        telemetry.Add(Telemetry(Epoch, 30, -95, onGround: true));
        Assert.False(await runtime.RefreshAsync());
        Assert.Empty(telemetry.ReadAfter(null));

        var publishedTimestamps = new List<DateTimeOffset>();
        runtime.EvidenceChanged += evidence =>
        {
            if (evidence is not null)
                publishedTimestamps.Add(evidence.Timestamp);
        };

        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(5.5),
                32,
                -97,
                onGround: false,
                altitudeMsl: 700,
                groundSpeed: 65,
                altitudeAgl: 50));
        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(6),
                32,
                -97,
                onGround: true,
                altitudeMsl: 650,
                groundSpeed: 55));
        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(6.25),
                32.0001,
                -97.0001,
                onGround: false,
                altitudeMsl: 658,
                groundSpeed: 58,
                altitudeAgl: 8));
        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(6.5),
                32.0002,
                -97.0002,
                onGround: true,
                altitudeMsl: 650,
                groundSpeed: 52));

        Assert.True(await runtime.RefreshAsync());

        FlightSession retained = Assert.IsType<FlightSession>(coordinator.Current);
        Assert.Equal(Epoch.AddSeconds(6.5), retained.UpdatedAt);
        Assert.Equal(1, retained.Tracking.LandingEpisodeCount);
        Assert.Equal(1, retained.Tracking.BounceCount);
        Assert.Equal(retained, store.Checkpoint);
        Assert.Equal(
            [
                Epoch.AddSeconds(5.5),
                Epoch.AddSeconds(6),
                Epoch.AddSeconds(6.25),
                Epoch.AddSeconds(6.5)
            ],
            publishedTimestamps);
        int savesAfterDrain = store.SaveCount;
        Assert.True(savesAfterDrain > 0);

        Assert.False(await runtime.RefreshAsync());
        Assert.Equal(savesAfterDrain, store.SaveCount);
        Assert.Equal(4, publishedTimestamps.Count);
        Assert.Equal(retained, coordinator.Current);
        Assert.Equal(retained, store.Checkpoint);
    }

    [Fact]
    public async Task FailedBouncePersistenceRestoresProcessorForExactRetry()
    {
        FlightSession active = ApproachSession();
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(active);

        var store = new MemoryStore { Checkpoint = active };
        var telemetry = new BufferedTestTelemetrySource();
        var runtime =
            new FlightSessionRuntime(
                coordinator,
                new FlightSessionPersistenceService(coordinator, store),
                new FlightTelemetryEvidenceProcessor(
                    new FlightEvidenceProcessorOptions(
                        StableTelemetrySamples: 1,
                        AirborneConfirmationSamples: 1,
                        GroundConfirmationSamples: 1)),
                new FlightContinuityPolicy(),
                Connected(),
                telemetry,
                new FixedTimeProvider(Epoch.AddHours(1)));

        Assert.False(await runtime.RefreshAsync());

        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(5.5),
                32,
                -97,
                onGround: false,
                altitudeMsl: 700,
                groundSpeed: 65,
                altitudeAgl: 50));
        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(6),
                32,
                -97,
                onGround: true,
                altitudeMsl: 650,
                groundSpeed: 55));
        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(6.25),
                32.0001,
                -97.0001,
                onGround: false,
                altitudeMsl: 658,
                groundSpeed: 58,
                altitudeAgl: 8));

        Assert.True(await runtime.RefreshAsync());

        FlightSession beforeFailure = Assert.IsType<FlightSession>(coordinator.Current);
        FlightStateEvidence beforeFailureEvidence =
            Assert.IsType<FlightStateEvidence>(runtime.Current);
        FlightSession? checkpointBeforeFailure = store.Checkpoint;
        int saveCountBeforeFailure = store.SaveCount;
        int sessionChanged = 0;
        var publishedEvidence = new List<FlightStateEvidence>();
        coordinator.SessionChanged += (_, _) => sessionChanged++;
        runtime.EvidenceChanged += evidence =>
        {
            if (evidence is not null)
                publishedEvidence.Add(evidence);
        };

        Assert.Equal(Epoch.AddSeconds(6.25), beforeFailure.UpdatedAt);
        Assert.Equal(1, beforeFailure.Tracking.LandingEpisodeCount);
        Assert.Equal(0, beforeFailure.Tracking.BounceCount);
        Assert.Equal(TimeSpan.FromMilliseconds(750), beforeFailure.TimeLedger.ObservedWallTime);
        Assert.Equal(TimeSpan.FromMilliseconds(750), beforeFailure.TimeLedger.MovementFlightTime);
        Assert.Equal(TimeSpan.FromMilliseconds(750), beforeFailure.TimeLedger.AirborneTime);
        Assert.Equal(TimeSpan.FromMilliseconds(750), beforeFailure.TimeLedger.CareerCreditTime);

        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(6.5),
                32.0002,
                -97.0002,
                onGround: true,
                altitudeMsl: 650,
                groundSpeed: 52));
        store.FailNextSave = true;

        await Assert.ThrowsAsync<IOException>(() => runtime.RefreshAsync());

        Assert.Same(beforeFailure, coordinator.Current);
        Assert.Same(beforeFailureEvidence, runtime.Current);
        Assert.Same(checkpointBeforeFailure, store.Checkpoint);
        Assert.Equal(saveCountBeforeFailure, store.SaveCount);
        Assert.Equal(Epoch.AddSeconds(6.25), telemetry.LastReadAfterTimestamp);
        Assert.Equal(0, sessionChanged);
        Assert.Empty(publishedEvidence);

        FlightSession failedAttempt =
            Assert.IsType<FlightSession>(store.FailedAttempt);
        Assert.Equal(Epoch.AddSeconds(6.5), failedAttempt.UpdatedAt);
        Assert.Equal(1, failedAttempt.Tracking.LandingEpisodeCount);
        Assert.Equal(1, failedAttempt.Tracking.BounceCount);
        Assert.Equal(TimeSpan.FromSeconds(1), failedAttempt.TimeLedger.ObservedWallTime);
        Assert.Equal(TimeSpan.FromSeconds(1), failedAttempt.TimeLedger.MovementFlightTime);
        Assert.Equal(TimeSpan.FromSeconds(1), failedAttempt.TimeLedger.AirborneTime);
        Assert.Equal(TimeSpan.FromSeconds(1), failedAttempt.TimeLedger.CareerCreditTime);

        Assert.True(await runtime.RefreshAsync());

        FlightSession afterRetry = Assert.IsType<FlightSession>(coordinator.Current);
        Assert.Equal(1, afterRetry.Tracking.LandingEpisodeCount);
        Assert.Equal(1, afterRetry.Tracking.BounceCount);
        Assert.Equal(failedAttempt.Tracking, afterRetry.Tracking);
        Assert.Equal(failedAttempt.TimeLedger, afterRetry.TimeLedger);
        Assert.Equal(afterRetry, store.Checkpoint);
        Assert.Equal(saveCountBeforeFailure + 1, store.SaveCount);
        Assert.Equal(1, sessionChanged);

        FlightStateEvidence retriedEvidence = Assert.Single(publishedEvidence);
        Assert.Equal(Epoch.AddSeconds(6.5), retriedEvidence.Timestamp);
        Assert.True(retriedEvidence.TouchdownConfirmed);
        Assert.True(retriedEvidence.BounceRecontact);

        Assert.False(await runtime.RefreshAsync());
        Assert.Equal(Epoch.AddSeconds(6.5), telemetry.LastReadAfterTimestamp);
        Assert.Equal(afterRetry, coordinator.Current);
        Assert.Equal(failedAttempt.TimeLedger, afterRetry.TimeLedger);
        Assert.Equal(saveCountBeforeFailure + 1, store.SaveCount);
        Assert.Equal(1, sessionChanged);
        Assert.Single(publishedEvidence);
    }

    [Fact]
    public async Task FailedGoAroundPersistenceRestoresProcessorForExactRetry()
    {
        FlightSession active = ApproachSession();
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(active);

        var store = new MemoryStore { Checkpoint = active };
        var telemetry = new BufferedTestTelemetrySource();
        var runtime =
            new FlightSessionRuntime(
                coordinator,
                new FlightSessionPersistenceService(coordinator, store),
                new FlightTelemetryEvidenceProcessor(
                    new FlightEvidenceProcessorOptions(
                        StableTelemetrySamples: 1,
                        AirborneConfirmationSamples: 1,
                        GroundConfirmationSamples: 1,
                        GoAroundMinimumClimbFeetPerMinute: 300,
                        GoAroundMinimumAglGainFeet: 100,
                        GoAroundMinimumClimbSeconds: 2,
                        GoAroundMaximumTelemetryGapSeconds: 2)),
                new FlightContinuityPolicy(),
                Connected(),
                telemetry,
                new FixedTimeProvider(Epoch.AddHours(1)));

        Assert.False(await runtime.RefreshAsync());

        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(5.5),
                32,
                -97,
                onGround: false,
                altitudeMsl: 750,
                groundSpeed: 70,
                altitudeAgl: 100) with
            {
                VerticalSpeedFeetPerMinute = -500
            });
        Assert.True(await runtime.RefreshAsync());

        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(6),
                32.0001,
                -97.0001,
                onGround: false,
                altitudeMsl: 800,
                groundSpeed: 72,
                altitudeAgl: 150) with
            {
                VerticalSpeedFeetPerMinute = 400
            });
        Assert.True(await runtime.RefreshAsync());

        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(7),
                32.0002,
                -97.0002,
                onGround: false,
                altitudeMsl: 830,
                groundSpeed: 74,
                altitudeAgl: 180) with
            {
                VerticalSpeedFeetPerMinute = 450
            });
        Assert.True(await runtime.RefreshAsync());

        FlightSession beforeFailure = Assert.IsType<FlightSession>(coordinator.Current);
        FlightStateEvidence beforeFailureEvidence =
            Assert.IsType<FlightStateEvidence>(runtime.Current);
        FlightSession? checkpointBeforeFailure = store.Checkpoint;
        int saveCountBeforeFailure = store.SaveCount;
        int sessionChanged = 0;
        var publishedEvidence = new List<FlightStateEvidence>();
        coordinator.SessionChanged += (_, _) => sessionChanged++;
        runtime.EvidenceChanged += evidence =>
        {
            if (evidence is not null)
                publishedEvidence.Add(evidence);
        };

        Assert.Equal(FlightTrackingState.Approach, beforeFailure.Tracking.State);
        Assert.Equal(0, beforeFailure.Tracking.LandingEpisodeCount);

        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(8),
                32.0003,
                -97.0003,
                onGround: false,
                altitudeMsl: 880,
                groundSpeed: 76,
                altitudeAgl: 230) with
            {
                VerticalSpeedFeetPerMinute = 500
            });
        store.FailNextSave = true;

        await Assert.ThrowsAsync<IOException>(() => runtime.RefreshAsync());

        Assert.Same(beforeFailure, coordinator.Current);
        Assert.Same(beforeFailureEvidence, runtime.Current);
        Assert.Same(checkpointBeforeFailure, store.Checkpoint);
        Assert.Equal(saveCountBeforeFailure, store.SaveCount);
        Assert.Equal(Epoch.AddSeconds(7), telemetry.LastReadAfterTimestamp);
        Assert.Equal(0, sessionChanged);
        Assert.Empty(publishedEvidence);

        FlightSession failedAttempt =
            Assert.IsType<FlightSession>(store.FailedAttempt);
        Assert.Equal(Epoch.AddSeconds(8), failedAttempt.UpdatedAt);
        Assert.Equal(FlightTrackingState.Airborne, failedAttempt.Tracking.State);
        Assert.Equal(0, failedAttempt.Tracking.LandingEpisodeCount);
        Assert.Equal(0, failedAttempt.Tracking.BounceCount);

        Assert.True(await runtime.RefreshAsync());

        FlightSession afterRetry = Assert.IsType<FlightSession>(coordinator.Current);
        Assert.Equal(FlightTrackingState.Airborne, afterRetry.Tracking.State);
        Assert.Equal(0, afterRetry.Tracking.LandingEpisodeCount);
        Assert.Equal(0, afterRetry.Tracking.BounceCount);
        Assert.Equal(failedAttempt.Tracking, afterRetry.Tracking);
        Assert.Equal(failedAttempt.TimeLedger, afterRetry.TimeLedger);
        Assert.Equal(afterRetry, store.Checkpoint);
        Assert.Equal(saveCountBeforeFailure + 1, store.SaveCount);
        Assert.Equal(1, sessionChanged);

        FlightStateEvidence retriedEvidence = Assert.Single(publishedEvidence);
        Assert.Equal(Epoch.AddSeconds(8), retriedEvidence.Timestamp);
        Assert.True(retriedEvidence.GoAroundConfirmed);
        Assert.False(retriedEvidence.TouchdownConfirmed);
        Assert.False(retriedEvidence.BounceRecontact);

        Assert.False(await runtime.RefreshAsync());
        Assert.Equal(Epoch.AddSeconds(8), telemetry.LastReadAfterTimestamp);
        Assert.Equal(afterRetry, coordinator.Current);
        Assert.Equal(failedAttempt.TimeLedger, afterRetry.TimeLedger);
        Assert.Equal(saveCountBeforeFailure + 1, store.SaveCount);
        Assert.Equal(1, sessionChanged);
        Assert.Single(publishedEvidence);
    }

    [Fact]
    public async Task FailedTouchAndGoPersistenceRestoresProcessorForExactRetry()
    {
        FlightSession active = ApproachSession();
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(active);

        var store = new MemoryStore { Checkpoint = active };
        var telemetry = new BufferedTestTelemetrySource();
        var runtime =
            new FlightSessionRuntime(
                coordinator,
                new FlightSessionPersistenceService(coordinator, store),
                new FlightTelemetryEvidenceProcessor(
                    new FlightEvidenceProcessorOptions(
                        StableTelemetrySamples: 1,
                        AirborneConfirmationSamples: 2,
                        GroundConfirmationSamples: 1)),
                new FlightContinuityPolicy(),
                Connected(),
                telemetry,
                new FixedTimeProvider(Epoch.AddHours(1)));

        Assert.False(await runtime.RefreshAsync());

        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(5.5),
                32,
                -97,
                onGround: true,
                altitudeMsl: 650,
                groundSpeed: 50));
        Assert.True(await runtime.RefreshAsync());

        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(6),
                32.0001,
                -97.0001,
                onGround: false,
                altitudeMsl: 670,
                groundSpeed: 65,
                altitudeAgl: 20));
        Assert.True(await runtime.RefreshAsync());

        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(7),
                32.0002,
                -97.0002,
                onGround: false,
                altitudeMsl: 685,
                groundSpeed: 70,
                altitudeAgl: 35));
        Assert.True(await runtime.RefreshAsync());

        FlightSession beforeFailure =
            Assert.IsType<FlightSession>(coordinator.Current);
        FlightStateEvidence beforeFailureEvidence =
            Assert.IsType<FlightStateEvidence>(runtime.Current);
        FlightSession? checkpointBeforeFailure = store.Checkpoint;
        int saveCountBeforeFailure = store.SaveCount;
        int sessionChanged = 0;
        var publishedEvidence = new List<FlightStateEvidence>();
        coordinator.SessionChanged += (_, _) => sessionChanged++;
        runtime.EvidenceChanged += evidence =>
        {
            if (evidence is not null)
                publishedEvidence.Add(evidence);
        };

        Assert.Equal(FlightTrackingState.LandingEpisode, beforeFailure.Tracking.State);
        Assert.Equal(1, beforeFailure.Tracking.LandingEpisodeCount);
        Assert.Equal(0, beforeFailure.Tracking.TouchAndGoCount);

        telemetry.Add(
            Telemetry(
                Epoch.AddSeconds(8),
                32.0003,
                -97.0003,
                onGround: false,
                altitudeMsl: 710,
                groundSpeed: 75,
                altitudeAgl: 60));
        store.FailNextSave = true;

        await Assert.ThrowsAsync<IOException>(() => runtime.RefreshAsync());

        Assert.Same(beforeFailure, coordinator.Current);
        Assert.Same(beforeFailureEvidence, runtime.Current);
        Assert.Same(checkpointBeforeFailure, store.Checkpoint);
        Assert.Equal(saveCountBeforeFailure, store.SaveCount);
        Assert.Equal(Epoch.AddSeconds(7), telemetry.LastReadAfterTimestamp);
        Assert.Equal(0, sessionChanged);
        Assert.Empty(publishedEvidence);

        FlightSession failedAttempt =
            Assert.IsType<FlightSession>(store.FailedAttempt);
        Assert.Equal(FlightTrackingState.Airborne, failedAttempt.Tracking.State);
        Assert.Equal(1, failedAttempt.Tracking.LandingEpisodeCount);
        Assert.Equal(1, failedAttempt.Tracking.TouchAndGoCount);
        Assert.Equal(
            FlightSessionLandingKind.TouchAndGo,
            Assert.Single(failedAttempt.EffectiveLandingEpisodes).Kind);

        Assert.True(await runtime.RefreshAsync());

        FlightSession afterRetry =
            Assert.IsType<FlightSession>(coordinator.Current);
        Assert.Equal(failedAttempt.Tracking, afterRetry.Tracking);
        Assert.Equal(failedAttempt.TimeLedger, afterRetry.TimeLedger);
        Assert.Equal(afterRetry, store.Checkpoint);
        Assert.Equal(saveCountBeforeFailure + 1, store.SaveCount);
        Assert.Equal(1, sessionChanged);

        FlightStateEvidence retriedEvidence = Assert.Single(publishedEvidence);
        Assert.Equal(Epoch.AddSeconds(8), retriedEvidence.Timestamp);
        Assert.True(retriedEvidence.TouchAndGoConfirmed);
        Assert.False(retriedEvidence.TouchdownConfirmed);
        Assert.False(retriedEvidence.BounceRecontact);

        Assert.False(await runtime.RefreshAsync());
        Assert.Equal(Epoch.AddSeconds(8), telemetry.LastReadAfterTimestamp);
        Assert.Equal(afterRetry, coordinator.Current);
        Assert.Equal(saveCountBeforeFailure + 1, store.SaveCount);
        Assert.Equal(1, sessionChanged);
        Assert.Single(publishedEvidence);
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

    [Theory]
    [InlineData(0.5, 0.5, 0.5)]
    [InlineData(1.0, 1.0, 1.0)]
    [InlineData(2.0, 2.0, 1.0)]
    [InlineData(4.0, 4.0, 1.0)]
    public async Task RuntimeUsesTelemetrySimulationRateForAuthoritativeTime(
        double simulationRate,
        double expectedSimulatedSeconds,
        double expectedCareerSeconds)
    {
        FlightSession active = AirborneSession();
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(active);
        var store = new MemoryStore { Checkpoint = active };
        var telemetry = new TestTelemetrySource
        {
            Latest = Telemetry(
                Epoch.AddSeconds(5),
                32,
                -97,
                onGround: false,
                simulationRate: simulationRate)
        };
        var runtime = CreateRuntime(coordinator, store, Connected(), telemetry);

        Assert.True(await runtime.RefreshAsync());

        telemetry.Latest = Telemetry(
            Epoch.AddSeconds(6),
            32,
            -97,
            onGround: false,
            simulationRate: simulationRate);
        Assert.True(await runtime.RefreshAsync());

        FlightTimeLedger ledger = coordinator.Current!.TimeLedger;
        Assert.Equal(TimeSpan.FromSeconds(1), ledger.ObservedWallTime);
        Assert.Equal(TimeSpan.FromSeconds(expectedSimulatedSeconds), ledger.SimulatedOperationalTime);
        Assert.Equal(TimeSpan.FromSeconds(expectedSimulatedSeconds), ledger.BlockTime);
        Assert.Equal(TimeSpan.FromSeconds(expectedSimulatedSeconds), ledger.MovementFlightTime);
        Assert.Equal(TimeSpan.FromSeconds(expectedSimulatedSeconds), ledger.AirborneTime);
        Assert.Equal(TimeSpan.FromSeconds(expectedCareerSeconds), ledger.CareerCreditTime);
        Assert.Equal(
            simulationRate > 1d
                ? TimeSpan.FromSeconds(1)
                : TimeSpan.Zero,
            ledger.AcceleratedWallTime);
    }

    [Fact]
    public async Task InvalidSimulationRateSkipsAuthoritativeTimeInterval()
    {
        FlightSession active = AirborneSession();
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(active);
        var store = new MemoryStore { Checkpoint = active };
        var telemetry = new TestTelemetrySource
        {
            Latest = Telemetry(
                Epoch.AddSeconds(5),
                32,
                -97,
                onGround: false)
        };
        var runtime = CreateRuntime(coordinator, store, Connected(), telemetry);

        Assert.True(await runtime.RefreshAsync());

        telemetry.Latest = Telemetry(
            Epoch.AddSeconds(6),
            32,
            -97,
            onGround: false,
            simulationRate: 0);
        Assert.True(await runtime.RefreshAsync());

        Assert.Equal(FlightTimeLedger.Empty, coordinator.Current!.TimeLedger);
    }

    [Theory]
    [InlineData(false, 1.0, 0.0)]
    [InlineData(true, 1.0, 1.0)]
    [InlineData(true, 4.0, 1.0)]
    [InlineData(null, 1.0, 0.0)]
    public async Task RuntimeCreditsNightOnlyFromAffirmativeEvidence(
        bool? isNight,
        double simulationRate,
        double expectedNightSeconds)
    {
        FlightSession active = AirborneSession();
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(active);
        var store = new MemoryStore { Checkpoint = active };
        var telemetry = new TestTelemetrySource
        {
            Latest = Telemetry(
                Epoch.AddSeconds(5),
                32,
                -97,
                onGround: false,
                simulationRate: simulationRate,
                isNight: isNight)
        };
        var runtime = CreateRuntime(coordinator, store, Connected(), telemetry);

        Assert.True(await runtime.RefreshAsync());

        telemetry.Latest = Telemetry(
            Epoch.AddSeconds(6),
            32,
            -97,
            onGround: false,
            simulationRate: simulationRate,
            isNight: isNight);
        Assert.True(await runtime.RefreshAsync());

        FlightTimeLedger ledger = coordinator.Current!.TimeLedger;
        Assert.Equal(TimeSpan.FromSeconds(1), ledger.CareerCreditTime);
        Assert.Equal(
            TimeSpan.FromSeconds(expectedNightSeconds),
            ledger.NightCareerCreditTime);
        Assert.True(
            ledger.NightCareerCreditTime
            <= ledger.CareerCreditTime);
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

    private static FlightSession AirborneSession(
        double altitudeMsl = 10_000)
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
                            altitudeMsl,
                            OnGround: false)));

        return session;
    }

    private static FlightSession ApproachSession()
    {
        FlightSession session =
            AirborneSession(700);

        return FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(5),
                    Connected: true,
                    ContinuityPlausible: true,
                    ApproachConfirmed: true),
                ContinuityAnchor:
                    new FlightContinuityAnchor(
                        Epoch.AddSeconds(5),
                        32,
                        -97,
                        700,
                        OnGround: false)));
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

    private static FlightSession ContractSession(
        FlightSession session,
        string expectedCanonicalAircraftId) =>
        session with
        {
            ContractId =
                Guid.Parse("97000000-0000-0000-0000-000000000001"),
            Plan =
                new FlightSessionPlan(
                    "KJFK",
                    "KJFK",
                    ExpectedCanonicalAircraftId:
                        expectedCanonicalAircraftId)
        };

    private static AircraftTelemetrySnapshot Telemetry(
        DateTimeOffset timestamp,
        double latitude,
        double longitude,
        bool onGround,
        double altitudeMsl = 650,
        double groundSpeed = 0,
        double? altitudeAgl = null,
        double simulationRate = 1d,
        bool? isNight = null) =>
        new(
            timestamp,
            latitude,
            longitude,
            altitudeMsl,
            altitudeAgl
                ?? (onGround ? 0 : 2_000),
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
            false,
            simulationRate,
            isNight);

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

    private sealed class TestLoadedAircraftIdentitySource(
        string? canonicalAircraftId = null)
        : ICurrentLoadedAircraftIdentitySource
    {
        public string? CanonicalAircraftId { get; set; } =
            canonicalAircraftId;

        public CurrentLoadedAircraftIdentitySnapshot Current =>
            string.IsNullOrWhiteSpace(CanonicalAircraftId)
                ? CurrentLoadedAircraftIdentitySnapshot.Unavailable
                : CurrentLoadedAircraftIdentitySnapshot.Identified(
                    CanonicalAircraftId);
    }

    private sealed class BufferedTestTelemetrySource :
        ISimulatorTelemetrySource,
        IFlightCriticalTelemetrySource
    {
        private readonly List<AircraftTelemetrySnapshot> _samples = [];

        public AircraftTelemetrySnapshot? Latest { get; private set; }

        public DateTimeOffset? LastReadAfterTimestamp { get; private set; }

        public void Add(AircraftTelemetrySnapshot sample)
        {
            if (_samples.Count > 0
                && sample.Timestamp <= _samples[^1].Timestamp)
            {
                return;
            }

            _samples.Add(sample);
            Latest = sample;
        }

        public IReadOnlyList<AircraftTelemetrySnapshot> ReadAfter(
            DateTimeOffset? exclusiveTimestamp)
        {
            LastReadAfterTimestamp = exclusiveTimestamp;

            return _samples
                .Where(sample =>
                    exclusiveTimestamp is null
                    || sample.Timestamp > exclusiveTimestamp.Value)
                .ToArray();
        }

        public void Clear() =>
            _samples.Clear();
    }

    private sealed class MemoryStore :
        IFlightSessionCheckpointStore
    {
        public FlightSession? Checkpoint { get; set; }
        public FlightSession? FailedAttempt { get; private set; }
        public int SaveCount { get; private set; }
        public bool FailNextSave { get; set; }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                FailedAttempt = session;
                throw new IOException("Injected checkpoint failure.");
            }

            SaveCount++;
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
