using System.Diagnostics;
using System.Text.Json;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Events;
using OpenCareer.Domain.Simulation;

var timer = Stopwatch.StartNew();
var failures = new List<string>();
var passed = 0;
var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
var location = new WorldEventLocation("DFW", "US", "KAFW", "KAMA", "C208");
MarketState Market(MarketSegment segment = MarketSegment.GeneralCargo) =>
    new("KAFW-KAMA:test", segment, 1m, 1.25m, 1.25m, 120, 120, 120, 120, 1, 1, 0, start);
WorldSimulationState World(IEnumerable<WorldEventDefinition>? definitions = null, ulong seed = 42,
    MarketSegment segment = MarketSegment.GeneralCargo) => WorldSimulation.Create(Market(segment), location, seed, definitions);
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Equal<T>(T expected, T actual, string message) => Check(JsonSerializer.Serialize(expected) == JsonSerializer.Serialize(actual), message);
void Throws(Action action)
{
    try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; }
    throw new Exception("Expected rejection did not occur.");
}
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures.Add(name); Console.Error.WriteLine($"FAIL {name}: {e}"); }
}
WorldEventInstance Event(string id, WorldEventScope scope, string? target, DateTimeOffset from, DateTimeOffset to, WorldEventEffects effects) =>
    new(id, id, id, WorldEventTier.LocalOperational, scope, target, from, to, effects);

