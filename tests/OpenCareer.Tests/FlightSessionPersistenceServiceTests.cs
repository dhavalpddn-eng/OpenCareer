using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Flights;

namespace OpenCareer.Tests;

public sealed class FlightSessionPersistenceServiceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 15, 0, 0, TimeSpan.Zero);

    private static readonly FlightLegTerminalEvidence CompleteTerminalEvidence =
        new(
            AtAcceptedTerminal: true,
            OnGround: true,
            Stationary: true,
            ParkingBrakeSet: true,
            EnginesRunning: 0,
            ServicingComplete: true);

    [Fact]
    public async Task StartPersistsBeforePublishingSession()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore();

        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        FlightSession session =
            await service.StartAsync(
                Epoch,
                sessionId:
                    Guid.Parse(
                        "33333333-3333-3333-3333-333333333333"));

        Assert.Equal(
            session,
            store.Checkpoint);

        Assert.Equal(
            session,
            coordinator.Current);
    }

    [Fact]
    public async Task StartPreservesTerminalCheckpointUntilExplicitCleanup()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore();
        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        FlightSession started =
            await service.StartAsync(
                Epoch,
                sessionId:
                    Guid.Parse(
                        "33333333-3333-3333-3333-333333333333"));

        FlightSession cancelled =
            await service.CancelAsync(
                started.SessionId,
                expectedContractId: null,
                requestedAt: Epoch.AddSeconds(1));
        int saveCount = store.SaveCount;

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.StartAsync(
                    Epoch.AddSeconds(2),
                    sessionId:
                        Guid.Parse(
                            "44444444-4444-4444-4444-444444444444")));

        Assert.Contains("cleared", error.Message);
        Assert.Equal(cancelled, coordinator.Current);
        Assert.Equal(cancelled, store.Checkpoint);
        Assert.Equal(saveCount, store.SaveCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAfterRestartPreservesUnrecoveredCheckpoint(
        bool terminal)
    {
        Guid contractId =
            Guid.Parse(
                "22222222-2222-2222-2222-222222222222");

        FlightSession checkpoint =
            FlightSession.Start(
                Epoch,
                contractId,
                Guid.Parse(
                    "33333333-3333-3333-3333-333333333333"));

        if (terminal)
        {
            checkpoint =
                FlightSessionEngine.Advance(
                    checkpoint,
                    new FlightSessionAdvance(
                        new FlightStateEvidence(
                            Epoch.AddSeconds(1),
                            Connected: true),
                        CancelRequested: true));
        }

        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore
            {
                Checkpoint = checkpoint
            };

        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.StartAsync(
                    Epoch.AddMinutes(1),
                    Guid.Parse(
                        "44444444-4444-4444-4444-444444444444"),
                    Guid.Parse(
                        "55555555-5555-5555-5555-555555555555")));

        Assert.Contains("recovered or cleared", error.Message);
        Assert.Same(checkpoint, store.Checkpoint);
        Assert.Null(coordinator.Current);
        Assert.Equal(1, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task FailedCheckpointWriteDoesNotAdvanceInMemorySession()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore();

        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        FlightSession started =
            await service.StartAsync(Epoch);

        store.FailWrites = true;

        await Assert.ThrowsAsync<IOException>(
            async () =>
                await service.AdvanceAsync(
                    new FlightSessionAdvance(
                        new FlightStateEvidence(
                            Epoch.AddSeconds(1),
                            Connected: true,
                            StableTelemetry: true,
                            ValidLoadedAircraft: true,
                            ContinuityPlausible: true))));

        Assert.Equal(
            started,
            coordinator.Current);
    }

    [Fact]
    public async Task FailedTimedCheckpointRetryDoesNotDoubleCountLegOrSessionTime()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore();
        var service = new FlightSessionPersistenceService(coordinator, store);
        FlightSession started = await service.StartAsync(Epoch);
        var update =
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(1),
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true),
                TimeInterval:
                    new FlightTimeInterval(
                        TimeSpan.FromSeconds(1),
                        1,
                        ValidOperationalEvidence: true,
                        Paused: false,
                        SlewActive: false,
                        CountsTowardBlockTime: true,
                        CountsTowardFlightTime: true,
                        Airborne: true,
                        TaxiOut: false,
                        TaxiIn: false,
                        Night: true,
                        ActualInstrument: true),
                ContinuityAnchor:
                    new FlightContinuityAnchor(
                        Epoch.AddSeconds(1),
                        40,
                        -73,
                        2_000,
                        OnGround: false),
                Observation:
                    new FlightSessionObservation(
                        Epoch.AddSeconds(1),
                        40,
                        -73,
                        2_000,
                        100,
                        100,
                        100,
                        10,
                        CaptureTrackPoint: true));

        store.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(() => service.AdvanceAsync(update));
        Assert.Equal(started, coordinator.Current);

        store.FailWrites = false;
        FlightSession retried = await service.AdvanceAsync(update);

        Assert.Equal(TimeSpan.FromSeconds(1), retried.TimeLedger.CareerCreditTime);
        Assert.Equal(
            retried.TimeLedger,
            Assert.Single(retried.EffectiveLegs).EffectiveTimeLedger);
        Assert.Single(retried.EffectiveStatistics.RouteTrack);
        Assert.Single(
            Assert.Single(retried.EffectiveLegs)
                .EffectiveStatistics.RouteTrack);
    }

    [Fact]
    public async Task FailedLandingCheckpointRetryDoesNotDuplicateLegReference()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore();
        var service = new FlightSessionPersistenceService(coordinator, store);
        await service.StartAsync(Epoch);

        foreach (FlightStateEvidence evidence in new[]
        {
            Evidence(1, stable: true, validAircraft: true),
            Evidence(2, engineStart: true),
            Evidence(3, movement: true),
            Evidence(4, takeoffCandidate: true),
            Evidence(5, airborne: true),
            Evidence(6, approach: true)
        })
        {
            await service.AdvanceAsync(new FlightSessionAdvance(evidence));
        }

        FlightSession beforeLanding = coordinator.Current!;
        var landing =
            new FlightSessionAdvance(
                Evidence(7, touchdown: true));

        store.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(() => service.AdvanceAsync(landing));
        Assert.Equal(beforeLanding, coordinator.Current);

        store.FailWrites = false;
        FlightSession retried = await service.AdvanceAsync(landing);

        Assert.Single(retried.EffectiveLandingEpisodes);
        Assert.Equal(
            [1],
            Assert.Single(retried.EffectiveLegs).EffectiveLandingEpisodeNumbers);
    }

    [Fact]
    public async Task InsufficientTerminalEvidenceDoesNotCompleteCurrentLeg()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore();
        var service = new FlightSessionPersistenceService(coordinator, store);
        FlightSession started = await service.StartAsync(Epoch);
        int saveCount = store.SaveCount;

        FlightLegTerminalEvidence incomplete =
            CompleteTerminalEvidence with { ServicingComplete = false };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteCurrentLegAsync(
                started.SessionId,
                started.ContractId,
                Epoch.AddMinutes(1),
                incomplete,
                FlightLegTerminalPolicy.ConventionalCommercialTurnaround));

        Assert.Equal(started, coordinator.Current);
        Assert.Equal(saveCount, store.SaveCount);
    }

    [Fact]
    public async Task SatisfiedPolicyCompletesLegOnceAndKeepsSessionActive()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore();
        var service = new FlightSessionPersistenceService(coordinator, store);
        FlightSession started = await service.StartAsync(Epoch);

        FlightSession completedLeg =
            await service.CompleteCurrentLegAsync(
                started.SessionId,
                started.ContractId,
                Epoch.AddMinutes(1),
                CompleteTerminalEvidence,
                FlightLegTerminalPolicy.ConventionalCommercialTurnaround);

        FlightLeg leg = Assert.Single(completedLeg.EffectiveLegs);
        Assert.Equal(FlightLegStatus.Completed, leg.Status);
        Assert.Equal(Epoch.AddMinutes(1), leg.CompletedAt);
        Assert.Equal(FlightSessionStatus.Active, completedLeg.Status);
        Assert.Equal(started.OperationState, completedLeg.OperationState);
        Assert.Equal(started.Tracking, completedLeg.Tracking);
        Assert.Equal(completedLeg, store.Checkpoint);

        int saveCount = store.SaveCount;
        FlightSession replay =
            await service.CompleteCurrentLegAsync(
                started.SessionId,
                started.ContractId,
                Epoch.AddMinutes(2),
                CompleteTerminalEvidence,
                FlightLegTerminalPolicy.ConventionalCommercialTurnaround);

        Assert.Equal(completedLeg, replay);
        Assert.Equal(saveCount, store.SaveCount);
        Assert.Equal(
            Epoch.AddMinutes(1),
            Assert.Single(replay.EffectiveLegs).CompletedAt);
    }

    [Fact]
    public async Task FailedLegCompletionWriteLeavesPublishedStateUnchangedAndRetryIsStable()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore();
        var service = new FlightSessionPersistenceService(coordinator, store);
        FlightSession started = await service.StartAsync(Epoch);

        store.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(
            () => service.CompleteCurrentLegAsync(
                started.SessionId,
                started.ContractId,
                Epoch.AddMinutes(1),
                CompleteTerminalEvidence,
                FlightLegTerminalPolicy.ConventionalCommercialTurnaround));

        Assert.Equal(started, coordinator.Current);
        Assert.Equal(
            FlightLegStatus.Active,
            Assert.Single(coordinator.Current!.EffectiveLegs).Status);

        store.FailWrites = false;
        FlightSession retried =
            await service.CompleteCurrentLegAsync(
                started.SessionId,
                started.ContractId,
                Epoch.AddMinutes(1),
                CompleteTerminalEvidence,
                FlightLegTerminalPolicy.ConventionalCommercialTurnaround);

        Assert.Equal(Epoch.AddMinutes(1), Assert.Single(retried.EffectiveLegs).CompletedAt);
        Assert.Equal(retried, store.Checkpoint);
    }

    [Fact]
    public async Task CompletedCurrentLegSurvivesSqliteRestart()
    {
        string directory =
            Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(directory, "career.db");

        try
        {
            var coordinator = new FlightSessionCoordinator();
            var service =
                new FlightSessionPersistenceService(
                    coordinator,
                    new SqliteFlightSessionCheckpointStore(databasePath));

            FlightSession started = await service.StartAsync(Epoch);
            await service.CompleteCurrentLegAsync(
                started.SessionId,
                started.ContractId,
                Epoch.AddMinutes(1),
                CompleteTerminalEvidence,
                FlightLegTerminalPolicy.ConventionalCommercialTurnaround);

            var restoredCoordinator = new FlightSessionCoordinator();
            var restoredService =
                new FlightSessionPersistenceService(
                    restoredCoordinator,
                    new SqliteFlightSessionCheckpointStore(databasePath));

            FlightSession restored =
                Assert.IsType<FlightSession>(await restoredService.RecoverAsync());

            FlightLeg restoredLeg = Assert.Single(restored.EffectiveLegs);
            Assert.Equal(FlightLegStatus.Completed, restoredLeg.Status);
            Assert.Equal(Epoch.AddMinutes(1), restoredLeg.CompletedAt);
            Assert.False(restored.IsTerminal);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NextLegRequiresCompletedCurrentLeg()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore();
        var service = new FlightSessionPersistenceService(coordinator, store);
        FlightSession started = await service.StartAsync(Epoch);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartNextLegAsync(
                started.SessionId,
                started.ContractId,
                Guid.NewGuid(),
                new FlightSessionPlan("KDFW", "KIAH"),
                Epoch.AddMinutes(1)));

        Assert.Single(coordinator.Current!.EffectiveLegs);
        Assert.Equal(started, store.Checkpoint);
    }

    [Fact]
    public async Task NextLegRejectsEmptyOrDuplicateIdentity()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore();
        var service = new FlightSessionPersistenceService(coordinator, store);
        FlightSession started = await service.StartAsync(Epoch);
        FlightSession completed = await CompleteCurrentLegAsync(service, started);
        var plan = new FlightSessionPlan("KDFW", "KIAH");

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.StartNextLegAsync(
                started.SessionId,
                started.ContractId,
                Guid.Empty,
                plan,
                Epoch.AddMinutes(2)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartNextLegAsync(
                started.SessionId,
                started.ContractId,
                started.SessionId,
                plan,
                Epoch.AddMinutes(2)));

        Assert.Equal(completed, coordinator.Current);
    }

    [Fact]
    public async Task NextLegPersistsOnceAndConflictingReplayFailsClosed()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore();
        var service = new FlightSessionPersistenceService(coordinator, store);
        FlightSession started = await service.StartAsync(Epoch);
        await CompleteCurrentLegAsync(service, started);

        Guid nextLegId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var nextPlan = new FlightSessionPlan("KDFW", "KIAH");
        FlightSession next =
            await service.StartNextLegAsync(
                started.SessionId,
                started.ContractId,
                nextLegId,
                nextPlan,
                Epoch.AddMinutes(2));

        Assert.Equal(started.SessionId, next.SessionId);
        Assert.Equal(2, next.EffectiveLegs.Count);
        Assert.Equal(2, next.EffectiveLegs[^1].Sequence);
        Assert.Equal(nextLegId, next.EffectiveLegs[^1].LegId);
        Assert.Equal(FlightLegStatus.Active, next.EffectiveLegs[^1].Status);
        Assert.Equal(FlightTrackingState.Preflight, next.Tracking.State);
        Assert.Equal(FlightOperationState.ReadyForStart, next.OperationState);

        int saveCount = store.SaveCount;
        FlightSession replay =
            await service.StartNextLegAsync(
                started.SessionId,
                started.ContractId,
                nextLegId,
                nextPlan,
                Epoch.AddMinutes(2));

        Assert.Equal(next, replay);
        Assert.Equal(saveCount, store.SaveCount);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartNextLegAsync(
                started.SessionId,
                started.ContractId,
                Guid.NewGuid(),
                nextPlan,
                Epoch.AddMinutes(2)));
        Assert.Equal(next, coordinator.Current);
    }

    [Fact]
    public async Task FailedNextLegSavePublishesNothingAndRetryAppendsOnce()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore();
        var service = new FlightSessionPersistenceService(coordinator, store);
        FlightSession started = await service.StartAsync(Epoch);
        FlightSession completed = await CompleteCurrentLegAsync(service, started);
        Guid nextLegId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var nextPlan = new FlightSessionPlan("KDFW", "KIAH");

        store.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(
            () => service.StartNextLegAsync(
                started.SessionId,
                started.ContractId,
                nextLegId,
                nextPlan,
                Epoch.AddMinutes(2)));

        Assert.Equal(completed, coordinator.Current);
        Assert.Single(coordinator.Current!.EffectiveLegs);

        store.FailWrites = false;
        FlightSession retried =
            await service.StartNextLegAsync(
                started.SessionId,
                started.ContractId,
                nextLegId,
                nextPlan,
                Epoch.AddMinutes(2));

        Assert.Equal(2, retried.EffectiveLegs.Count);
        Assert.Equal(nextLegId, retried.EffectiveLegs[^1].LegId);
    }

    [Fact]
    public async Task TwoLegSessionSurvivesSqliteRestart()
    {
        string directory =
            Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(directory, "career.db");

        try
        {
            var coordinator = new FlightSessionCoordinator();
            var service =
                new FlightSessionPersistenceService(
                    coordinator,
                    new SqliteFlightSessionCheckpointStore(databasePath));
            FlightSession started = await service.StartAsync(Epoch);
            await CompleteCurrentLegAsync(service, started);
            Guid nextLegId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            await service.StartNextLegAsync(
                started.SessionId,
                started.ContractId,
                nextLegId,
                new FlightSessionPlan("KDFW", "KIAH"),
                Epoch.AddMinutes(2));

            var restoredCoordinator = new FlightSessionCoordinator();
            var restoredService =
                new FlightSessionPersistenceService(
                    restoredCoordinator,
                    new SqliteFlightSessionCheckpointStore(databasePath));
            FlightSession restored =
                Assert.IsType<FlightSession>(await restoredService.RecoverAsync());

            Assert.Equal(started.SessionId, restored.SessionId);
            Assert.Equal(2, restored.EffectiveLegs.Count);
            Assert.Equal(FlightLegStatus.Completed, restored.EffectiveLegs[0].Status);
            Assert.Equal(nextLegId, restored.EffectiveLegs[1].LegId);
            Assert.Equal(FlightLegStatus.Active, restored.EffectiveLegs[1].Status);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RecoverRestoresCheckpointIntoEmptyCoordinator()
    {
        FlightSession checkpoint =
            FlightSession.Start(
                Epoch,
                sessionId:
                    Guid.Parse(
                        "44444444-4444-4444-4444-444444444444"));

        var store =
            new MemoryStore
            {
                Checkpoint = checkpoint
            };

        var coordinator =
            new FlightSessionCoordinator();

        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        FlightSession? recovered =
            await service.RecoverAsync();

        Assert.NotNull(recovered);

        Assert.Equal(
            checkpoint.SessionId,
            recovered!.SessionId);

        Assert.Equal(
            FlightSessionStatus.Suspended,
            recovered.Status);

        Assert.Equal(
            FlightTrackingState.Suspended,
            recovered.Tracking.State);

        Assert.Equal(
            checkpoint.SessionId,
            service.LastRecoveredSessionId);

        Assert.Equal(
            recovered,
            store.Checkpoint);

        Assert.Equal(
            recovered,
            coordinator.Current);
    }

    [Fact]
    public async Task RecoverPreservesTerminalSessionUntilExplicitCleanup()
    {
        var coordinator = new FlightSessionCoordinator();
        FlightSession terminal =
            FlightSessionEngine.Advance(
                FlightSession.Start(Epoch),
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(1),
                        Connected: true),
                    CancelRequested: true));
        coordinator.Restore(terminal);

        FlightSession checkpoint =
            FlightSession.Start(
                Epoch.AddSeconds(2),
                sessionId:
                    Guid.Parse(
                        "55555555-5555-5555-5555-555555555555"));
        var store =
            new MemoryStore
            {
                Checkpoint = checkpoint
            };
        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RecoverAsync());

        Assert.Contains("cleared", error.Message);
        Assert.Equal(terminal, coordinator.Current);
        Assert.Equal(checkpoint, store.Checkpoint);
        Assert.Equal(0, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
        Assert.Null(service.LastRecoveredSessionId);

        coordinator.ClearTerminalSession();
        FlightSession recovered =
            Assert.IsType<FlightSession>(
                await service.RecoverAsync());

        Assert.Equal(checkpoint.SessionId, recovered.SessionId);
        Assert.Equal(FlightSessionStatus.Suspended, recovered.Status);
        Assert.Equal(1, store.LoadCount);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(recovered, coordinator.Current);
    }

    [Fact]
    public async Task FlushWritesLatestInMemorySessionEvenBeforeCadenceExpires()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore();

        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store,
                new FlightSessionCheckpointPolicy(
                    TimeSpan.FromMinutes(5)));

        await service.StartAsync(Epoch);

        await service.AdvanceAsync(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(1),
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true)));

        await service.AdvanceAsync(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(2),
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true)));

        Assert.Equal(
            Epoch.AddSeconds(1),
            store.Checkpoint?.UpdatedAt);

        await service.FlushAsync();

        Assert.Equal(
            Epoch.AddSeconds(2),
            store.Checkpoint?.UpdatedAt);
    }

    [Fact]
    public async Task SteadyStateTelemetryDoesNotWriteEverySample()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore();

        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store,
                new FlightSessionCheckpointPolicy(
                    TimeSpan.FromSeconds(30)));

        await service.StartAsync(Epoch);

        await service.AdvanceAsync(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(1),
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true)));

        await service.AdvanceAsync(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(5),
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true)));

        Assert.Equal(
            2,
            store.SaveCount);

        Assert.Equal(
            Epoch.AddSeconds(1),
            store.Checkpoint?.UpdatedAt);

        Assert.Equal(
            Epoch.AddSeconds(5),
            coordinator.Current?.UpdatedAt);
    }

    [Fact]
    public async Task SteadyStateCheckpointOccursAtMaximumInterval()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore();

        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store,
                new FlightSessionCheckpointPolicy(
                    TimeSpan.FromSeconds(30)));

        await service.StartAsync(Epoch);

        await service.AdvanceAsync(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(1),
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true)));

        await service.AdvanceAsync(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(31),
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true)));

        Assert.Equal(
            3,
            store.SaveCount);

        Assert.Equal(
            Epoch.AddSeconds(31),
            store.Checkpoint?.UpdatedAt);
    }

    [Fact]
    public async Task OperationalTransitionCheckpointsImmediately()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore();

        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store,
                new FlightSessionCheckpointPolicy(
                    TimeSpan.FromMinutes(5)));

        await service.StartAsync(Epoch);

        await service.AdvanceAsync(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(1),
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true)));

        await service.AdvanceAsync(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(2),
                    Connected: true,
                    ContinuityPlausible: true,
                    EngineStartObserved: true)));

        Assert.Equal(
            3,
            store.SaveCount);

        Assert.Equal(
            FlightOperationState.EngineStart,
            store.Checkpoint?.OperationState);
    }

    [Fact]
    public async Task ClearingTerminalSessionClearsPersistenceFirst()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore();

        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        await service.StartAsync(Epoch);

        await service.AdvanceAsync(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(1),
                    Connected: true),
                CancelRequested: true));

        await service.ClearTerminalAsync();

        Assert.Null(store.Checkpoint);
        Assert.Null(coordinator.Current);
    }

    [Fact]
    public async Task TerminalClearRestoresReplacementCheckpointBeforeRefusingStaleCleanup()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore
            {
                PauseNextClear = true
            };

        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        await service.StartAsync(Epoch);

        FlightSession terminal =
            await service.AdvanceAsync(
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(1),
                        Connected: true),
                    CancelRequested: true));

        Task clear =
            service.ClearTerminalAsync(
                terminal.SessionId,
                terminal.ContractId);

        await store.ClearStarted.Task;

        coordinator.ClearTerminalSession();

        FlightSession replacement =
            FlightSessionEngine.Advance(
                FlightSession.Start(
                    Epoch.AddMinutes(1),
                    sessionId:
                        Guid.Parse(
                            "77777777-7777-7777-7777-777777777777")),
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddMinutes(1).AddSeconds(1),
                        Connected: true),
                    CancelRequested: true));

        coordinator.Restore(replacement);
        store.AllowClear.TrySetResult();

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => clear);

        Assert.Contains("changed", error.Message);
        Assert.Equal(replacement, coordinator.Current);
        Assert.Equal(replacement, store.Checkpoint);
        Assert.Equal(3, store.SaveCount);
    }

    [Fact]
    public async Task AuthoritativeCancellationValidatesIdentityBeforeWriting()
    {
        Guid sessionId = Guid.NewGuid();
        Guid contractId = Guid.NewGuid();
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore();
        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        await service.StartAsync(
            Epoch,
            contractId,
            sessionId);

        int saveCount = store.SaveCount;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CancelAsync(
                sessionId,
                Guid.NewGuid(),
                Epoch.AddSeconds(1)));

        Assert.Equal(saveCount, store.SaveCount);
        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current!.Status);
    }

    [Fact]
    public async Task LaterTelemetryCannotOverwritePersistedCancellation()
    {
        Guid sessionId = Guid.NewGuid();
        Guid contractId = Guid.NewGuid();
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore();
        var service =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        await service.StartAsync(
            Epoch,
            contractId,
            sessionId);

        await service.CancelAsync(
            sessionId,
            contractId,
            Epoch.AddSeconds(1));

        FlightSession afterTelemetry =
            await service.AdvanceAsync(
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(2),
                        Connected: true,
                        StableTelemetry: true,
                        ValidLoadedAircraft: true,
                        ContinuityPlausible: true)));

        Assert.Equal(
            FlightSessionStatus.Cancelled,
            afterTelemetry.Status);
        Assert.Equal(
            FlightSessionStatus.Cancelled,
            store.Checkpoint!.Status);
        Assert.Equal(
            FlightOperationState.Cancelled,
            coordinator.Current!.OperationState);
    }

    private static Task<FlightSession> CompleteCurrentLegAsync(
        FlightSessionPersistenceService service,
        FlightSession started) =>
        service.CompleteCurrentLegAsync(
            started.SessionId,
            started.ContractId,
            Epoch.AddMinutes(1),
            CompleteTerminalEvidence,
            FlightLegTerminalPolicy.ConventionalCommercialTurnaround);

    private static FlightStateEvidence Evidence(
        int seconds,
        bool stable = false,
        bool validAircraft = false,
        bool engineStart = false,
        bool movement = false,
        bool takeoffCandidate = false,
        bool airborne = false,
        bool approach = false,
        bool touchdown = false) =>
        new(
            Epoch.AddSeconds(seconds),
            Connected: true,
            StableTelemetry: stable,
            ValidLoadedAircraft: validAircraft,
            ContinuityPlausible: true,
            EngineStartObserved: engineStart,
            SelfPoweredMovementForFlight: movement,
            TakeoffCandidate: takeoffCandidate,
            AirborneConfirmed: airborne,
            ApproachConfirmed: approach,
            TouchdownConfirmed: touchdown);

    private sealed class MemoryStore :
        IFlightSessionCheckpointStore
    {
        public FlightSession? Checkpoint { get; set; }

        public bool FailWrites { get; set; }

        public bool PauseNextClear { get; set; }

        public TaskCompletionSource ClearStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowClear { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SaveCount { get; private set; }

        public int LoadCount { get; private set; }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            if (FailWrites)
            {
                throw new IOException(
                    "Synthetic persistence failure.");
            }

            SaveCount++;
            Checkpoint = session;
            return Task.CompletedTask;
        }

        public Task<FlightSession?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(Checkpoint);
        }

        public async Task ClearAsync(
            CancellationToken cancellationToken = default)
        {
            Checkpoint = null;

            if (!PauseNextClear)
                return;

            PauseNextClear = false;
            ClearStarted.TrySetResult();

            await AllowClear.Task
                .WaitAsync(cancellationToken);
        }
    }
}
