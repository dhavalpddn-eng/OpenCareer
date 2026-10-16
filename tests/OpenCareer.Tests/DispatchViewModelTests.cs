using OpenCareer.App.ViewModels;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class DispatchViewModelTests
{
    [Fact]
    public async Task InitializeProjectsInstalledAircraftAndSelectsFirst()
    {
        var planning = new StubPlanning();
        var viewModel = new DispatchViewModel(
            Inventory(
                Item("aircraft-b", "Bravo"),
                Item("aircraft-a", "Alpha")),
            planning);

        await viewModel.InitializeAsync();

        Assert.Equal(2, viewModel.AircraftOptions.Count);
        Assert.Equal("aircraft-b", viewModel.SelectedAircraftId);
        Assert.True(viewModel.CanEvaluate);
        Assert.Equal(0, planning.CallCount);
    }

    [Fact]
    public async Task ValidInputUsesCompositePlannerAndProjectsFeasibleResult()
    {
        var planning = new StubPlanning
        {
            Result = new(
                CompositeDispatchPlanningStatus.Evaluated,
                null,
                null,
                null,
                null,
                null,
                new(0, 6, 30, 180, 50, 300),
                DispatchFeasibilityResult.Create(
                    DispatchFeasibilityStatus.Feasible,
                    "18",
                    "27",
                    []))
        };
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            planning);
        await viewModel.InitializeAsync();

        await viewModel.EvaluateAsync(ValidInput());

        Assert.Equal(1, planning.CallCount);
        Assert.Equal("aircraft-a", planning.AircraftId);
        Assert.Equal("KJFK", planning.OriginIcao);
        Assert.Equal("KBOS", planning.DestinationIcao);
        Assert.Equal(250, planning.Requirements!.PayloadPounds);
        Assert.Equal(300, planning.Requirements.RequiredRangeNauticalMiles);
        Assert.Equal(10, planning.Requirements.RangeSafetyMarginPercent);
        Assert.Equal(15, planning.Requirements.RunwayLengthSafetyMarginPercent);
        Assert.Equal(0, planning.Cruise!.ProfileIndex);
        Assert.Equal(6000, planning.Cruise.PlannedCruiseWeightPounds);
        Assert.Equal(260, planning.RouteFuel!.CruiseSegmentDistanceNauticalMiles);
        Assert.Equal("FEASIBLE", viewModel.ResultStatus);
        Assert.Contains("30.0", viewModel.FuelSummary, StringComparison.Ordinal);
        Assert.Contains("18", viewModel.RunwaySummary, StringComparison.Ordinal);
        Assert.Empty(viewModel.Issues);
    }

    [Fact]
    public async Task InvalidInputDoesNotCallPlanner()
    {
        var planning = new StubPlanning();
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            planning);
        await viewModel.InitializeAsync();

        await viewModel.EvaluateAsync(
            ValidInput() with { OriginIcao = "JFK" });

        Assert.Equal(0, planning.CallCount);
        Assert.Equal("INVALID INPUT", viewModel.ResultStatus);
        Assert.Contains("four letters", viewModel.ResultDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingAuthoritativePerformanceStaysInsufficient()
    {
        var planning = new StubPlanning
        {
            Result = new(
                CompositeDispatchPlanningStatus.InsufficientData,
                CompositeDispatchPlanningReason.FuelWeightPlanUnavailable,
                RouteFuelWeightPlanningReason.RouteFuelUnavailable,
                RouteFuelPlanningReason.CruisePerformanceUnavailable,
                CruisePerformancePlanningReason.AircraftDispatchPerformanceUnknown,
                null,
                null,
                null)
        };
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            planning);
        await viewModel.InitializeAsync();

        await viewModel.EvaluateAsync(ValidInput());

        Assert.Equal("INSUFFICIENT DATA", viewModel.ResultStatus);
        Assert.Contains("Aircraft Dispatch Performance Unknown", viewModel.ResultDetail, StringComparison.Ordinal);
        Assert.Equal("Fuel plan unavailable", viewModel.FuelSummary);
    }

    [Fact]
    public async Task InfeasibleIssueRetainsQuantitativeEvidence()
    {
        var planning = new StubPlanning
        {
            Result = new(
                CompositeDispatchPlanningStatus.Evaluated,
                null,
                null,
                null,
                null,
                null,
                new(0, 6, 30, 180, 50, 300),
                DispatchFeasibilityResult.Create(
                    DispatchFeasibilityStatus.Infeasible,
                    null,
                    null,
                    [
                        new(
                            DispatchFeasibilityReason.RunwayTooShort,
                            DispatchEndpoint.Origin,
                            "KJFK",
                            "04L",
                            RequiredFeet: 9000,
                            AvailableFeet: 8000)
                    ]))
        };
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            planning);
        await viewModel.InitializeAsync();

        await viewModel.EvaluateAsync(ValidInput());

        Assert.Equal("INFEASIBLE", viewModel.ResultStatus);
        DispatchIssueViewModel issue = Assert.Single(viewModel.Issues);
        Assert.Equal("Runway Too Short", issue.Reason);
        Assert.Contains("required 9000 ft", issue.Detail, StringComparison.Ordinal);
        Assert.Contains("available 8000 ft", issue.Detail, StringComparison.Ordinal);
    }

    private static DispatchPlanningFormInput ValidInput() =>
        new(
            "kjfk",
            "kbos",
            "250",
            "300",
            "260",
            "0",
            "0",
            "6000",
            "0",
            "12000",
            "10",
            "8",
            "3",
            "2",
            "5",
            "30",
            "1",
            "10",
            "15");

    private static IInstalledAircraftInventorySource Inventory(
        params InstalledAircraftInventoryItem[] items) =>
        new StubInventory(new(
            InstalledAircraftInventoryStatus.Ready,
            $"{items.Length} installed aircraft record(s).",
            items));

    private static InstalledAircraftInventoryItem Item(
        string id,
        string displayName) =>
        new(
            id,
            displayName,
            "INSTALLED",
            "ICAO type unknown",
            "Powerplant unknown",
            "Capability data incomplete",
            "Fleet status not tracked",
            "No operational availability record exists",
            "Career access unknown");

    private sealed class StubInventory(InstalledAircraftInventorySnapshot snapshot)
        : IInstalledAircraftInventorySource
    {
        public Task<InstalledAircraftInventorySnapshot> ReadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class StubPlanning : ICompositeDispatchPlanningService
    {
        public CompositeDispatchPlanningResult Result { get; set; } =
            new(
                CompositeDispatchPlanningStatus.InsufficientData,
                CompositeDispatchPlanningReason.FuelWeightPlanUnavailable,
                RouteFuelWeightPlanningReason.RouteFuelUnavailable,
                RouteFuelPlanningReason.CruisePerformanceUnavailable,
                CruisePerformancePlanningReason.AircraftDispatchPerformanceUnknown,
                null,
                null,
                null);

        public int CallCount { get; private set; }
        public string? AircraftId { get; private set; }
        public string? OriginIcao { get; private set; }
        public string? DestinationIcao { get; private set; }
        public CruisePerformancePlanningRequest? Cruise { get; private set; }
        public RouteFuelPlanningRequest? RouteFuel { get; private set; }
        public OperationDispatchRequirements? Requirements { get; private set; }

        public Task<CompositeDispatchPlanningResult> EvaluateAsync(
            string aircraftId,
            string originIcao,
            string destinationIcao,
            CruisePerformancePlanningRequest cruiseRequest,
            RouteFuelPlanningRequest routeFuelRequest,
            OperationDispatchRequirements requirements,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            AircraftId = aircraftId;
            OriginIcao = originIcao;
            DestinationIcao = destinationIcao;
            Cruise = cruiseRequest;
            RouteFuel = routeFuelRequest;
            Requirements = requirements;
            return Task.FromResult(Result);
        }
    }
}