Test("daily, six-hour and weekly requests produce identical checkpoint", () =>
{
    var initial = World(); var daily = initial; var sixHourly = initial;
    var weekly = WorldSimulation.Advance(initial, start.AddDays(28));
    for (var day = 1; day <= 28; day++) daily = WorldSimulation.Advance(daily, start.AddDays(day));
    for (var n = 1; n <= 112; n++) sixHourly = WorldSimulation.Advance(sixHourly, start.AddHours(n * 6));
    Equal(weekly, daily, "Daily replay differs"); Equal(weekly, sixHourly, "Six-hour replay differs");
});
Test("sub-hour requests cannot reroll or change simulation", () =>
{
    var initial = World(); var incremental = initial;
    for (var n = 1; n <= 288; n++) incremental = WorldSimulation.Advance(incremental, start.AddMinutes(n * 5));
    Equal(WorldSimulation.Advance(initial, start.AddDays(1)), incremental, "Five-minute polling changes state");
    Equal(incremental, WorldSimulation.Advance(incremental, incremental.RequestedThrough), "Repeated time changed state");
});
Test("fractional-hour save/reload preserves future economy and event RNG", () =>
{
    var halfway = WorldSimulation.Advance(World(), start.AddDays(17).AddMinutes(23));
    var restored = JsonSerializer.Deserialize<WorldSimulationState>(JsonSerializer.Serialize(halfway))!;
    Equal(WorldSimulation.Advance(halfway, start.AddDays(100)), WorldSimulation.Advance(restored, start.AddDays(100)), "Reload divergence");
});
Test("random stream checkpoint preserves next values", () =>
{
    var random = new DeterministicRandom(12); for (var i = 0; i < 99; i++) random.NextUInt64();
    var restored = new DeterministicRandom(random.State);
    for (var i = 0; i < 100; i++) Check(random.NextUInt64() == restored.NextUInt64(), "Random stream replay differs");
});
Test("catalog order does not reshuffle events", () =>
{
    Equal(WorldSimulation.Advance(World(), start.AddDays(100)),
        WorldSimulation.Advance(World(WorldEventCatalog.Definitions.Reverse()), start.AddDays(100)), "Catalog order changed result");
});
Test("unrelated mission-only event does not change economy or regional RNG", () =>
{
    var neutral = new WorldEventDefinition("neutral", "Mission opportunity", WorldEventTier.LocalOperational,
        WorldEventScope.Region, 500, .01, .1, new(MissionOpportunities: MissionOpportunity.Reposition));
    var baseline = WorldSimulation.Advance(World(Array.Empty<WorldEventDefinition>()), start.AddDays(90));
    var added = WorldSimulation.Advance(World(new[] { neutral }), start.AddDays(90));
    Equal(baseline.Market, added.Market, "Unrelated event changes market integration");
    Equal(baseline.Cycle, added.Cycle, "Unrelated event changes cycle RNG");
});
Test("clock rejects backwards time and inconsistent checkpoint", () =>
{
    var state = WorldSimulation.Advance(World(), start.AddMinutes(40));
    Throws(() => WorldSimulation.Advance(state, start.AddMinutes(20)));
    Throws(() => WorldSimulation.Advance(state with { SchemaVersion = 99 }, start.AddDays(1)));
    Throws(() => WorldSimulation.Advance(state with { Market = state.Market with { UpdatedAt = start.AddMinutes(1) } }, start.AddDays(1)));
});
Test("event scopes distinguish airport, fleet, region, nation and global", () =>
{
    var cases = new[] { (WorldEventScope.Airport, "KLAX", false), (WorldEventScope.Airport, "KAMA", true),
        (WorldEventScope.Region, "OTHER", false), (WorldEventScope.Region, "DFW", true),
        (WorldEventScope.National, "CA", false), (WorldEventScope.National, "US", true),
        (WorldEventScope.FleetType, "B737", false), (WorldEventScope.FleetType, "C208", true),
        (WorldEventScope.Global, (string?)null, true) };
    foreach (var (scope, target, expected) in cases)
    {
        var instance = Event("scope", scope, target, start, start.AddDays(1), new(CapacityMultiplier: .5));
        Check(WorldEventEngine.Aggregate(new[] { instance }, start, location).CapacityMultiplier == (expected ? .5 : 1), "Wrong scope matched");
    }
});
Test("0.2-day outage ends at 4.8 hours, not end of day", () =>
{
    var initial = World(Array.Empty<WorldEventDefinition>());
    var outage = Event("outage", WorldEventScope.Region, "DFW", start, start.AddDays(.2), new(CapacityMultiplier: 0));
    var shortOutage = WorldSimulation.Advance(initial with { Events = new[] { outage } }, start.AddDays(1));
    var longOutage = WorldSimulation.Advance(initial with { Events = new[] { outage with { EndsAt = start.AddDays(1) } } }, start.AddDays(1));
    Check(shortOutage.Market.BacklogUnits < longOutage.Market.BacklogUnits, "Partial outage applied all day");
    Check(shortOutage.Events.Length == 0, "Expired outage retained");
    Check(WorldEventEngine.Aggregate(new[] { outage }, outage.EndsAt, location).CapacityMultiplier == 1, "End boundary not exclusive");
    var partial = WorldSimulation.Advance(initial with { Events = new[] { outage } }, start.AddHours(3).AddMinutes(12));
    Equal(shortOutage, WorldSimulation.Advance(partial, start.AddDays(1)), "Event boundaries depend on requests");
});
Test("event beginning mid-hour is not applied early", () =>
{
    var initial = World(Array.Empty<WorldEventDefinition>());
    var late = Event("late", WorldEventScope.Region, "DFW", start.AddMinutes(30), start.AddHours(1), new(CapacityMultiplier: 0));
    var half = WorldSimulation.Advance(initial with { Events = new[] { late } }, start.AddHours(1));
    var full = WorldSimulation.Advance(initial with { Events = new[] { late with { StartsAt = start } } }, start.AddHours(1));
    Check(half.Market.BacklogUnits < full.Market.BacklogUnits, "Future event applied before start");
});
Test("invalid global or regional event target is rejected", () =>
{
    var global = WorldEventCatalog.Definitions.First(e => e.Scope == WorldEventScope.Global);
    Throws(() => WorldEventEngine.Start(global, start, "DFW", new(1)));
    var regional = WorldEventCatalog.Definitions.First(e => e.Scope == WorldEventScope.Region);
    Throws(() => WorldEventEngine.Start(regional, start, null, new(1)));
});
Test("zero service cannot increase permanent capacity over ten years", () =>
{
    var market = Market() with { CurrentUnitPrice = 5m, CapacityInvestmentSignal = 2 };
    var parameters = MarketParameterProfiles.For(market.Segment);
    for (var day = 0; day < 3650; day++)
    {
        var next = MarketTickEngine.Advance(market, parameters, new(AvailableCapacityMultiplier: 0)).State;
        Check(next.StructuralCapacityPerDay <= market.StructuralCapacityPerDay, "Closure caused expansion"); market = next;
    }
    Check(double.IsFinite(market.BacklogUnits), "Non-finite closure backlog");
});
Test("temporary restriction does not overwrite permanent capacity", () =>
{
    var next = MarketTickEngine.Advance(Market(), new(), new(AvailableCapacityMultiplier: .5)).State;
    Check(next.StructuralCapacityPerDay > 118 && next.StructuralCapacityPerDay < 122, "Permanent capacity multiplied by outage");
});
Test("finance freeze blocks positive expansion", () =>
{
    var initial = Market() with { CurrentUnitPrice = 2m, CapacityInvestmentSignal = 1 };
    var next = MarketTickEngine.Advance(initial, new(), new(FinanceLiquidityMultiplier: 0)).State;
    Check(next.StructuralCapacityPerDay == initial.StructuralCapacityPerDay, "Finance freeze ignored");
});
Test("non-finite market parameters are rejected", () =>
{
    foreach (var property in typeof(MarketParameters).GetProperties())
    foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    {
        var parameters = new MarketParameters(); property.SetValue(parameters, value);
        Throws(() => MarketTickEngine.Advance(Market(), parameters, new()));
    }
});
Test("non-finite cycle parameters are rejected", () =>
{
    foreach (var property in typeof(EconomicCycleParameters).GetProperties())
    foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    {
        var parameters = new EconomicCycleParameters(); property.SetValue(parameters, value);
        Throws(() => EconomicCycleEngine.Advance(new("DFW", 0, 0, start), parameters, new(1), 1));
    }
});
Test("invalid event effects and duplicate definitions are rejected", () =>
{
    Throws(() => WorldEventEngine.ValidateEffects(new(DemandMultiplier: double.NaN)));
    Throws(() => WorldEventEngine.ValidateEffects(new(FinanceLiquidityMultiplier: -1)));
    var definition = WorldEventCatalog.Definitions[0]; Throws(() => World(new[] { definition, definition }));
});
var plane = new AircraftCapabilityProfile("C208", "Caravan", AircraftCapability.Cargo, AircraftAccess.Civilian,
    3000, 900, 170, 9, 1, true, false, false);
