using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Domain.Economy;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteMarketStateStoreTests : IAsyncLifetime
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
        MarketState expected = State(
            "KDFW:express-cargo",
            MarketSegment.ExpressCargo,
            Start);

        SqliteMarketStateStore first = CreateStore();
        await first.SaveAsync(expected);

        SqliteMarketStateStore second = CreateStore();
        MarketState? loaded = await second.GetAsync(expected.MarketId);

        Assert.Equal(expected, loaded);
        Assert.True(File.Exists(_databasePath));
    }

    [Fact]
    public async Task NewerSnapshotReplacesExistingMarketState()
    {
        MarketState original = State(
            "KDFW:general-cargo",
            MarketSegment.GeneralCargo,
            Start);
        MarketState updated = original with
        {
            CurrentUnitPrice = 187.4321m,
            CurrentDemandPerDay = 42.75,
            BacklogUnits = 11.25,
            UpdatedAt = Start.AddHours(1)
        };

        SqliteMarketStateStore store = CreateStore();

        await store.SaveAsync(original);
        await store.SaveAsync(updated);

        MarketState? loaded = await store.GetAsync(original.MarketId);

        Assert.Equal(updated, loaded);
    }

    [Fact]
    public async Task StaleSnapshotCannotRollBackNewerMarketState()
    {
        MarketState stale = State(
            "KDFW:medical",
            MarketSegment.MedicalLogistics,
            Start);
        MarketState newer = stale with
        {
            CurrentUnitPrice = 245.75m,
            BacklogUnits = 18.5,
            UpdatedAt = Start.AddHours(2)
        };

        SqliteMarketStateStore store = CreateStore();

        await store.SaveAsync(newer);
        await store.SaveAsync(stale);

        MarketState? loaded = await store.GetAsync(stale.MarketId);

        Assert.Equal(newer, loaded);
    }

    [Fact]
    public async Task LoadAllUsesStableMarketIdOrdering()
    {
        SqliteMarketStateStore store = CreateStore();

        await store.SaveAsync(
            State("KDAL:business-charter", MarketSegment.BusinessCharter, Start));
        await store.SaveAsync(
            State("KAFW:express-cargo", MarketSegment.ExpressCargo, Start));
        await store.SaveAsync(
            State("KDFW:general-passenger", MarketSegment.GeneralPassenger, Start));

        IReadOnlyList<MarketState> states = await store.LoadAllAsync();

        Assert.Equal(
            [
                "KAFW:express-cargo",
                "KDAL:business-charter",
                "KDFW:general-passenger"
            ],
            states.Select(static state => state.MarketId).ToArray());
    }

    private SqliteMarketStateStore CreateStore() =>
        new(
            new OpenCareerDatabaseOptions(_databasePath),
            NullLogger<SqliteMarketStateStore>.Instance);

    private static MarketState State(
        string marketId,
        MarketSegment segment,
        DateTimeOffset updatedAt) =>
        new(
            marketId,
            segment,
            ReferenceUnitCost: 100.1250m,
            ReferenceUnitPrice: 125.2500m,
            CurrentUnitPrice: 131.3750m,
            BaselineDemandPerDay: 30.5,
            CurrentDemandPerDay: 32.75,
            StructuralCapacityPerDay: 35.25,
            BacklogUnits: 4.5,
            SeasonalFactor: 1.05,
            RegionalEconomicFactor: 0.98,
            CapacityInvestmentSignal: 0.125,
            UpdatedAt: updatedAt);
}
