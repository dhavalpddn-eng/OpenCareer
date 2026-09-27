using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class JobBoardGenerationServiceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MissingBoardIsGeneratedPersistedAndReturned()
    {
        var store = new FakeStore();
        var service =
            new JobBoardGenerationService(store);

        JobBoardState result =
            await service.RefreshAsync(
                Request(Epoch));

        Assert.Equal("KRME", result.AirportIcao);
        Assert.NotEmpty(result.Offers);
        Assert.Equal(1, store.SaveCount);
        Assert.Same(store.State, result);
    }

    [Fact]
    public async Task SameCycleRefreshDoesNotDuplicateDeterministicOffers()
    {
        var store = new FakeStore();
        var service =
            new JobBoardGenerationService(store);
        JobMarketGenerationRequest request =
            Request(Epoch);

        JobBoardState first =
            await service.RefreshAsync(request);

        JobBoardState second =
            await service.RefreshAsync(request);

        Assert.Equal(
            first.Offers.Select(x => x.OfferId),
            second.Offers.Select(x => x.OfferId));
        Assert.Equal(
            first.Offers.Length,
            second.Offers.Length);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task PersistedNewerBoardWinsAfterSave()
    {
        JobBoardState newer =
            JobBoardState.Empty(
                "KRME",
                Epoch.AddHours(2));

        var store =
            new FakeStore
            {
                OnSave =
                    _ => newer
            };

        var service =
            new JobBoardGenerationService(store);

        JobBoardState result =
            await service.RefreshAsync(
                Request(Epoch));

        Assert.Same(newer, result);
    }

    [Fact]
    public async Task BackwardRefreshIsRejectedBeforeSaving()
    {
        var store =
            new FakeStore
            {
                State =
                    JobBoardState.Empty(
                        "KRME",
                        Epoch.AddHours(1))
            };

        var service =
            new JobBoardGenerationService(store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RefreshAsync(
                Request(Epoch)));

        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task StoreCannotSubstituteAnotherAirport()
    {
        var store =
            new FakeStore
            {
                State =
                    JobBoardState.Empty(
                        "KALB",
                        Epoch)
            };

        var service =
            new JobBoardGenerationService(store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RefreshAsync(
                Request(Epoch)));

        Assert.Equal(0, store.SaveCount);
    }

    private static JobMarketGenerationRequest Request(
        DateTimeOffset time) =>
        new(
            GenerationSeed: 123456789,
            Time: time,
            Origin:
                InitialAirportProfiles
                    .GriffissInternational,
            Destinations:
            [
                new(
                    "KSYR",
                    45,
                    RouteStrength: 0.8,
                    RelationshipStrength: 0.6,
                    EstimatedFlightHours: 0.5),
                new(
                    "KALB",
                    80,
                    RouteStrength: 0.45,
                    RelationshipStrength: 0.3,
                    EstimatedFlightHours: 0.8),
                new(
                    "KBOS",
                    220,
                    EstimatedFlightHours: 1.9)
            ],
            Access:
                JobMarketAccess.CivilianEmployment);

    private sealed class FakeStore : IJobBoardStateStore
    {
        public JobBoardState? State { get; set; }

        public int SaveCount { get; private set; }

        public Func<JobBoardState, JobBoardState>? OnSave { get; init; }

        public Task SaveAsync(
            JobBoardState state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            State =
                OnSave is null
                    ? state
                    : OnSave(state);
            return Task.CompletedTask;
        }

        public Task<JobBoardState?> GetAsync(
            string airportIcao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(State);
        }

        public Task<IReadOnlyList<JobBoardState>> LoadAllAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<JobBoardState> states =
                State is null
                    ? Array.Empty<JobBoardState>()
                    : [State];

            return Task.FromResult(states);
        }
    }
}
