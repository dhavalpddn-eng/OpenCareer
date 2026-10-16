using OpenCareer.App.ViewModels;
using OpenCareer.Application.Careers;
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
            Profiles(),
            planning,
            NoAcceptedContext());

        await viewModel.InitializeAsync();

        Assert.Equal(2, viewModel.AircraftOptions.Count);
        Assert.Equal("aircraft-b", viewModel.SelectedAircraftId);
        Assert.False(viewModel.IsAcceptedContextLocked);
        Assert.Single(viewModel.CruiseProfileOptions);
        Assert.False(viewModel.CanEvaluate);
        Assert.Equal(0, planning.CallCount);
    }

    [Fact]
    public async Task EvaluationRequiresAnExplicitRegistryProfileSelection()
    {
        var planning = new StubPlanning();
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            planning,
            NoAcceptedContext());
        await viewModel.InitializeAsync();

        await viewModel.EvaluateAsync(ValidInput());

        Assert.Equal(0, planning.CallCount);
        Assert.Equal("NOT EVALUATED", viewModel.ResultStatus);
        Assert.Contains("authoritative cruise profile", viewModel.ResultDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AircraftChangeClearsPriorProfileSelection()
    {
        var profiles = new StubProfiles(new(
            DispatchCruiseProfileCatalogStatus.Ready,
            "Authoritative profile available.",
            [new(0, 0, "Economy · fuel type 0", Envelope(), "Registry evidence")]));
        var viewModel = new DispatchViewModel(
            Inventory(
                Item("aircraft-a", "Alpha"),
                Item("aircraft-b", "Bravo")),
            profiles,
            new StubPlanning(),
            NoAcceptedContext());
        await InitializeReadyAsync(viewModel);

        await viewModel.SetSelectedAircraftAsync("aircraft-b");

        Assert.Null(viewModel.SelectedCruiseProfileIndex);
        Assert.False(viewModel.CanEvaluate);
        Assert.Equal(["aircraft-a", "aircraft-b"], profiles.Requests);
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
            Profiles(),
            planning,
            NoAcceptedContext());
        await InitializeReadyAsync(viewModel);

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
        Assert.Equal(0, planning.Cruise.FuelTypeIndex);
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
            Profiles(),
            planning,
            NoAcceptedContext());
        await InitializeReadyAsync(viewModel);

        await viewModel.EvaluateAsync(
            ValidInput() with { OriginIcao = "JFK" });

        Assert.Equal(0, planning.CallCount);
        Assert.Equal("INVALID INPUT", viewModel.ResultStatus);
        Assert.Contains("four letters", viewModel.ResultDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConditionsOutsideDisplayedEnvelopeDoNotCallPlanner()
    {
        var planning = new StubPlanning();
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            planning,
            NoAcceptedContext());
        await InitializeReadyAsync(viewModel);

        await viewModel.EvaluateAsync(
            ValidInput() with { PlannedCruiseWeightPounds = "9000" });

        Assert.Equal(0, planning.CallCount);
        Assert.Equal("INVALID INPUT", viewModel.ResultStatus);
        Assert.Contains("displayed common envelope", viewModel.ResultDetail, StringComparison.Ordinal);
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
            Profiles(),
            planning,
            NoAcceptedContext());
        await InitializeReadyAsync(viewModel);

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
            Profiles(),
            planning,
            NoAcceptedContext());
        await InitializeReadyAsync(viewModel);

        await viewModel.EvaluateAsync(ValidInput());

        Assert.Equal("INFEASIBLE", viewModel.ResultStatus);
        DispatchIssueViewModel issue = Assert.Single(viewModel.Issues);
        Assert.Equal("Runway Too Short", issue.Reason);
        Assert.Contains("required 9000 ft", issue.Detail, StringComparison.Ordinal);
        Assert.Contains("available 8000 ft", issue.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceptedContextPrefillsRouteAndUsesOnlyMatchingReservation()
    {
        var planning = new StubPlanning();
        var accepted = new StubAcceptedContext(new(
            AcceptedDispatchPlanningContextStatus.Ready,
            "Accepted job loaded.",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            OpenCareer.Domain.Careers.ContractKind.Ferry,
            "KJFK",
            "KBOS",
            "aircraft-b",
            "contract:fixture:aircraft-v1",
            RouteDistanceNauticalMiles: 300,
            PayloadPounds: 250));
        var viewModel = new DispatchViewModel(
            Inventory(
                Item("aircraft-a", "Alpha"),
                Item("aircraft-b", "Bravo")),
            Profiles(),
            planning,
            accepted);

        await InitializeReadyAsync(viewModel);
        await viewModel.EvaluateAsync(ValidInput());

        Assert.Equal("aircraft-b", viewModel.SelectedAircraftId);
        Assert.True(viewModel.IsAcceptedContextLocked);
        Assert.Equal("KJFK", viewModel.AcceptedOriginIcao);
        Assert.Equal("KBOS", viewModel.AcceptedDestinationIcao);
        Assert.Equal("300", viewModel.AcceptedRouteDistanceText);
        Assert.Equal("250", viewModel.AcceptedPayloadPoundsText);
        Assert.Equal("contract:fixture:aircraft-v1", planning.ReservationId);

        await viewModel.SetSelectedAircraftAsync("aircraft-a");
        viewModel.SetSelectedCruiseProfile(0);
        await viewModel.EvaluateAsync(ValidInput());
        Assert.Null(planning.ReservationId);
    }

    [Theory]
    [InlineData("KTEB", "KBOS", "250", "300")]
    [InlineData("KJFK", "KTEB", "250", "300")]
    [InlineData("KJFK", "KBOS", "249", "300")]
    [InlineData("KJFK", "KBOS", "250", "299")]
    public async Task AcceptedReservationRejectsAlteredImmutableTerms(
        string origin,
        string destination,
        string payload,
        string distance)
    {
        var planning = new StubPlanning();
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            planning,
            new StubAcceptedContext(new(
                AcceptedDispatchPlanningContextStatus.Ready,
                "Accepted job loaded.",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                OpenCareer.Domain.Careers.ContractKind.Ferry,
                "KJFK",
                "KBOS",
                "aircraft-a",
                "contract:fixture:aircraft-v1",
                RouteDistanceNauticalMiles: 300,
                PayloadPounds: 250)));
        await InitializeReadyAsync(viewModel);

        await viewModel.EvaluateAsync(
            ValidInput() with
            {
                OriginIcao = origin,
                DestinationIcao = destination,
                PayloadPounds = payload,
                TotalRouteDistanceNauticalMiles = distance
            });

        Assert.Equal(0, planning.CallCount);
        Assert.Equal("ACCEPTED PLAN MISMATCH", viewModel.ResultStatus);
        Assert.Contains("immutable accepted contract", viewModel.ResultDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyAcceptedContractCannotUseReservationWithoutPlanningTerms()
    {
        var planning = new StubPlanning();
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            planning,
            new StubAcceptedContext(new(
                AcceptedDispatchPlanningContextStatus.Ready,
                "Accepted legacy job loaded.",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                OpenCareer.Domain.Careers.ContractKind.Ferry,
                "KJFK",
                "KBOS",
                "aircraft-a",
                "contract:fixture:aircraft-v1")));
        await InitializeReadyAsync(viewModel);

        await viewModel.EvaluateAsync(ValidInput());

        Assert.Equal(0, planning.CallCount);
        Assert.True(viewModel.IsAcceptedContextLocked);
        Assert.Equal("ACCEPTED PLAN UNAVAILABLE", viewModel.ResultStatus);
        Assert.Contains("no complete authoritative", viewModel.ResultDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitWeatherLimitsReachCompositePlannerWithoutDefaults()
    {
        var planning = new StubPlanning();
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            planning,
            NoAcceptedContext());
        await InitializeReadyAsync(viewModel);

        await viewModel.EvaluateAsync(
            ValidInput() with
            {
                ApplyWeatherLimits = true,
                MaximumCrosswindKnots = "17",
                MaximumTailwindKnots = "6",
                MaximumDensityAltitudeFeet = "8500",
                MinimumVisibilityStatuteMiles = "3",
                MinimumCeilingFeetAgl = "1200"
            });

        DispatchWeatherLimits limits = Assert.IsType<DispatchWeatherLimits>(
            planning.Requirements!.WeatherLimits);
        Assert.Equal(17, limits.MaximumCrosswindKnots);
        Assert.Equal(6, limits.MaximumTailwindKnots);
        Assert.Equal(8500, limits.MaximumDensityAltitudeFeet);
        Assert.Equal(3, limits.MinimumVisibilityStatuteMiles);
        Assert.Equal(1200, limits.MinimumCeilingFeetAgl);
    }

    [Fact]
    public async Task DisabledWeatherLimitsIgnoreBlankFields()
    {
        var planning = new StubPlanning();
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            planning,
            NoAcceptedContext());
        await InitializeReadyAsync(viewModel);

        await viewModel.EvaluateAsync(ValidInput());

        Assert.Null(planning.Requirements!.WeatherLimits);
    }

    [Fact]
    public async Task EnabledWeatherLimitsRequireExplicitWindLimits()
    {
        var planning = new StubPlanning();
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            planning,
            NoAcceptedContext());
        await InitializeReadyAsync(viewModel);

        await viewModel.EvaluateAsync(
            ValidInput() with { ApplyWeatherLimits = true });

        Assert.Equal("INVALID INPUT", viewModel.ResultStatus);
        Assert.Equal(0, planning.CallCount);
        Assert.Contains("Maximum crosswind", viewModel.ResultDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WeatherFailureProjectsObservedAndLimitEvidence()
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
                    "18",
                    "27",
                    [
                        new(
                            DispatchFeasibilityReason.CrosswindExceedsLimit,
                            DispatchEndpoint.Origin,
                            "KJFK",
                            "18",
                            ObservedKnots: 22,
                            LimitKnots: 17)
                    ]))
        };
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            planning,
            NoAcceptedContext());
        await InitializeReadyAsync(viewModel);

        await viewModel.EvaluateAsync(
            ValidInput() with
            {
                ApplyWeatherLimits = true,
                MaximumCrosswindKnots = "17",
                MaximumTailwindKnots = "6"
            });

        DispatchIssueViewModel issue = Assert.Single(viewModel.Issues);
        Assert.Equal("Crosswind Exceeds Limit", issue.Reason);
        Assert.Contains("observed 22.0 kt", issue.Detail, StringComparison.Ordinal);
        Assert.Contains("limit 17.0 kt", issue.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreparePersistsExactAcceptedPlanAndProjectsSummary()
    {
        Guid contractId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var planning = new StubPlanning();
        var preparation = new StubPreparation(
            PrepareDispatchPlanStatus.Prepared,
            "The exact feasible dispatch plan was persisted. Flight start remains a separate authority.",
            PreparedPlan(contractId));
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            planning,
            ReadyAcceptedContext(contractId),
            preparation);
        var navigationTargets = new List<DispatchNavigationTarget>();
        viewModel.NavigationRequested += (_, args) =>
            navigationTargets.Add(args.Target);
        await InitializeReadyAsync(viewModel);

        Assert.True(viewModel.CanPrepare);
        await viewModel.PrepareAsync(ValidInput());

        Assert.Equal(1, preparation.CallCount);
        Assert.Equal(0, planning.CallCount);
        Assert.Equal(contractId, preparation.Request!.ContractId);
        Assert.Equal("aircraft-a", preparation.Request.CanonicalAircraftId);
        Assert.Equal("contract:fixture:aircraft-v1", preparation.Request.ReservationId);
        Assert.Equal("KJFK", preparation.Request.OriginIcao);
        Assert.Equal("KBOS", preparation.Request.DestinationIcao);
        Assert.Equal(250, preparation.Request.Requirements.PayloadPounds);
        Assert.Equal("PREPARED", viewModel.PreparationStatus);
        Assert.Contains("30.0 gal", viewModel.PreparationSummary, StringComparison.Ordinal);
        Assert.Contains("04L / 22R", viewModel.PreparationSummary, StringComparison.Ordinal);
        Assert.Equal(
            new[] { DispatchNavigationTarget.Jobs },
            navigationTargets);
    }

    [Fact]
    public async Task AlreadyPreparedRetryProjectsIdempotentStatus()
    {
        Guid contractId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var preparation = new StubPreparation(
            PrepareDispatchPlanStatus.AlreadyPrepared,
            "The exact feasible dispatch plan was already prepared.",
            PreparedPlan(contractId));
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            new StubPlanning(),
            ReadyAcceptedContext(contractId),
            preparation);
        var navigationTargets = new List<DispatchNavigationTarget>();
        viewModel.NavigationRequested += (_, args) =>
            navigationTargets.Add(args.Target);
        await InitializeReadyAsync(viewModel);

        await viewModel.PrepareAsync(ValidInput());

        Assert.Equal("ALREADY PREPARED", viewModel.PreparationStatus);
        Assert.Contains("already prepared", viewModel.PreparationDetail, StringComparison.Ordinal);
        Assert.Equal(
            new[] { DispatchNavigationTarget.Jobs },
            navigationTargets);
    }

    [Fact]
    public async Task UnsuccessfulPreparationStaysOnDispatch()
    {
        Guid contractId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var preparation = new StubPreparation(
            PrepareDispatchPlanStatus.PlanningInfeasible,
            "Current evidence is infeasible.");
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            new StubPlanning(),
            ReadyAcceptedContext(contractId),
            preparation);
        var navigationTargets = new List<DispatchNavigationTarget>();
        viewModel.NavigationRequested += (_, args) =>
            navigationTargets.Add(args.Target);
        await InitializeReadyAsync(viewModel);

        await viewModel.PrepareAsync(ValidInput());

        Assert.Equal("PLANNING INFEASIBLE", viewModel.PreparationStatus);
        Assert.Empty(navigationTargets);
    }

    [Fact]
    public async Task LegacyAcceptedContextCannotPrepare()
    {
        var preparation = new StubPreparation(
            PrepareDispatchPlanStatus.Prepared,
            "Should not be reached.");
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            new StubPlanning(),
            new StubAcceptedContext(new(
                AcceptedDispatchPlanningContextStatus.Ready,
                "Accepted legacy job loaded.",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                OpenCareer.Domain.Careers.ContractKind.Ferry,
                "KJFK",
                "KBOS",
                "aircraft-a",
                "contract:fixture:aircraft-v1")),
            preparation);
        await InitializeReadyAsync(viewModel);

        Assert.False(viewModel.CanPrepare);
        await viewModel.PrepareAsync(ValidInput());

        Assert.Equal(0, preparation.CallCount);
        Assert.Equal("ACCEPTED PLAN UNAVAILABLE", viewModel.PreparationStatus);
    }

    [Fact]
    public async Task DifferentAircraftCannotPrepareAcceptedPlan()
    {
        Guid contractId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var preparation = new StubPreparation(
            PrepareDispatchPlanStatus.Prepared,
            "Should not be reached.");
        var viewModel = new DispatchViewModel(
            Inventory(
                Item("aircraft-a", "Alpha"),
                Item("aircraft-b", "Bravo")),
            Profiles(),
            new StubPlanning(),
            ReadyAcceptedContext(contractId),
            preparation);
        await InitializeReadyAsync(viewModel);
        await viewModel.SetSelectedAircraftAsync("aircraft-b");
        viewModel.SetSelectedCruiseProfile(0);

        Assert.False(viewModel.CanPrepare);
        await viewModel.PrepareAsync(ValidInput());

        Assert.Equal(0, preparation.CallCount);
        Assert.Equal("AIRCRAFT MISMATCH", viewModel.PreparationStatus);
    }

    private static DispatchPlanningFormInput ValidInput() =>
        new(
            "kjfk",
            "kbos",
            "250",
            "300",
            "260",
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
            "15",
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

    private static async Task InitializeReadyAsync(DispatchViewModel viewModel)
    {
        await viewModel.InitializeAsync();
        viewModel.SetSelectedCruiseProfile(0);
        Assert.True(viewModel.CanEvaluate);
    }

    private static IInstalledAircraftInventorySource Inventory(
        params InstalledAircraftInventoryItem[] items) =>
        new StubInventory(new(
            InstalledAircraftInventoryStatus.Ready,
            $"{items.Length} installed aircraft record(s).",
            items));

    private static IAcceptedDispatchPlanningContextSource NoAcceptedContext() =>
        new StubAcceptedContext(new(
            AcceptedDispatchPlanningContextStatus.NoAcceptedContract,
            "No accepted job is waiting for dispatch preparation."));

    private static IAcceptedDispatchPlanningContextSource ReadyAcceptedContext(
        Guid contractId) =>
        new StubAcceptedContext(new(
            AcceptedDispatchPlanningContextStatus.Ready,
            "Accepted job loaded.",
            contractId,
            OpenCareer.Domain.Careers.ContractKind.Ferry,
            "KJFK",
            "KBOS",
            "aircraft-a",
            "contract:fixture:aircraft-v1",
            RouteDistanceNauticalMiles: 300,
            PayloadPounds: 250));

    private static PreparedDispatchPlan PreparedPlan(Guid contractId)
    {
        var routeFuel = new RouteFuelPlanningRequest(
            300,
            260,
            10,
            8,
            3,
            2,
            new(5, 30, 1));
        var fuel = new RouteFuelWeightPlan(0, 6, 30, 180, 50, 300);
        var requirements = fuel.ApplyTo(new(
            PayloadPounds: 250,
            RequiredRangeNauticalMiles: 300,
            RangeSafetyMarginPercent: 10,
            RunwayLengthSafetyMarginPercent: 15));

        return new(
            contractId,
            1,
            "aircraft-a",
            "contract:fixture:aircraft-v1",
            "KJFK",
            "KBOS",
            new(0, 0, 6000, 0, 12000),
            routeFuel,
            requirements,
            fuel,
            "04L",
            "22R",
            new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    }

    private static IDispatchCruiseProfileSource Profiles() =>
        new StubProfiles(new(
            DispatchCruiseProfileCatalogStatus.Ready,
            "1 authoritative profile available.",
            [new(
                0,
                0,
                "Economy · fuel type 0",
                Envelope(),
                "Profile 0 · Verified evidence")]));

    private static DispatchCruiseProfileEnvelope Envelope() =>
        new(
            MinimumWeightPounds: 4000,
            MaximumWeightPounds: 8000,
            MinimumIsaDeviationCelsius: -20,
            MaximumIsaDeviationCelsius: 20,
            MinimumPressureAltitudeFeet: 0,
            MaximumPressureAltitudeFeet: 30000);

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

    private sealed class StubAcceptedContext(AcceptedDispatchPlanningContext context)
        : IAcceptedDispatchPlanningContextSource
    {
        public Task<AcceptedDispatchPlanningContext> ReadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(context);
    }

    private sealed class StubProfiles(DispatchCruiseProfileCatalog catalog)
        : IDispatchCruiseProfileSource
    {
        public List<string> Requests { get; } = [];

        public Task<DispatchCruiseProfileCatalog> ReadAsync(
            string canonicalAircraftId,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(canonicalAircraftId);
            return Task.FromResult(catalog);
        }
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
        public string? ReservationId { get; private set; }

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
            ReservationId = null;
            return Task.FromResult(Result);
        }

        public Task<CompositeDispatchPlanningResult> EvaluateAsync(
            string aircraftId,
            string originIcao,
            string destinationIcao,
            CruisePerformancePlanningRequest cruiseRequest,
            RouteFuelPlanningRequest routeFuelRequest,
            OperationDispatchRequirements requirements,
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            AircraftId = aircraftId;
            OriginIcao = originIcao;
            DestinationIcao = destinationIcao;
            Cruise = cruiseRequest;
            RouteFuel = routeFuelRequest;
            Requirements = requirements;
            ReservationId = reservationId;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubPreparation(
        PrepareDispatchPlanStatus status,
        string detail,
        PreparedDispatchPlan? plan = null)
        : IPreparedDispatchPlanService
    {
        public int CallCount { get; private set; }
        public PrepareDispatchPlanRequest? Request { get; private set; }

        public Task<PrepareDispatchPlanResult> PrepareAsync(
            PrepareDispatchPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Request = request;
            return Task.FromResult(new PrepareDispatchPlanResult(
                status,
                detail,
                plan));
        }
    }
}
