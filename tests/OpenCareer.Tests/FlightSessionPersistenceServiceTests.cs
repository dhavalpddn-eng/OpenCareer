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

    private sealed class MemoryStore :
        IFlightSessionCheckpointStore
    {
        public FlightSession? Checkpoint { get; set; }

        public bool FailWrites { get; set; }

        public int SaveCount { get; private set; }

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
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Checkpoint);

        public Task ClearAsync(
            CancellationToken cancellationToken = default)
        {
            Checkpoint = null;
            return Task.CompletedTask;
        }
    }
}
