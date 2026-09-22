using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Domain.Economy;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteCommodityMarketSnapshotStoreTests :
    IAsyncLifetime
{
    private static readonly DateTimeOffset Start =
        new(
            2026,
            9,
            21,
            18,
            0,
            0,
            TimeSpan.Zero);

    private string _root = string.Empty;
    private string _databasePath = string.Empty;

    public Task InitializeAsync()
    {
        _root =
            Path.Combine(
                Path.GetTempPath(),
                "OpenCareer.Tests",
                Guid.NewGuid().ToString("N"));

        _databasePath =
            Path.Combine(
                _root,
                "opencareer.db");

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(
                    _root,
                    recursive:
                        true);
            }
        }
        catch
        {
            // Test cleanup should not mask the actual assertion result.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task SaveRoundTripsAcrossStoreInstances()
    {
        CommodityMarketSnapshot expected =
            Snapshot(
                CommodityMarketScope.Airport,
                "KDFW",
                Start,
                price:
                    8.88m);

        SqliteCommodityMarketSnapshotStore first =
            CreateStore();

        await first.SaveAsync(expected);

        SqliteCommodityMarketSnapshotStore second =
            CreateStore();

        CommodityMarketSnapshot? loaded =
            await second.GetAsync(
                CommodityMarketScope.Airport,
                "KDFW");

        Assert.NotNull(loaded);
        Assert.Equal(
            expected.Scope,
            loaded.Scope);
        Assert.Equal(
            expected.LocationId,
            loaded.LocationId);
        Assert.Equal(
            expected.CapturedAt,
            loaded.CapturedAt);
        Assert.Equal(
            expected.Commodities,
            loaded.Commodities);
        Assert.True(
            File.Exists(_databasePath));
    }

    [Fact]
    public async Task NewerSnapshotReplacesExistingLocationState()
    {
        CommodityMarketSnapshot original =
            Snapshot(
                CommodityMarketScope.Region,
                "north-america",
                Start,
                price:
                    7.40m);

        CommodityMarketSnapshot updated =
            Snapshot(
                CommodityMarketScope.Region,
                "north-america",
                Start.AddHours(1),
                price:
                    8.05m);

        SqliteCommodityMarketSnapshotStore store =
            CreateStore();

        await store.SaveAsync(original);
        await store.SaveAsync(updated);

        CommodityMarketSnapshot? loaded =
            await store.GetAsync(
                CommodityMarketScope.Region,
                "north-america");

        Assert.NotNull(loaded);
        Assert.Equal(
            updated.CapturedAt,
            loaded.CapturedAt);
        Assert.Equal(
            8.05m,
            loaded
                .GetRequired(
                    "coffee.roasted")
                .UnitPrice);
    }

    [Fact]
    public async Task StaleSnapshotCannotRollBackNewerLocationState()
    {
        CommodityMarketSnapshot stale =
            Snapshot(
                CommodityMarketScope.Airport,
                "KDFW",
                Start,
                price:
                    7.40m);

        CommodityMarketSnapshot newer =
            Snapshot(
                CommodityMarketScope.Airport,
                "KDFW",
                Start.AddHours(2),
                price:
                    9.10m);

        SqliteCommodityMarketSnapshotStore store =
            CreateStore();

        await store.SaveAsync(newer);
        await store.SaveAsync(stale);

        CommodityMarketSnapshot? loaded =
            await store.GetAsync(
                CommodityMarketScope.Airport,
                "KDFW");

        Assert.NotNull(loaded);
        Assert.Equal(
            newer.CapturedAt,
            loaded.CapturedAt);
        Assert.Equal(
            9.10m,
            loaded
                .GetRequired(
                    "coffee.roasted")
                .UnitPrice);
    }

    [Fact]
    public async Task LoadAllUsesStableScopeAndLocationOrdering()
    {
        SqliteCommodityMarketSnapshotStore store =
            CreateStore();

        await store.SaveAsync(
            Snapshot(
                CommodityMarketScope.Region,
                "southwest",
                Start,
                7.40m));

        await store.SaveAsync(
            Snapshot(
                CommodityMarketScope.Airport,
                "KDFW",
                Start,
                7.40m));

        await store.SaveAsync(
            Snapshot(
                CommodityMarketScope.Airport,
                "KAFW",
                Start,
                7.40m));

        IReadOnlyList<CommodityMarketSnapshot> snapshots =
            await store.LoadAllAsync();

        Assert.Equal(
            [
                "Airport:KAFW",
                "Airport:KDFW",
                "Region:southwest"
            ],
            snapshots
                .Select(
                    snapshot =>
                        $"{snapshot.Scope}:{snapshot.LocationId}")
                .ToArray());
    }

    [Fact]
    public async Task AirportAndRegionWithSameLocationIdAreIndependent()
    {
        SqliteCommodityMarketSnapshotStore store =
            CreateStore();

        await store.SaveAsync(
            Snapshot(
                CommodityMarketScope.Airport,
                "TEST",
                Start,
                7.40m));

        await store.SaveAsync(
            Snapshot(
                CommodityMarketScope.Region,
                "TEST",
                Start,
                8.40m));

        CommodityMarketSnapshot? airport =
            await store.GetAsync(
                CommodityMarketScope.Airport,
                "TEST");

        CommodityMarketSnapshot? region =
            await store.GetAsync(
                CommodityMarketScope.Region,
                "TEST");

        Assert.Equal(
            7.40m,
            airport!
                .GetRequired(
                    "coffee.roasted")
                .UnitPrice);

        Assert.Equal(
            8.40m,
            region!
                .GetRequired(
                    "coffee.roasted")
                .UnitPrice);
    }

    [Fact]
    public async Task UnknownLocationReturnsNull()
    {
        SqliteCommodityMarketSnapshotStore store =
            CreateStore();

        CommodityMarketSnapshot? loaded =
            await store.GetAsync(
                CommodityMarketScope.Airport,
                "KDFW");

        Assert.Null(loaded);
    }

    private SqliteCommodityMarketSnapshotStore CreateStore() =>
        new(
            new OpenCareerDatabaseOptions(
                _databasePath),
            NullLogger<SqliteCommodityMarketSnapshotStore>.Instance);

    private static CommodityMarketSnapshot Snapshot(
        CommodityMarketScope scope,
        string locationId,
        DateTimeOffset capturedAt,
        decimal price) =>
        CommodityMarketSnapshot.Create(
            scope,
            locationId,
            capturedAt,
            [
                new CommodityMarketEntry(
                    CommodityId:
                        "coffee.roasted",
                    SupplyPressure:
                        1.0,
                    DemandPressure:
                        1.2,
                    UnitPrice:
                        price,
                    Trend:
                        0.1,
                    Balance:
                        CommodityMarketBalance.Scarce,
                    ActiveEventModifierIds:
                        ["event.test"],
                    AvailabilityConfidence:
                        0.9)
            ]);
}
