using OpenCareer.Application.Careers;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class CareerJobMarketDispatchSourceTests
{
    [Fact]
    public async Task MissingExactAircraftEvidenceCannotAuthorizePlanning()
    {
        var profiles = new StubProfiles();
        var planning = new StubPlanning(_ => Result());
        CareerJobMarketFeasibilitySnapshot snapshot = Snapshot(
            new CareerJobMarketAircraftCandidate(ContractKind.Ferry, "KBBB", "aircraft-a", 1_000));

        var eligible = await new CareerJobMarketDispatchSource(profiles, planning)
            .FilterAsync("KAAA", snapshot with { AircraftCandidates = null }, NoCargo());

        Assert.Empty(eligible);
        Assert.Empty(profiles.Reads);
        Assert.Empty(planning.Calls);
    }

    [Theory]
    [InlineData(DispatchCruiseProfileCatalogStatus.DispatchPerformanceUnavailable)]
    [InlineData(DispatchCruiseProfileCatalogStatus.NoUsableCruiseProfiles)]
    public async Task MissingCruiseAuthorityCannotReachPhysicalPlanner(
        DispatchCruiseProfileCatalogStatus status)
    {
        var profiles = new StubProfiles(_ => new(status, "Unavailable.", []));
        var planning = new StubPlanning(_ => Result());

        var eligible = await new CareerJobMarketDispatchSource(profiles, planning)
            .FilterAsync("KAAA", Snapshot(new CareerJobMarketAircraftCandidate(ContractKind.Ferry, "KBBB", "aircraft-a", 1_000)), NoCargo());

        Assert.Empty(eligible);
        Assert.Equal(["aircraft-a"], profiles.Reads);
        Assert.Empty(planning.Calls);
    }

    [Theory]
    [InlineData(CompositeDispatchPlanningStatus.InsufficientData, DispatchFeasibilityStatus.Feasible, true)]
    [InlineData(CompositeDispatchPlanningStatus.Evaluated, DispatchFeasibilityStatus.Infeasible, true)]
    [InlineData(CompositeDispatchPlanningStatus.Evaluated, DispatchFeasibilityStatus.InsufficientData, true)]
    [InlineData(CompositeDispatchPlanningStatus.Evaluated, DispatchFeasibilityStatus.Feasible, false)]
    public async Task EligibilityRequiresBothFuelAndPhysicalProof(
        CompositeDispatchPlanningStatus status, DispatchFeasibilityStatus dispatch, bool hasFuel)
    {
        var planning = new StubPlanning(_ => Result(status, dispatch, hasFuel));
        var eligible = await new CareerJobMarketDispatchSource(new StubProfiles(), planning)
            .FilterAsync("KAAA", Snapshot(new CareerJobMarketAircraftCandidate(ContractKind.Ferry, "KBBB", "aircraft-a", 1_000)), NoCargo());

        Assert.Empty(eligible);
        Assert.Single(planning.Calls);
    }

    [Fact]
    public async Task PassengerCannotBorrowAZeroPayloadFerryApproval()
    {
        var profiles = new StubProfiles();
        var planning = new StubPlanning(call => Result(dispatch:
            call.Requirements.PayloadPounds == 0
                ? DispatchFeasibilityStatus.Feasible : DispatchFeasibilityStatus.Infeasible));
        var snapshot = Snapshot(
            new(ContractKind.Ferry, "KBBB", "aircraft-a", 1_000),
            new(ContractKind.Reposition, "KBBB", "aircraft-a", 1_000),
            new(ContractKind.Passenger, "KBBB", "aircraft-a", 1_000));

        var eligible = await new CareerJobMarketDispatchSource(profiles, planning)
            .FilterAsync("KAAA", snapshot, NoCargo());

        Assert.Equal(2, eligible.Count);
        Assert.Contains(ContractKind.Ferry, eligible.Keys);
        Assert.Contains(ContractKind.Reposition, eligible.Keys);
        Assert.DoesNotContain(ContractKind.Passenger, eligible.Keys);
        Assert.Equal(["aircraft-a"], profiles.Reads);
        Assert.Equal([0d, JobMarketPolicy.Default.StandardPassengerPayloadPounds],
            planning.Calls.Select(call => call.Requirements.PayloadPounds));
        Assert.All(planning.Calls, call =>
        {
            Assert.Equal(10, call.RouteFuel.ReservePolicy.ContingencyPercentOfTripFuel);
            Assert.Equal(45, call.RouteFuel.ReservePolicy.FinalReserveMinutesAtCruiseConsumption);
            Assert.Equal(10, call.Requirements.RangeSafetyMarginPercent);
            Assert.Equal(15, call.Requirements.RunwayLengthSafetyMarginPercent);
        });
    }

    [Fact]
    public async Task TriesOtherProvenAircraftAndProfilesBeforeRejectingRoute()
    {
        var profiles = new StubProfiles(_ => ReadyProfiles() with
        {
            Profiles = [Profile(0), Profile(1)]
        });
        var planning = new StubPlanning(call => Result(dispatch:
            call.Aircraft == "aircraft-b" && call.Cruise.ProfileIndex == 1
                ? DispatchFeasibilityStatus.Feasible : DispatchFeasibilityStatus.Infeasible));

        var eligible = await new CareerJobMarketDispatchSource(profiles, planning)
            .FilterAsync("KAAA", Snapshot(
                new(ContractKind.Ferry, "KBBB", "aircraft-a", 1_000),
                new(ContractKind.Ferry, "KBBB", "aircraft-b", 1_000)), NoCargo());

        Assert.Equal(["KBBB"], eligible[ContractKind.Ferry]);
        Assert.Equal(4, planning.Calls.Count);
    }

    [Theory]
    [InlineData(ContractKind.Cargo)]
    [InlineData(ContractKind.ExpressCargo)]
    [InlineData(ContractKind.AogPartsDelivery)]
    public async Task CargoUsesFrozenMassAndOnlyAnAircraftAbleToCarryThatLot(ContractKind kind)
    {
        var planning = new StubPlanning(_ => Result());
        var lots = NoCargo();
        lots.Add(kind, new Dictionary<string, AcceptedCargoLot> { ["KBBB"] = Lot(450) });

        var eligible = await new CareerJobMarketDispatchSource(new StubProfiles(), planning)
            .FilterAsync("KAAA", Snapshot(
                new(kind, "KBBB", "small-aircraft", 200),
                new(kind, "KBBB", "large-aircraft", 1_000)), lots);

        Assert.Contains("KBBB", eligible[kind]);
        PlanningCall call = Assert.Single(planning.Calls);
        Assert.Equal("large-aircraft", call.Aircraft);
        Assert.Equal(450, call.Requirements.PayloadPounds);
    }

    [Fact]
    public async Task MissingCargoManifestCannotAuthorizeAZeroPayloadCargoFlight()
    {
        var planning = new StubPlanning(_ => Result());
        var eligible = await new CareerJobMarketDispatchSource(new StubProfiles(), planning)
            .FilterAsync("KAAA", Snapshot(new CareerJobMarketAircraftCandidate(ContractKind.Cargo, "KBBB", "aircraft-a", 1_000)), NoCargo());

        Assert.Empty(eligible);
        Assert.Empty(planning.Calls);
    }

    [Fact]
    public async Task MismatchedCargoRouteFailsBeforePlanning()
    {
        var planning = new StubPlanning(_ => Result());
        var lots = NoCargo();
        lots.Add(ContractKind.Cargo, new Dictionary<string, AcceptedCargoLot>
        {
            ["KBBB"] = Lot(450) with { OriginIcao = "KCCC" }
        });
        var service = new CareerJobMarketDispatchSource(new StubProfiles(), planning);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.FilterAsync(
            "KAAA", Snapshot(new CareerJobMarketAircraftCandidate(ContractKind.Cargo, "KBBB", "aircraft-a", 1_000)), lots));
        Assert.Empty(planning.Calls);
    }

    [Fact]
    public async Task SubsequentRefreshRechecksChangedEvidence()
    {
        bool feasible = true;
        var planning = new StubPlanning(_ => Result(dispatch: feasible
            ? DispatchFeasibilityStatus.Feasible : DispatchFeasibilityStatus.Infeasible));
        var profiles = new StubProfiles();
        var service = new CareerJobMarketDispatchSource(profiles, planning);
        var snapshot = Snapshot(new CareerJobMarketAircraftCandidate(ContractKind.Ferry, "KBBB", "aircraft-a", 1_000));

        Assert.NotEmpty(await service.FilterAsync("KAAA", snapshot, NoCargo()));
        feasible = false;
        Assert.Empty(await service.FilterAsync("KAAA", snapshot, NoCargo()));
        Assert.Equal(2, planning.Calls.Count);
        Assert.Equal(2, profiles.Reads.Count);
    }

    [Fact]
    public async Task CancellationDuringEvaluationCannotReturnEligibility()
    {
        using var cancellation = new CancellationTokenSource();
        var planning = new StubPlanning(_ => { cancellation.Cancel(); return Result(); });
        var service = new CareerJobMarketDispatchSource(new StubProfiles(), planning);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FilterAsync(
            "KAAA", Snapshot(new CareerJobMarketAircraftCandidate(ContractKind.Ferry, "KBBB", "aircraft-a", 1_000)), NoCargo(), cancellation.Token));
    }

    private static CareerJobMarketFeasibilitySnapshot Snapshot(
        params CareerJobMarketAircraftCandidate[] candidates) =>
        new(CareerJobMarketFeasibilityStatus.Ready,
            [new("KBBB", 100, 0, 0, 1, 1)], "Static capability proven.",
            candidates.GroupBy(candidate => candidate.Kind).ToDictionary(
                group => group.Key,
                group => (IReadOnlySet<string>)group.Select(item => item.DestinationIcao).ToHashSet()),
            AircraftCandidates: candidates);

    private static Dictionary<ContractKind, IReadOnlyDictionary<string, AcceptedCargoLot>> NoCargo() => [];

    private static AcceptedCargoLot Lot(double mass) => new(
        Guid.Parse("e1000000-0000-0000-0000-000000000001"), "coffee.green-beans", "Coffee",
        CargoCommodityCategory.GeneralFreight, (decimal)mass, CargoUnitOfTrade.Pound, mass, null,
        "KAAA", "KBBB", "Shipper", "Consignee", (decimal)mass, 1m, 2m, CargoHandlingRequirement.None);

    private static CompositeDispatchPlanningResult Result(
        CompositeDispatchPlanningStatus status = CompositeDispatchPlanningStatus.Evaluated,
        DispatchFeasibilityStatus dispatch = DispatchFeasibilityStatus.Feasible,
        bool hasFuel = true) => new(status, null, null, null, null, null,
            hasFuel ? new RouteFuelWeightPlan(0, 6, 20, 120, 50, 300) : null,
            DispatchFeasibilityResult.Create(dispatch, "01", "01",
                dispatch == DispatchFeasibilityStatus.Feasible
                    ? [] : [new(DispatchFeasibilityReason.FuelExceedsAircraftMaximum)]));

    private static DispatchCruiseProfileOption Profile(int index) => new(
        index, 0, "Cruise", new(1_800, 2_550, -20, 40, 0, 14_000), "Test evidence");

    private static DispatchCruiseProfileCatalog ReadyProfiles() =>
        new(DispatchCruiseProfileCatalogStatus.Ready, "Proven profile.", [Profile(0)]);

    private sealed class StubProfiles(Func<string, DispatchCruiseProfileCatalog>? read = null)
        : IDispatchCruiseProfileSource
    {
        public List<string> Reads { get; } = [];
        public Task<DispatchCruiseProfileCatalog> ReadAsync(string canonicalAircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads.Add(canonicalAircraftId);
            return Task.FromResult(read?.Invoke(canonicalAircraftId) ?? ReadyProfiles());
        }
    }

    private sealed record PlanningCall(string Aircraft, CruisePerformancePlanningRequest Cruise,
        RouteFuelPlanningRequest RouteFuel, OperationDispatchRequirements Requirements);

    private sealed class StubPlanning(Func<PlanningCall, CompositeDispatchPlanningResult> evaluate)
        : ICompositeDispatchPlanningService
    {
        public List<PlanningCall> Calls { get; } = [];
        public Task<CompositeDispatchPlanningResult> EvaluateAsync(string aircraftId, string originIcao,
            string destinationIcao, CruisePerformancePlanningRequest cruiseRequest,
            RouteFuelPlanningRequest routeFuelRequest, OperationDispatchRequirements requirements,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var call = new PlanningCall(aircraftId, cruiseRequest, routeFuelRequest, requirements);
            Calls.Add(call);
            return Task.FromResult(evaluate(call));
        }

        public Task<CompositeDispatchPlanningResult> EvaluateAsync(string aircraftId, string originIcao,
            string destinationIcao, CruisePerformancePlanningRequest cruiseRequest,
            RouteFuelPlanningRequest routeFuelRequest, OperationDispatchRequirements requirements,
            string reservationId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Market screening cannot use reservation authority.");
    }
}
