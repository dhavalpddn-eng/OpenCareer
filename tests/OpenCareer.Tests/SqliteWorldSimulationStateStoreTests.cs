using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Events;
using OpenCareer.Domain.Simulation;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteWorldSimulationStateStoreTests : IAsyncLifetime
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
    public async Task SaveRoundTripsDeterministicCheckpointAcrossStoreInstances()
    {
        WorldSimulationState expected = WorldSimulation.Advance(
            CreateState("KDFW:general-cargo", "US-TX", 1234),
            Start.AddMinutes(45));

        SqliteWorldSimulationStateStore first = CreateStore();
        await first.SaveAsync(expected);

        SqliteWorldSimulationStateStore second = CreateStore();
        WorldSimulationState? loaded = await second.GetAsync(expected.Market.MarketId);

        Assert.NotNull(loaded);
        Assert.Equal(expected.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(expected.Market, loaded.Market);
        Assert.Equal(expected.Cycle, loaded.Cycle);
        Assert.Equal(expected.CycleRandomState, loaded.CycleRandomState);
        Assert.Equal(expected.Location, loaded.Location);
        Assert.Equal(expected.RequestedThrough, loaded.RequestedThrough);
        Assert.Equal(expected.Schedule.Length, loaded.Schedule.Length);
        Assert.Equal(expected.Schedule[0], loaded.Schedule[0]);
        Assert.Equal(expected.Definitions.Length, loaded.Definitions.Length);
        Assert.True(File.Exists(_databasePath));
    }

    [Fact]
    public async Task ReloadedCheckpointContinuesDeterministically()
    {
        WorldSimulationState expected = WorldSimulation.Advance(
            CreateState("KDFW:general-cargo", "US-TX", 777),
            Start.AddMinutes(30));

        SqliteWorldSimulationStateStore store = CreateStore();
        await store.SaveAsync(expected);

        WorldSimulationState loaded = Assert.IsType<WorldSimulationState>(
            await CreateStore().GetAsync(expected.Market.MarketId));

        DateTimeOffset through = Start.AddHours(3).AddMinutes(20);
        WorldSimulationState expectedContinuation =
            WorldSimulation.Advance(expected, through);
        WorldSimulationState loadedContinuation =
            WorldSimulation.Advance(loaded, through);

        Assert.Equal(expectedContinuation.Market, loadedContinuation.Market);
        Assert.Equal(expectedContinuation.Cycle, loadedContinuation.Cycle);
        Assert.Equal(
            expectedContinuation.CycleRandomState,
            loadedContinuation.CycleRandomState);
        Assert.Equal(
            expectedContinuation.RequestedThrough,
            loadedContinuation.RequestedThrough);
        Assert.Equal(
            expectedContinuation.Schedule,
            loadedContinuation.Schedule);
        Assert.Equal(
            expectedContinuation.Events.Length,
            loadedContinuation.Events.Length);

        for (int i = 0; i < expectedContinuation.Events.Length; i++)
        {
            AssertEventEquivalent(
                expectedContinuation.Events[i],
                loadedContinuation.Events[i]);
        }
    }

    [Fact]
    public async Task FractionallyNewerCheckpointCannotBeRolledBack()
    {
        WorldSimulationState original =
            CreateState("KDFW:express-cargo", "US-TX", 99);
        WorldSimulationState stale =
            WorldSimulation.Advance(original, Start.AddMinutes(20));
        WorldSimulationState newer =
            WorldSimulation.Advance(stale, Start.AddMinutes(50));

        SqliteWorldSimulationStateStore store = CreateStore();

        await store.SaveAsync(newer);
        await store.SaveAsync(stale);

        WorldSimulationState? loaded =
            await store.GetAsync(original.Market.MarketId);

        Assert.NotNull(loaded);
        Assert.Equal(newer.RequestedThrough, loaded.RequestedThrough);
    }

    [Fact]
    public async Task LoadAllUsesStableMarketIdOrdering()
    {
        SqliteWorldSimulationStateStore store = CreateStore();

        await store.SaveAsync(CreateState("KDAL:business-charter", "US-TX", 1));
        await store.SaveAsync(CreateState("KAFW:express-cargo", "US-TX", 2));
        await store.SaveAsync(CreateState("KDFW:general-passenger", "US-TX", 3));

        IReadOnlyList<WorldSimulationState> states =
            await store.LoadAllAsync();

        Assert.Equal(
            [
                "KAFW:express-cargo",
                "KDAL:business-charter",
                "KDFW:general-passenger"
            ],
            states.Select(static state => state.Market.MarketId).ToArray());
    }

    [Fact]
    public async Task InvalidCheckpointIsRejectedBeforePersistence()
    {
        WorldSimulationState invalid =
            CreateState("KDFW:general-cargo", "US-TX", 5) with
            {
                RequestedThrough = Start.AddHours(2)
            };

        SqliteWorldSimulationStateStore store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAsync(invalid));
    }

    private static void AssertEventEquivalent(
        WorldEventInstance expected,
        WorldEventInstance actual)
    {
        Assert.Equal(expected.InstanceId, actual.InstanceId);
        Assert.Equal(expected.DefinitionId, actual.DefinitionId);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Tier, actual.Tier);
        Assert.Equal(expected.Scope, actual.Scope);
        Assert.Equal(expected.ScopeTarget, actual.ScopeTarget);
        Assert.Equal(expected.StartsAt, actual.StartsAt);
        Assert.Equal(expected.EndsAt, actual.EndsAt);
        Assert.Equal(expected.Effects, actual.Effects);
        Assert.Equal(
            expected.AffectedMarketSegments,
            actual.AffectedMarketSegments);
    }

    private SqliteWorldSimulationStateStore CreateStore() =>
        new(
            new OpenCareerDatabaseOptions(_databasePath),
            NullLogger<SqliteWorldSimulationStateStore>.Instance);

    private static WorldSimulationState CreateState(
        string marketId,
        string regionId,
        ulong seed)
    {
        MarketSegment segment = marketId.Contains(
            "passenger",
            StringComparison.Ordinal)
            ? MarketSegment.GeneralPassenger
            : marketId.Contains(
                "business-charter",
                StringComparison.Ordinal)
                ? MarketSegment.BusinessCharter
                : marketId.Contains(
                    "express-cargo",
                    StringComparison.Ordinal)
                    ? MarketSegment.ExpressCargo
                    : MarketSegment.GeneralCargo;

        var market = new MarketState(
            marketId,
            segment,
            ReferenceUnitCost: 100m,
            ReferenceUnitPrice: 125m,
            CurrentUnitPrice: 125m,
            BaselineDemandPerDay: 30,
            CurrentDemandPerDay: 30,
            StructuralCapacityPerDay: 35,
            BacklogUnits: 0,
            SeasonalFactor: 1,
            RegionalEconomicFactor: 1,
            CapacityInvestmentSignal: 0,
            UpdatedAt: Start);

        var location = new WorldEventLocation(
            RegionId: regionId,
            NationId: "US",
            OriginAirport: "KDFW",
            DestinationAirport: "KDAL");

        WorldEventDefinition definition = new(
            EventId: "test-regional-surge",
            Name: "Test Regional Surge",
            Tier: WorldEventTier.LocalOperational,
            Scope: WorldEventScope.Region,
            AnnualOccurrenceRatePerEligibleScope: 8765.82,
            MinimumDurationDays: 0.25,
            MaximumDurationDays: 0.25,
            Effects: new(
                DemandMultiplier: 1.10,
                MissionOpportunities: MissionOpportunity.Cargo),
            AffectedMarketSegments:
            [
                MarketSegment.GeneralCargo,
                MarketSegment.ExpressCargo,
                MarketSegment.GeneralPassenger,
                MarketSegment.BusinessCharter
            ]);

        return WorldSimulation.Create(
            market,
            location,
            seed,
            [definition]);
    }
}