var contract = new JobContract(Guid.Parse("11111111-1111-1111-1111-111111111111"), null, ContractKind.Cargo,
    ServiceTrack.CivilianEmployment, "KAFW", "KAMA", new(CompensationModel.PilotWage, 1000m, 200m, true, true, true),
    start, start.AddHours(3), start.AddHours(8), new(RequiredCapabilities: AircraftCapability.Cargo));
var dispatch = new ContractDispatchContext(start.AddHours(1), plane, AircraftAccess.Civilian, new(), true, true);
Test("contract valid lifecycle and duplicate transition rejection", () =>
{
    var accepted = contract.Accept(dispatch); var flying = accepted.Start(dispatch); var completed = flying.Complete(start.AddHours(4), true);
    Check(completed.Status == ContractStatus.Completed, "Completion failed");
    Throws(() => completed.Complete(start.AddHours(4), true)); Throws(() => accepted.Accept(dispatch));
});
Test("contract deadlines enforced at acceptance, departure and completion", () =>
{
    Throws(() => contract.Accept(dispatch with { Time = start.AddHours(4) }));
    Throws(() => contract.Accept(dispatch).Start(dispatch with { Time = start.AddHours(4) }));
    Throws(() => contract.Accept(dispatch).Start(dispatch).Complete(start.AddHours(9), true));
    Throws(() => contract.Expire(start.AddHours(2)));
    Check(contract.Expire(start.AddHours(4)).Status == ContractStatus.Expired, "Offer expiry failed");
    contract.Accept(dispatch with { Time = start.AddHours(3) });
});
Test("unverified qualification, infeasible route and incompatible aircraft rejected", () =>
{
    Throws(() => contract.Accept(dispatch with { QualificationsVerified = false }));
    Throws(() => contract.Accept(dispatch with { DispatchFeasibilityVerified = false }));
    Throws(() => contract.Accept(dispatch with { Aircraft = plane with { Capabilities = AircraftCapability.Passenger } }));
    Throws(() => contract.Accept(dispatch with { AuthorizedAccess = AircraftAccess.Military }));
    Throws(() => contract.Accept(dispatch).Start(dispatch).Complete(start.AddHours(4), false));
});
Test("closed and restricted airspace cannot be bypassed by ordinary jobs", () =>
{
    Throws(() => contract.Accept(dispatch with { Effects = new(AirspaceRestriction: AirspaceRestriction.Closed) }));
    Throws(() => contract.Accept(dispatch with { Effects = new(AirspaceRestriction: AirspaceRestriction.AuthorizedOperationsOnly) }));
    Throws(() => contract.Accept(dispatch with { Effects = new(AirspaceRestriction: AirspaceRestriction.MilitaryAndEmergencyAuthorizedOnly),
        RestrictedAirspaceAuthorized = true, EmergencyOperationAuthorized = true }));
    (contract with { Kind = ContractKind.DisasterRelief }).Accept(dispatch with {
        Effects = new(AirspaceRestriction: AirspaceRestriction.MilitaryAndEmergencyAuthorizedOnly),
        RestrictedAirspaceAuthorized = true, EmergencyOperationAuthorized = true });
});
Test("navigation outage and government authorization enforced", () =>
{
    Throws(() => contract.Accept(dispatch with { Effects = new(NavigationAvailability: NavigationAvailability.GpsUnavailable) }));
    contract.Accept(dispatch with { Effects = new(NavigationAvailability: NavigationAvailability.GpsUnavailable), NavigationPlanVerified = true });
    Throws(() => (contract with { GovernmentAuthorizationRequired = true }).Accept(dispatch));
});
Test("invalid aircraft and mission numbers rejected", () =>
{
    Throws(() => plane.Satisfies(new(MinimumPayloadPounds: double.NaN)));
    Throws(() => (plane with { MaximumRangeNauticalMiles = double.PositiveInfinity }).Satisfies(new()));
});
Test("contract timestamps reject completion before departure and departure before acceptance", () =>
{
    var accepted = contract.Accept(dispatch);
    Throws(() => accepted.Start(dispatch with { Time = start.AddMinutes(30) }));
    var flying = accepted.Start(dispatch with { Time = start.AddHours(2) });
    Throws(() => flying.Complete(start.AddHours(1), true));
    Equal(flying, JsonSerializer.Deserialize<JobContract>(JsonSerializer.Serialize(flying)), "Contract checkpoint differs");
});
Test("opportunity demand is limited to matching market segments", () =>
{
    var definition = WorldEventCatalog.Definitions.First(d => d.EventId == "local-charter-demand");
    var instance = WorldEventEngine.Start(definition, start, "KAFW", new(1));
    Check(WorldEventEngine.Aggregate(new[] { instance }, start, location, MarketSegment.GeneralCargo).DemandMultiplier == 1,
        "Charter event increased cargo demand");
    Check(WorldEventEngine.Aggregate(new[] { instance }, start, location, MarketSegment.BusinessCharter).DemandMultiplier > 1,
        "Charter demand missing");
});
Test("checkpoint rejects missing event schedule entries", () =>
{
    var state = World();
    Throws(() => WorldSimulation.Advance(state with { Schedule = Array.Empty<ScheduledWorldEvent>() }, start.AddDays(1)));
});
Test("operational restrictions are current within an uncommitted hour without rerolling", () =>
{
    var definition = new WorldEventDefinition("closure", "Closure", WorldEventTier.LocalOperational,
        WorldEventScope.Region, 1, .01, .01, new(AirspaceRestriction: AirspaceRestriction.Closed));
    var initial = World(new[] { definition });
    initial = initial with { Schedule = new[] { initial.Schedule[0] with { NextStartAt = start.AddMinutes(15) } } };
    var state = WorldSimulation.Advance(initial, start.AddMinutes(20));
    var before = JsonSerializer.Serialize(state);
    Check(WorldSimulation.GetOperationalEffects(state).AirspaceRestriction == AirspaceRestriction.Closed, "Pending-hour closure missed");
    Check(before == JsonSerializer.Serialize(state), "Preview consumed saved RNG");
    var later = WorldSimulation.Advance(state, start.AddMinutes(40));
    Check(WorldSimulation.GetOperationalEffects(later).AirspaceRestriction == AirspaceRestriction.Normal, "Expired pending-hour closure retained");
    Equal(WorldSimulation.Advance(initial, start.AddHours(1)), WorldSimulation.Advance(later, start.AddHours(1)), "Preview changed replay");
});
Test("all 14 segments remain finite across two three-year event paths", () =>
{
    foreach (var segment in Enum.GetValues<MarketSegment>())
    foreach (var seed in new ulong[] { 42, 192837 })
    {
        var state = World(seed: seed, segment: segment);
        for (var year = 1; year <= 3; year++)
        {
            state = WorldSimulation.Advance(state, start.AddDays(365 * year)); var m = state.Market;
            Check(m.CurrentUnitPrice > 0 && m.StructuralCapacityPerDay > 0 && double.IsFinite(m.StructuralCapacityPerDay)
                && m.BacklogUnits >= 0 && double.IsFinite(m.BacklogUnits), $"Invalid {segment}, seed {seed}, year {year}");
        }
    }
});
Test("ten-year integrated world health checks", () =>
{
    var state = World();
    for (var year = 1; year <= 10; year++)
    {
        state = WorldSimulation.Advance(state, start.AddDays(year * 365));
        Check(state.Market.CurrentUnitPrice > 0 && double.IsFinite(state.Market.BacklogUnits)
            && state.Market.StructuralCapacityPerDay < 10000, "Runaway baseline economy");
    }
    Console.WriteLine($"10-year sample: price={state.Market.CurrentUnitPrice:F4}, capacity={state.Market.StructuralCapacityPerDay:F2}, backlog={state.Market.BacklogUnits:F2}");
});
Console.WriteLine($"Regression scenarios: {passed} passed, {failures.Count} failed; elapsed {timer.Elapsed.TotalSeconds:F2}s (informational, not an MSFS benchmark).");
if (failures.Count > 0) Environment.ExitCode = 1;
