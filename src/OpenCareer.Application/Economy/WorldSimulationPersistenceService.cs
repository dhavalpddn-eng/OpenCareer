using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Events;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Application.Economy;

public sealed record WorldSimulationInitialization(
    MarketState Market,
    WorldEventLocation Location,
    ulong Seed,
    IReadOnlyList<WorldEventDefinition>? Definitions = null,
    MarketParameters? MarketParameters = null,
    EconomicCycleParameters? CycleParameters = null);

public sealed class WorldSimulationPersistenceService
{
    private readonly IWorldSimulationStateStore _store;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);

    public WorldSimulationState? Current { get; private set; }

    public WorldSimulationPersistenceService(
        IWorldSimulationStateStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<WorldSimulationState> LoadOrCreateAsync(
        WorldSimulationInitialization initialization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(initialization.Market);
        ArgumentNullException.ThrowIfNull(initialization.Location);

        string marketId = initialization.Market.MarketId;
        if (string.IsNullOrWhiteSpace(marketId))
            throw new ArgumentException("Market id is required.", nameof(initialization));

        await _initializationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            WorldSimulationState? existing = await _store
                .GetAsync(marketId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                EnsureMarketIdentity(existing, marketId);
                Current = existing;
                return existing;
            }

            WorldSimulationState created = WorldSimulation.Create(
                initialization.Market,
                initialization.Location,
                initialization.Seed,
                initialization.Definitions,
                initialization.MarketParameters,
                initialization.CycleParameters);

            await _store
                .SaveAsync(created, cancellationToken)
                .ConfigureAwait(false);

            WorldSimulationState authoritative = await _store
                .GetAsync(marketId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "World-simulation checkpoint was not readable after persistence.");

            EnsureMarketIdentity(authoritative, marketId);
            Current = authoritative;
            return authoritative;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static void EnsureMarketIdentity(
        WorldSimulationState state,
        string expectedMarketId)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!string.Equals(
                state.Market.MarketId,
                expectedMarketId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"World-simulation store returned market '{state.Market.MarketId}' while '{expectedMarketId}' was requested.");
        }
    }
}
