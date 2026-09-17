using System.Diagnostics;
using System.Text.Json;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Events;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Simulation;

var timer = Stopwatch.StartNew();
var failures = new List<string>();
var passed = 0;
var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection did not occur."); }
void Test(string name, Action action) { try { action(); passed++; Console.WriteLine($"PASS {name}"); } catch (Exception e) { failures.Add(name); Console.Error.WriteLine($"FAIL {name}: {e.Message}"); } }

Test("first used light aircraft tuning remains in 50-80 hour target", () =>
{
    var policy = CareerProgressionPolicy.Default;
    policy.Validate();
    Check(policy.MinimumAcquisitionCash == 8_000m, "Acquisition cash target drifted");
    Check(policy.ExpectedHoursToAcquisition == 64, "Expected ownership center point drifted");
});

Test("solo absence has grace period and bounded fixed liabilities", () =>
{
    var policy = OfflineLiabilityPolicy.Default;
    Check(policy.Assess(start, start.AddDays(2), 1_500m, false).AccruedFixedLiabilities == 0, "Grace period billed player");
    var sixtyDays = policy.Assess(start, start.AddDays(60), 1_500m, false);
    Check(sixtyDays.BillableDuration == TimeSpan.FromDays(30), "Solo liability cap failed");
    Check(sixtyDays.AccruedFixedLiabilities == 1_500m, "Solo cap amount incorrect");
    Check(sixtyDays.ProtectedDuration == TimeSpan.FromDays(27), "Protected inactive time incorrect");
});

Test("staffed operations can persist longer than solo company", () =>
{
    var policy = OfflineLiabilityPolicy.Default;
    var staffed = policy.Assess(start, start.AddDays(180), 1_500m, true);
    Check(staffed.BillableDuration == TimeSpan.FromDays(90), "Staffed cap failed");
    Check(staffed.AccruedFixedLiabilities == 4_500m, "Staffed settlement incorrect");
});

Test("KRME remains mixed civilian government military and UAS market", () =>
{
    var airport = InitialAirportProfiles.GriffissInternational;
    airport.Validate();
    Check(airport.Icao == "KRME", "Wrong reference airport");
    Check(airport.CivilianDemand > .5 && airport.GovernmentDemand > .5 && airport.MilitaryDemand > .5 && airport.UasResearchDemand > .8,
        "KRME mixed-use demand profile regressed");
    Check(airport.DemandFor(ServiceTrack.CivilianEmployment) > 0 && airport.DemandFor(ServiceTrack.MilitaryService) > 0,
        "KRME lost a career path");
});

Test("manual ground work is optional bounded and duplicate resistant", () =>
{
    var policy = ManualGroundProcedurePolicy.Default;
    GroundProcedureEvent Done(GroundProcedureKind kind) => new(start, kind, ProcedureOutcome.Completed, ProcedureObservationSource.UserConfirmed, .95);
    var reward = policy.CalculateReward(new[] { Done(GroundProcedureKind.PreflightInspection), Done(GroundProcedureKind.PreflightInspection),
        Done(GroundProcedureKind.Deicing), Done(GroundProcedureKind.CargoLoading), Done(GroundProcedureKind.Fueling),
        Done(GroundProcedureKind.CargoUnloading), Done(GroundProcedureKind.PassengerBoarding) });
    Check(reward == 35m, "Manual reward cap or duplicate protection failed");
    Check(policy.CalculateReward(new[] { new GroundProcedureEvent(start, GroundProcedureKind.Fueling, ProcedureOutcome.Completed,
        ProcedureObservationSource.SimulatorTelemetry, 1) }) == 0, "Automated procedure earned manual reward");
});

Test("world simulation remains deterministic after foundation additions", () =>
{
    var location = new WorldEventLocation("DFW", "US", "KAFW", "KAMA", "C208");
    var market = new MarketState("KAFW-KAMA:test", MarketSegment.GeneralCargo, 1m, 1.25m, 1.25m, 120, 120, 120, 120, 1, 1, 0, start);
    var first = WorldSimulation.Create(market, location, 42);
    var second = JsonSerializer.Deserialize<WorldSimulationState>(JsonSerializer.Serialize(first))!;
    var a = WorldSimulation.Advance(first, start.AddDays(365));
    var b = WorldSimulation.Advance(second, start.AddDays(365));
    Check(JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b), "Deterministic replay diverged");
    Check(a.Market.CurrentUnitPrice > 0 && double.IsFinite(a.Market.BacklogUnits), "Economy health failed");
});

Test("contract still requires verified flight evidence", () =>
{
    var plane = new AircraftCapabilityProfile("C208", "Caravan", AircraftCapability.Cargo, AircraftAccess.Civilian,
        3000, 900, 170, 9, 1, true, false, false);
    var contract = new JobContract(Guid.Parse("11111111-1111-1111-1111-111111111111"), null, ContractKind.Cargo,
        ServiceTrack.CivilianEmployment, "KRME", "KALB", new(CompensationModel.PilotWage, 1000m, 200m, true, true, true),
        start, start.AddHours(3), start.AddHours(8), new(RequiredCapabilities: AircraftCapability.Cargo));
    var dispatch = new ContractDispatchContext(start.AddHours(1), plane, AircraftAccess.Civilian, new(), true, true);
    var flying = contract.Accept(dispatch).Start(dispatch);
    Throws(() => flying.Complete(start.AddHours(3), false));
    Check(flying.Complete(start.AddHours(3), true).Status == ContractStatus.Completed, "Verified completion failed");
});

Console.WriteLine($"Regression scenarios: {passed} passed, {failures.Count} failed; elapsed {timer.Elapsed.TotalSeconds:F2}s.");
if (failures.Count > 0) Environment.ExitCode = 1;
