using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Domain.Economy;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteEconomicCycleStateStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private string _root = string.Empty;
    private string _databasePath = string.Empty;

    public Task InitializeAsync()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_root, "opencareer.db");
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
    public async Task SaveRoundTripsAcrossStoreInstances()
    {
        EconomicCycleState expected = State(
            "US-TX-DFW",
            demand: 0.125,
            operatingCost: -0.075,
            updatedAt: Start);

        SqliteEconomicCycleStateStore first = CreateStore();
        await first.SaveAsync(expected);

        SqliteEconomicCycleStateStore second = CreateStore();
        EconomicCycleState? loaded = await second.GetAsync(expected.RegionId);

        Assert.Equal(expected, loaded);
        Assert.True(File.Exists(_databasePath));
    }

    [Fact]
    public async Task NewerSnapshotReplacesExistingRegionState()
    {
        EconomicCycleState original = State(
            "US-TX",
            demand: 0.05,
            operatingCost: 0.02,
            updatedAt: Start);
        EconomicCycleState updated = original with
        {
            LogDemandFactor = 0.175,
            LogOperatingCostFactor = 0.095,
            UpdatedAt = Start.AddDays(1)
        };

        SqliteEconomicCycleStateStore store = CreateStore();

        await store.SaveAsync(original);
        await store.SaveAsync(updated);

        EconomicCycleState? loaded = await store.GetAsync(original.RegionId);

        Assert.Equal(updated, loaded);
    }

    [Fact]
    public async Task StaleSnapshotCannotRollBackNewerRegionState()
    {
        EconomicCycleState stale = State(
            "US-SW",
            demand: -0.03,
            operatingCost: 0.04,
            updatedAt: Start);
        EconomicCycleState newer = stale with
        {
            LogDemandFactor = 0.15,
            LogOperatingCostFactor = 0.11,
            UpdatedAt = Start.AddHours(6)
        };

        SqliteEconomicCycleStateStore store = CreateStore();

        await store.SaveAsync(newer);
        await store.SaveAsync(stale);

        EconomicCycleState? loaded = await store.GetAsync(stale.RegionId);

        Assert.Equal(newer, loaded);
    }

    [Fact]
    public async Task LoadAllUsesStableRegionIdOrdering()
    {
        SqliteEconomicCycleStateStore store = CreateStore();

        await store.SaveAsync(State("US-TX", 0.01, 0.02, Start));
        await store.SaveAsync(State("US-OK", 0.03, 0.04, Start));
        await store.SaveAsync(State("US-AR", 0.05, 0.06, Start));

        IReadOnlyList<EconomicCycleState> states = await store.LoadAllAsync();

        Assert.Equal(
            [
                "US-AR",
                "US-OK",
                "US-TX"
            ],
            states.Select(static state => state.RegionId).ToArray());
    }

    [Fact]
    public async Task UnknownRegionReturnsNull()
    {
        SqliteEconomicCycleStateStore store = CreateStore();

        EconomicCycleState? loaded = await store.GetAsync("US-NOT-FOUND");

        Assert.Null(loaded);
    }

    private SqliteEconomicCycleStateStore CreateStore() =>
        new(
            new OpenCareerDatabaseOptions(_databasePath),
            NullLogger<SqliteEconomicCycleStateStore>.Instance);

    private static EconomicCycleState State(
        string regionId,
        double demand,
        double operatingCost,
        DateTimeOffset updatedAt) =>
        new(
            regionId,
            demand,
            operatingCost,
            updatedAt);
}
