using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Domain.Careers;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteJobBoardStateStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private string _root = string.Empty;
    private string _databasePath = string.Empty;

    public Task InitializeAsync()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));
        _databasePath =
            Path.Combine(_root, "opencareer.db");

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Test cleanup should not mask the actual assertion result.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task SaveRoundTripsOffersAndRetiredIdsAcrossStoreInstances()
    {
        JobMarketOfferDraft active =
            Offer(
                Guid.Parse("10000000-0000-0000-0000-000000000001"),
                "KRME",
                "KALB",
                Start,
                Start.AddHours(6));

        Guid retired =
            Guid.Parse("10000000-0000-0000-0000-000000000002");

        JobBoardState expected =
            JobBoardState.Empty("KRME", Start)
                .Reconcile(Start, 1, [active])
                .Retire(retired, Start);

        expected = expected with
        {
            RetiredOfferIds =
                expected.RetiredOfferIds.Add(retired)
        };

        SqliteJobBoardStateStore first =
            CreateStore();
        await first.SaveAsync(expected);

        SqliteJobBoardStateStore second =
            CreateStore();
        JobBoardState? loaded =
            await second.GetAsync("krme");

        Assert.NotNull(loaded);
        Assert.Equal(expected.AirportIcao, loaded.AirportIcao);
        Assert.Equal(expected.UpdatedAt, loaded.UpdatedAt);
        Assert.Equal(
            expected.Offers.ToArray(),
            loaded.Offers.ToArray());
        Assert.Equal(
            expected.RetiredOfferIds.OrderBy(x => x),
            loaded.RetiredOfferIds.OrderBy(x => x));
    }

    [Fact]
    public async Task NewerBoardReplacesExistingBoard()
    {
        JobMarketOfferDraft first =
            Offer(
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                "KRME",
                "KALB",
                Start,
                Start.AddHours(6));

        JobMarketOfferDraft replacement =
            Offer(
                Guid.Parse("20000000-0000-0000-0000-000000000002"),
                "KRME",
                "KBOS",
                Start.AddHours(1),
                Start.AddHours(7));

        JobBoardState original =
            JobBoardState.Empty("KRME", Start)
                .Reconcile(Start, 1, [first]);

        JobBoardState newer =
            original.Reconcile(
                Start.AddHours(1),
                1,
                [replacement],
                forceReplace: true);

        SqliteJobBoardStateStore store =
            CreateStore();

        await store.SaveAsync(original);
        await store.SaveAsync(newer);

        JobBoardState? loaded =
            await store.GetAsync("KRME");

        Assert.NotNull(loaded);
        Assert.Equal(newer.UpdatedAt, loaded.UpdatedAt);
        Assert.Equal(
            newer.Offers.ToArray(),
            loaded.Offers.ToArray());
        Assert.Contains(first.OfferId, loaded.RetiredOfferIds);
    }

    [Fact]
    public async Task StaleBoardCannotRollBackRetiredOfferHistory()
    {
        JobMarketOfferDraft offer =
            Offer(
                Guid.Parse("30000000-0000-0000-0000-000000000001"),
                "KRME",
                "KALB",
                Start,
                Start.AddHours(6));

        JobBoardState stale =
            JobBoardState.Empty("KRME", Start)
                .Reconcile(Start, 1, [offer]);

        JobBoardState newer =
            stale.Retire(
                offer.OfferId,
                Start.AddHours(1));

        SqliteJobBoardStateStore store =
            CreateStore();

        await store.SaveAsync(newer);
        await store.SaveAsync(stale);

        JobBoardState? loaded =
            await store.GetAsync("KRME");

        Assert.NotNull(loaded);
        Assert.Empty(loaded.Offers);
        Assert.Contains(
            offer.OfferId,
            loaded.RetiredOfferIds);
        Assert.Equal(
            newer.UpdatedAt,
            loaded.UpdatedAt);
    }

    [Fact]
    public async Task LoadAllUsesStableAirportOrdering()
    {
        SqliteJobBoardStateStore store =
            CreateStore();

        await store.SaveAsync(
            JobBoardState.Empty("KDFW", Start));
        await store.SaveAsync(
            JobBoardState.Empty("KAFW", Start));
        await store.SaveAsync(
            JobBoardState.Empty("KDAL", Start));

        IReadOnlyList<JobBoardState> states =
            await store.LoadAllAsync();

        Assert.Equal(
            ["KAFW", "KDAL", "KDFW"],
            states
                .Select(x => x.AirportIcao)
                .ToArray());
    }

    [Fact]
    public async Task InvalidBoardIsRejectedBeforePersistence()
    {
        JobBoardState invalid =
            JobBoardState.Empty("KRME", Start) with
            {
                AirportIcao = "krme"
            };

        SqliteJobBoardStateStore store =
            CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAsync(invalid));
    }

    private SqliteJobBoardStateStore CreateStore() =>
        new(
            new OpenCareerDatabaseOptions(_databasePath),
            NullLogger<SqliteJobBoardStateStore>.Instance);

    private static JobMarketOfferDraft Offer(
        Guid id,
        string origin,
        string destination,
        DateTimeOffset offeredAt,
        DateTimeOffset expiresAt) =>
        new(
            id,
            ServiceTrack.CivilianEmployment,
            ContractKind.Cargo,
            JobScenarioKind.Standard,
            origin,
            destination,
            DistanceNm: 100,
            EstimatedFlightHours: 1.2,
            offeredAt,
            expiresAt,
            IsLockedPreview: false,
            RouteStrength: 0.25,
            RelationshipStrength: 0.10,
            MarketSelectionWeight: 1.0);
}
