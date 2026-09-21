using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class FlightSessionPersistenceServiceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 15, 0, 0, TimeSpan.Zero);

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
    public async Task StartWaitsForRecoveryAndCannotOverwriteRecoveredSession()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new DeferredRecoveryStore();
        var service = new FlightSessionPersistenceService(coordinator, store);
        Task<FlightSession?> recovery = service.RecoverAsync();
        Task<FlightSession> start = service.StartAsync(Epoch.AddSeconds(1));

        Assert.False(start.IsCompleted);
        Assert.Null(coordinator.Current);
        store.LoadCompletion.SetResult(FlightSession.Start(Epoch));
        FlightSession? recovered = await recovery;

        await Assert.ThrowsAsync<InvalidOperationException>(() => start);
        Assert.Same(recovered, coordinator.Current);
        Assert.Same(recovered, store.Checkpoint);
    }

    [Fact]
    public async Task CancelledQueuedFlushDoesNotReleaseRecoveryGate()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new DeferredRecoveryStore();
        var service = new FlightSessionPersistenceService(coordinator, store);
        Task<FlightSession?> recovery = service.RecoverAsync();
        using var cancellation = new CancellationTokenSource();
        Task flush = service.FlushAsync(cancellation.Token);
        Assert.False(flush.IsCompleted);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush);
        Task<FlightSession> start = service.StartAsync(Epoch.AddSeconds(1));
        Assert.False(start.IsCompleted);
        Assert.Null(coordinator.Current);

        store.LoadCompletion.SetResult(FlightSession.Start(Epoch));
        FlightSession? recovered = await recovery;
        await Assert.ThrowsAsync<InvalidOperationException>(() => start);
        await service.FlushAsync();
        Assert.Same(recovered, coordinator.Current);
        Assert.Same(recovered, store.Checkpoint);
    }

    [Fact]
    public async Task FailedRecoveryReadReleasesGateForRetry()
    {
        var coordinator = new FlightSessionCoordinator();
        var store = new DeferredRecoveryStore();
        var service = new FlightSessionPersistenceService(coordinator, store);
        Task<FlightSession?> failedRecovery = service.RecoverAsync();
        store.LoadCompletion.SetException(new IOException("Synthetic read failure."));

        await Assert.ThrowsAsync<IOException>(() => failedRecovery);
        Assert.Null(coordinator.Current);
        Assert.Null(service.LastRecoveredSessionId);
        Assert.Null(store.Checkpoint);

        var checkpoint = FlightSession.Start(Epoch);
        store.LoadCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        store.LoadCompletion.SetResult(checkpoint);
        FlightSession? recovered = await service.RecoverAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(recovered);
        Assert.Equal(checkpoint.SessionId, recovered.SessionId);
        Assert.Equal(FlightSessionStatus.Suspended, recovered.Status);
        Assert.Equal(checkpoint.SessionId, service.LastRecoveredSessionId);
        Assert.Same(recovered, coordinator.Current);
        Assert.Same(recovered, store.Checkpoint);
    }

    [Fact]
    public async Task FailedRecoveryWritePreservesCheckpointAndAllowsRetry()
    {
        var checkpoint = FlightSession.Start(Epoch);
        var coordinator = new FlightSessionCoordinator();
        var store = new MemoryStore { Checkpoint = checkpoint, FailWrites = true };
        var service = new FlightSessionPersistenceService(coordinator, store);

        await Assert.ThrowsAsync<IOException>(() => service.RecoverAsync());
        Assert.Null(coordinator.Current);
        Assert.Null(service.LastRecoveredSessionId);
        Assert.Same(checkpoint, store.Checkpoint);
        Assert.Equal(0, store.SaveCount);

        store.FailWrites = false;
        FlightSession? recovered = await service.RecoverAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(recovered);
        Assert.Equal(checkpoint.SessionId, recovered.SessionId);
        Assert.Equal(FlightSessionStatus.Suspended, recovered.Status);
        Assert.Equal(checkpoint.SessionId, service.LastRecoveredSessionId);
        Assert.Same(recovered, coordinator.Current);
        Assert.Same(recovered, store.Checkpoint);
        Assert.Equal(1, store.SaveCount);
    }

    private sealed class DeferredRecoveryStore : IFlightSessionCheckpointStore
    {
        public TaskCompletionSource<FlightSession?> LoadCompletion { get; set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FlightSession? Checkpoint { get; private set; }

        public Task<FlightSession?> LoadAsync(CancellationToken cancellationToken = default) =>
            LoadCompletion.Task;

        public Task SaveAsync(FlightSession session, CancellationToken cancellationToken = default)
        {
            Checkpoint = session;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Checkpoint = null;
            return Task.CompletedTask;
        }
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
