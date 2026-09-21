using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Events;

namespace OpenCareer.Domain.Simulation;

public sealed record ScheduledWorldEvent(
    string DefinitionId, string? Target, DateTimeOffset NextStartAt, ulong RandomState);

// Serializable checkpoint. RequestedThrough includes the unprocessed fraction of an hour.
// Market.UpdatedAt is the last committed hour. No wall clock or runtime RNG is consulted.
public sealed record WorldSimulationState(
    int SchemaVersion,
    MarketState Market,
    MarketParameters MarketParameters,
    EconomicCycleState Cycle,
    EconomicCycleParameters CycleParameters,
    ulong CycleRandomState,
    WorldEventLocation Location,
    WorldEventDefinition[] Definitions,
    ScheduledWorldEvent[] Schedule,
    WorldEventInstance[] Events,
    DateTimeOffset RequestedThrough);

public static class WorldSimulation
{
    private const long HourTicks = TimeSpan.TicksPerHour;

    public static WorldSimulationState Create(
        MarketState market, WorldEventLocation location, ulong seed,
        IEnumerable<WorldEventDefinition>? definitions = null,
        MarketParameters? marketParameters = null,
        EconomicCycleParameters? cycleParameters = null)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(location);
        location.Validate();
        if (market.UpdatedAt.UtcTicks % HourTicks != 0)
            throw new ArgumentException("The initial checkpoint must be aligned to a UTC hour.", nameof(market));
        var catalog = (definitions ?? WorldEventCatalog.Definitions)
            .OrderBy(d => d.EventId, StringComparer.Ordinal).ToArray();
        foreach (var definition in catalog) WorldEventEngine.ValidateDefinition(definition);
        if (catalog.Select(d => d.EventId).Distinct(StringComparer.Ordinal).Count() != catalog.Length)
            throw new ArgumentException("Event IDs must be unique.", nameof(definitions));
        var schedule = new List<ScheduledWorldEvent>();
        foreach (var definition in catalog.Where(d => d.AnnualOccurrenceRatePerEligibleScope > 0))
        foreach (var target in location.Targets(definition.Scope))
        {
            var random = DeterministicSeed.CreateStream(seed,
                $"event:{definition.EventId}:{definition.Scope}:{target ?? "global"}");
            var next = NextStart(market.UpdatedAt, definition, random);
            schedule.Add(new(definition.EventId, target, next, random.State));
        }
        var state = new WorldSimulationState(1, market, marketParameters ?? MarketParameterProfiles.For(market.Segment),
            new(location.RegionId, 0, 0, market.UpdatedAt), cycleParameters ?? new(),
            DeterministicSeed.Derive(seed, $"economy:region:{location.RegionId}"), location,
            catalog, schedule.ToArray(), Array.Empty<WorldEventInstance>(), market.UpdatedAt);
        Validate(state);
        return state;
    }

    // All production advancement goes through this clock. The low-level numerical
    // integrators are not an application clock and do not promise arbitrary-step equivalence.
    public static WorldSimulationState Advance(WorldSimulationState state, DateTimeOffset through)
    {
        ArgumentNullException.ThrowIfNull(state);
        Validate(state);
        if (through < state.RequestedThrough)
            throw new ArgumentOutOfRangeException(nameof(through), "Career time cannot move backwards.");
        var endTicks = through.UtcTicks / HourTicks * HourTicks;
        var definitions = state.Definitions.ToDictionary(d => d.EventId, StringComparer.Ordinal);
        var schedule = state.Schedule.ToArray();
        var active = state.Events.ToList();
        var market = state.Market;
        var cycle = state.Cycle;
        var cycleRandom = new DeterministicRandom(state.CycleRandomState);

        while (market.UpdatedAt.UtcTicks < endTicks)
        {
            var hourEnd = market.UpdatedAt.AddHours(1);
            // Use the previous committed cycle throughout this hour, then advance it.
            market = market with { RegionalEconomicFactor = cycle.DemandFactor };
            // Schedule the whole committed hour first. Events without market effects
            // do not introduce numerical substeps into unrelated economic calculations.
            active.RemoveAll(e => e.EndsAt <= market.UpdatedAt);
            for (var i = 0; i < schedule.Length; i++)
            {
                var entry = schedule[i];
                while (entry.NextStartAt < hourEnd)
                {
                    var definition = definitions[entry.DefinitionId];
                    var random = new DeterministicRandom(entry.RandomState);
                    var instance = WorldEventEngine.Start(definition, entry.NextStartAt, entry.Target, random);
                    active.Add(instance);
                    var next = NextStart(instance.EndsAt, definition, random);
                    entry = entry with { NextStartAt = next, RandomState = random.State };
                }
                schedule[i] = entry;
            }
            while (market.UpdatedAt < hourEnd)
            {
                var now = market.UpdatedAt;
                var nextBoundary = hourEnd;
                foreach (var instance in active)
                {
                    if (!state.Location.Matches(instance) || !WorldEventEngine.MatchesSegment(instance, market.Segment) || !AffectsMarket(instance.Effects)) continue;
                    if (instance.StartsAt > now && instance.StartsAt < nextBoundary) nextBoundary = instance.StartsAt;
                    if (instance.EndsAt > now && instance.EndsAt < nextBoundary) nextBoundary = instance.EndsAt;
                }
                var effects = WorldEventEngine.Aggregate(active, now, state.Location, market.Segment);
                var result = MarketTickEngine.Advance(market, state.MarketParameters, new(
                    ElapsedDays: (nextBoundary - now).TotalDays,
                    DemandMultiplier: effects.DemandMultiplier,
                    AvailableCapacityMultiplier: effects.CapacityMultiplier,
                    OperatingCostMultiplier: effects.OperatingCostMultiplier * cycle.OperatingCostFactor,
                    FinanceLiquidityMultiplier: effects.FinanceLiquidityMultiplier));
                // Preserve the exact event/hour boundary instead of accumulating double time rounding.
                market = result.State with { UpdatedAt = nextBoundary };
            }
            cycle = EconomicCycleEngine.Advance(cycle, state.CycleParameters, cycleRandom, 1.0 / 24.0)
                with { UpdatedAt = hourEnd };
        }
        active.RemoveAll(e => e.EndsAt <= market.UpdatedAt);
        return state with { Market = market, Cycle = cycle, CycleRandomState = cycleRandom.State,
            Schedule = schedule, Events = active.ToArray(), RequestedThrough = through };
    }

    // Dispatch restrictions must be current even while the economy has an uncommitted
    // fraction of an hour. Project scheduled events without consuming their saved streams.
    public static WorldEventEffects GetOperationalEffects(WorldSimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Validate(state);
        var active = state.Events.ToList();
        var definitions = state.Definitions.ToDictionary(d => d.EventId, StringComparer.Ordinal);
        foreach (var scheduled in state.Schedule)
        {
            var entry = scheduled;
            while (entry.NextStartAt <= state.RequestedThrough)
            {
                var definition = definitions[entry.DefinitionId];
                var random = new DeterministicRandom(entry.RandomState);
                var instance = WorldEventEngine.Start(definition, entry.NextStartAt, entry.Target, random);
                active.Add(instance);
                entry = entry with { NextStartAt = NextStart(instance.EndsAt, definition, random), RandomState = random.State };
            }
        }
        return WorldEventEngine.Aggregate(active, state.RequestedThrough, state.Location, state.Market.Segment);
    }

    private static bool AffectsMarket(WorldEventEffects effects) => effects.DemandMultiplier != 1
        || effects.CapacityMultiplier != 1 || effects.OperatingCostMultiplier != 1 || effects.FinanceLiquidityMultiplier != 1;

    private static DateTimeOffset NextStart(DateTimeOffset after, WorldEventDefinition definition, DeterministicRandom random)
    {
        var days = random.NextExponential(definition.AnnualOccurrenceRatePerEligibleScope / 365.2425);
        // A one-second minimum prevents zero-length renewal loops from floating-point rounding.
        return after.AddTicks(Math.Max(TimeSpan.TicksPerSecond, TimeSpan.FromDays(days).Ticks));
    }

    private static void Validate(WorldSimulationState state)
    {
        if (state.SchemaVersion != 1) throw new ArgumentException("Unsupported simulation checkpoint version.");
        ArgumentNullException.ThrowIfNull(state.Market);
        ArgumentNullException.ThrowIfNull(state.Cycle);
        ArgumentNullException.ThrowIfNull(state.Location);
        ArgumentNullException.ThrowIfNull(state.Definitions);
        ArgumentNullException.ThrowIfNull(state.Schedule);
        ArgumentNullException.ThrowIfNull(state.Events);
        state.Location.Validate();
        if (state.Market.UpdatedAt != state.Cycle.UpdatedAt
            || state.Market.UpdatedAt.UtcTicks % HourTicks != 0
            || state.RequestedThrough < state.Market.UpdatedAt
            || state.RequestedThrough - state.Market.UpdatedAt >= TimeSpan.FromHours(1)
            || state.Cycle.RegionId != state.Location.RegionId)
            throw new ArgumentException("Inconsistent simulation checkpoint clock or region.");
        MarketTickEngine.Validate(state.Market, state.MarketParameters, new());
        EconomicCycleEngine.Validate(state.Cycle, state.CycleParameters, 1);
        foreach (var definition in state.Definitions) WorldEventEngine.ValidateDefinition(definition);
        var definitions = state.Definitions.ToDictionary(d => d.EventId, StringComparer.Ordinal);
        if (state.Schedule.Select(e => (e.DefinitionId, e.Target)).Distinct().Count() != state.Schedule.Length)
            throw new ArgumentException("Duplicate event schedule.");
        var expectedEntries = state.Definitions.Where(d => d.AnnualOccurrenceRatePerEligibleScope > 0)
            .SelectMany(d => state.Location.Targets(d.Scope).Select(t => (d.EventId, t))).ToHashSet();
        if (!expectedEntries.SetEquals(state.Schedule.Select(e => (e.DefinitionId, e.Target))))
            throw new ArgumentException("Incomplete event schedule.");
        foreach (var entry in state.Schedule)
            if (!definitions.TryGetValue(entry.DefinitionId, out var definition)
                || definition.AnnualOccurrenceRatePerEligibleScope <= 0
                || !state.Location.Targets(definition.Scope).Contains(entry.Target, StringComparer.Ordinal)
                || entry.NextStartAt < state.Market.UpdatedAt)
                throw new ArgumentException("Invalid event schedule.");
        foreach (var instance in state.Events) WorldEventEngine.ValidateInstance(instance);
        if (state.Events.Select(e => e.InstanceId).Distinct().Count() != state.Events.Length)
            throw new ArgumentException("Duplicate event instance.");
    }
}
