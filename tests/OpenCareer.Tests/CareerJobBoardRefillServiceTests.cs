using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Careers;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class CareerJobBoardRefillServiceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FreshBoardGeneratesPersistsAndRoundTripsPlayableOffer()
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "OpenCareer.Tests",
                Guid.NewGuid().ToString("N"));

        try
        {
            string databasePath =
                Path.Combine(
                    root,
                    "opencareer.db");

            var options =
                new OpenCareerDatabaseOptions(
                    databasePath);

            var firstStore =
                new SqliteJobBoardStateStore(
                    options,
                    NullLogger<SqliteJobBoardStateStore>.Instance);

            var service =
                Service(
                    firstStore,
                    Airports(),
                    new FixedTimeProvider(
                        Epoch));

            PlayerCareerProfile profile =
                Profile("KRME");

            JobBoardState board =
                await service.RefillAsync(
                    profile);

            board.Validate();
            Assert.Equal(
                "KRME",
                board.AirportIcao);
            Assert.NotEmpty(
                board.Offers);

            Assert.All(
                board.Offers,
                offer =>
                {
                    Assert.Equal(
                        "KRME",
                        offer.OriginIcao);
                    Assert.Equal(
                        ServiceTrack.CivilianEmployment,
                        offer.ServiceTrack);
                    Assert.Contains(
                        offer.Kind,
                        new[]
                        {
                            ContractKind.Ferry,
                            ContractKind.Reposition
                        });
                    Assert.Equal(
                        JobScenarioKind.Standard,
                        offer.Scenario);

                    JobMarketContractTermsEnvelope terms =
                        Assert.IsType<JobMarketContractTermsEnvelope>(
                            offer.ContractTerms);

                    Assert.Equal(
                        PersistedJobContractTermsSource.AuthorityId,
                        terms.AuthorityId);
                    Assert.Equal(
                        PilotQualificationState.Entry,
                        terms.RequiredPilotQualifications);
                    Assert.Equal(
                        OpenCareer.Domain.Aircraft.AircraftAccess.Civilian,
                        terms.AuthorizedAircraftAccess);
                });

            var reopened =
                new SqliteJobBoardStateStore(
                    options,
                    NullLogger<SqliteJobBoardStateStore>.Instance);

            JobBoardState loaded =
                Assert.IsType<JobBoardState>(
                    await reopened.GetAsync(
                        "KRME"));

            Assert.Equal(
                board.UpdatedAt,
                loaded.UpdatedAt);
            Assert.Equal(
                board.Offers.ToArray(),
                loaded.Offers.ToArray());
            Assert.Equal(
                board.RetiredOfferIds,
                loaded.RetiredOfferIds);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RepeatedSameCycleRefillIsDeterministicAndDoesNotDuplicate()
    {
        var store =
            new FakeBoardStore();

        var service =
            Service(
                store,
                Airports(),
                new FixedTimeProvider(
                    Epoch));

        PlayerCareerProfile profile =
            Profile("KRME");

        JobBoardState first =
            await service.RefillAsync(
                profile);

        JobBoardState second =
            await service.RefillAsync(
                profile);

        Assert.Equal(
            first.Offers.Select(
                static offer =>
                    offer.OfferId),
            second.Offers.Select(
                static offer =>
                    offer.OfferId));

        Assert.Equal(
            second.Offers.Length,
            second.Offers
                .Select(
                    static offer =>
                        offer.OfferId)
                .Distinct()
                .Count());

        Assert.Equal(
            2,
            store.SaveCount);
    }

    [Fact]
    public async Task CurrentCareerLocationOwnsGeneratedOfferOrigin()
    {
        var store =
            new FakeBoardStore();

        var service =
            Service(
                store,
                Airports(),
                new FixedTimeProvider(
                    Epoch));

        JobBoardState board =
            await service.RefillAsync(
                Profile("KSYR"));

        Assert.NotEmpty(
            board.Offers);

        Assert.All(
            board.Offers,
            static offer =>
                Assert.Equal(
                    "KSYR",
                    offer.OriginIcao));
    }

    [Fact]
    public async Task MissingSuitableDestinationPersistsValidEmptyBoard()
    {
        var store =
            new FakeBoardStore();

        var airports =
            new FakeAirportSource(
                Airport(
                    "KRME",
                    43.2338,
                    -75.4069));

        var service =
            Service(
                store,
                airports,
                new FixedTimeProvider(
                    Epoch));

        JobBoardState board =
            await service.RefillAsync(
                Profile("KRME"));

        board.Validate();
        Assert.Empty(
            board.Offers);
        Assert.Equal(
            "KRME",
            board.AirportIcao);
        Assert.Equal(
            1,
            store.SaveCount);
    }

    private static CareerJobBoardRefillService Service(
        IJobBoardStateStore store,
        IAirportDataSource airports,
        TimeProvider timeProvider) =>
        new(
            new JobBoardGenerationService(
                store),
            store,
            airports,
            timeProvider);

    private static PlayerCareerProfile Profile(
        string airportIcao) =>
        PlayerCareerProfile.Start(
            Guid.Parse(
                "b1000000-0000-0000-0000-000000000001"),
            airportIcao,
            Epoch.AddDays(-30));

    private static FakeAirportSource Airports() =>
        new(
            Airport(
                "KRME",
                43.2338,
                -75.4069),
            Airport(
                "KSYR",
                43.1112,
                -76.1063),
            Airport(
                "KALB",
                42.7483,
                -73.8017));

    private static AirportRecord Airport(
        string icao,
        double latitude,
        double longitude) =>
        new(
            icao,
            $"{icao} Fixture",
            [
                new RunwayRecord(
                    "01/19",
                    UsableLengthFeet:
                        6_000,
                    WidthFeet:
                        100,
                    RunwaySurface.Asphalt)
            ],
            latitude,
            longitude);

    private sealed class FakeAirportSource(
        params AirportRecord[] airports)
        : IAirportDataSource
    {
        private readonly IReadOnlyDictionary<string, AirportRecord> _airports =
            airports.ToDictionary(
                static airport =>
                    airport.Icao,
                StringComparer.OrdinalIgnoreCase);

        public Task<AirportRecord?> FindAirportAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _airports.TryGetValue(
                icao,
                out AirportRecord? airport);

            return Task.FromResult(
                airport);
        }
    }

    private sealed class FakeBoardStore
        : IJobBoardStateStore
    {
        private readonly Dictionary<string, JobBoardState> _boards =
            new(
                StringComparer.OrdinalIgnoreCase);

        public int SaveCount { get; private set; }

        public Task SaveAsync(
            JobBoardState state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Validate();

            SaveCount++;
            _boards[state.AirportIcao] =
                state;

            return Task.CompletedTask;
        }

        public Task<JobBoardState?> GetAsync(
            string airportIcao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _boards.TryGetValue(
                airportIcao,
                out JobBoardState? state);

            return Task.FromResult(
                state);
        }

        public Task<IReadOnlyList<JobBoardState>> LoadAllAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<IReadOnlyList<JobBoardState>>(
                _boards.Values.ToArray());
        }
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            now;
    }
}
