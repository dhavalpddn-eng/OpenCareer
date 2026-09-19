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

        Assert.Equal(
            checkpoint,
            recovered);

        Assert.Equal(
            checkpoint,
            coordinator.Current);
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
                    "Synthetic persistence failure.");
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
}
