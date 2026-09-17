using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Events;
using OpenCareer.Domain.Simulation;

const ulong careerSeed = 0x4F50454E43415245UL;
var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

var market = new MarketState(
    MarketId: "KAFW-KAMA:general-cargo",
    Segment: MarketSegment.GeneralCargo,
    ReferenceUnitCost: 1.00m,
    ReferenceUnitPrice: 1.25m,
    CurrentUnitPrice: 1.25m,
    BaselineDemandPerDay: 120,
    CurrentDemandPerDay: 120,
    StructuralCapacityPerDay: 120,
    BacklogUnits: 0,
    SeasonalFactor: 1.0,
    RegionalEconomicFactor: 1.0,
    CapacityInvestmentSignal: 0,
    UpdatedAt: start);

var marketParameters = MarketParameterProfiles.For(market.Segment);
var cycleParameters = new EconomicCycleParameters();
var cycle = new EconomicCycleState("DFW", 0, 0, start);
var cycleRandom = DeterministicSeed.CreateStream(careerSeed, "economy:region:DFW");

var eventRandom = WorldEventCatalog.Definitions.ToDictionary(
    definition => definition.EventId,
    definition => DeterministicSeed.CreateStream(careerSeed, $"event:{definition.EventId}:DFW"));

var activeEvents = new List<WorldEventInstance>();
var now = start;
MarketTickResult? last = null;

for (var day = 0; day < 10 * 365; day++)
{
    cycle = EconomicCycleEngine.Advance(
        cycle,
        cycleParameters,
        cycleRandom,
        elapsedDays: 1.0);

    activeEvents.RemoveAll(worldEvent => worldEvent.EndsAt <= now);

    foreach (var definition in WorldEventCatalog.Definitions)
    {
        if (activeEvents.Any(worldEvent => worldEvent.DefinitionId == definition.EventId))
        {
            continue;
        }

        var random = eventRandom[definition.EventId];
        if (WorldEventEngine.ShouldStart(definition, 1.0, random))
        {
            activeEvents.Add(WorldEventEngine.Start(definition, now, "DFW", random));
        }
    }

    var effects = WorldEventEngine.Aggregate(activeEvents, now);
    market = market with { RegionalEconomicFactor = cycle.DemandFactor };

    last = MarketTickEngine.Advance(
        market,
        marketParameters,
        new MarketTickContext(
            ElapsedDays: 1.0,
            DemandMultiplier: effects.DemandMultiplier,
            AvailableCapacityMultiplier: effects.CapacityMultiplier,
            OperatingCostMultiplier: effects.OperatingCostMultiplier * cycle.OperatingCostFactor));

    market = last.State;
    now = now.AddDays(1);
}

Console.WriteLine("OpenCareer deterministic 10-year simulation");
Console.WriteLine($"Market: {market.MarketId}");
Console.WriteLine($"Price: {market.CurrentUnitPrice:F4}");
Console.WriteLine($"Structural capacity/day: {market.StructuralCapacityPerDay:F2}");
Console.WriteLine($"Backlog: {market.BacklogUnits:F2}");
Console.WriteLine($"Regional demand factor: {cycle.DemandFactor:F3}");
Console.WriteLine($"Regional cost factor: {cycle.OperatingCostFactor:F3}");
Console.WriteLine($"Final utilization: {last?.AverageUtilization:P1}");
