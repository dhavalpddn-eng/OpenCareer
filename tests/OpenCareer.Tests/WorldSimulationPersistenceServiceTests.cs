using OpenCareer.Application.Economy;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Events;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Tests;

public sealed class WorldSimulationPersistenceServiceTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExistingCheckpointIsLoadedWithoutBeingReplaced()
    {
        WorldSimulationState existing = CreateState(seed: 111);
        var store = new MemoryStore(existing);
        var service = new WorldSimulationPersistenceService(store);

        WorldSimulationState loaded = await service.LoadOrCreateAsync(
            Initialization(seed: 999));

        Assert.Same(existing, loaded);
        Assert.Same(existing, service.Current);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(1, store.GetCount);
    }

    [Fact]
    public async Task MissingCheckpointIsPersistedBeforeBeingPublished()
    {
        var store = new MemoryStore();
        var service = new WorldSimulationPersistenceService(store);

        WorldSimulationState loaded = await service.LoadOrCreateAsync(
            Initialization(seed: 222));

        Assert.NotNull(store.Checkpoint);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(2, store.GetCount);
        Assert.Same(store.Checkpoint, loaded);
        Assert.Same(store.Checkpoint, service.Current);
    }

    [Fact]
    public async Task RepeatedInitializationKeepsFirstPersistedSeedAuthoritative()
    {
        var store = new MemoryStore();
        var service = new WorldSimulationPersistenceService(store);

        WorldSimulationState first = await service.LoadOrCreateAsync(
            Initialization(seed: 333));
        WorldSimulationState second = await service.LoadOrCreateAsync(
            Initialization(seed: 444));

        Assert.Equal(first.CycleRandomState, second.CycleRandomState);
        Assert.Equal(first.Schedule, second.Schedule);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(3, store.GetCount);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task FailedInitialPersistenceDoesNotPublishFreshState()
    {
        var store = new MemoryStore
        {
            FailWrites = true
        };
        var service = new WorldSimulationPersistenceService(store);

        await Assert.ThrowsAsync<IOException>(
            () => service.LoadOrCreateAsync(
                Initialization(seed: 555)));

        Assert.Null(service.Current);
        Assert.Null(store.Checkpoint);
    }

    [Fact]
    public async Task ConcurrentInitializationUsesSinglePersistedAuthority()
    {
        var store = new MemoryStore();
        var service = new WorldSimulationPersistenceService(store);

        Task<WorldSimulationState> first =
            service.LoadOrCreateAsync(Initialization(seed: 666));
        Task<WorldSimulationState> second =
            service.LoadOrCreateAsync(Initialization(seed: 777));

        WorldSimulationState[] states =
            await Task.WhenAll(first, second);

        Assert.Equal(1, store.SaveCount);
        Assert.Same(states[0], states[1]);
        Assert.Same(store.Checkpoint, states[0]);
    }

    private static WorldSimulationInitialization Initialization(
        ulong seed) =>
        new(
            Market(),
            Location(),
            seed,
            [Definition()]);

    private static WorldSimulationState CreateState(
        ulong seed) =>
        WorldSimulation.Create(
            Market(),
            Location(),
            seed,
            [Definition()]);

    private static MarketState Market() =>
        new(
            "KDFW:general-cargo",
            MarketSegment.GeneralCargo,
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

    private static WorldEventLocation Location() =>
        new(
            RegionId: "US-TX",
            NationId: "US",
            OriginAirport: "KDFW",
            DestinationAirport: "KDAL");

    private static WorldEventDefinition Definition() =>
        new(
            EventId: "test-regional-surge",
            Name: "Test Regional Surge",
            Tier: WorldEventTier.LocalOperational,
            Scope: WorldEventScope.Region,
            AnnualOccurrenceRatePerEligibleScope: 1,
            MinimumDurationDays: 1,
            MaximumDurationDays: 1,
            Effects: new(
                DemandMultiplier: 1.1,
                MissionOpportunities: MissionOpportunity.Cargo),
            AffectedMarketSegments: [MarketSegment.GeneralCargo]);

    private sealed class MemoryStore :
        IWorldSimulationStateStore
    {
        public MemoryStore(
            WorldSimulationState? checkpoint = null)
        {
            Checkpoint = checkpoint;
        }

        public WorldSimulationState? Checkpoint { get; private set; }

        public bool FailWrites { get; set; }

        public int GetCount { get; private set; }

        public int SaveCount { get; private set; }

        public Task SaveAsync(
            WorldSimulationState state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (FailWrites)
                throw new IOException("Synthetic persistence failure.");

            SaveCount++;
            Checkpoint = state;
            return Task.CompletedTask;
        }

        public Task<WorldSimulationState?> GetAsync(
            string marketId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetCount++;

            WorldSimulationState? result =
                Checkpoint is not null &&
                string.Equals(
                    Checkpoint.Market.MarketId,
                    marketId,
                    StringComparison.Ordinal)
                    ? Checkpoint
                    : null;

            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<WorldSimulationState>> LoadAllAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<WorldSimulationState> result =
                Checkpoint is null
                    ? Array.Empty<WorldSimulationState>()
                    : [Checkpoint];

            return Task.FromResult(result);
        }
    }
}
