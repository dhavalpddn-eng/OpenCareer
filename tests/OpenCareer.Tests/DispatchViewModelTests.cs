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
    public async Task MultipleAcceptedContractsCanAllBeCancelledFromBlockedDispatchState()
    {
        Guid first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var cancellation = new StubAcceptedJobCancellation();
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            new StubPlanning(),
            new StubAcceptedContext(new(
                AcceptedDispatchPlanningContextStatus.MultipleAcceptedContracts,
                "2 accepted jobs are blocking Dispatch.",
                AcceptedContractIds: [first, second])),
            cancellation: cancellation);
        DispatchNavigationTarget? navigationTarget = null;
        viewModel.NavigationRequested += (_, args) => navigationTarget = args.Target;

        await viewModel.InitializeAsync();

        Assert.True(viewModel.CanCancelAcceptedJob);
        Assert.Equal("Cancel All 2 Accepted Jobs", viewModel.CancelAcceptedJobButtonText);

        await viewModel.CancelAcceptedJobAsync();

        Assert.Equal(new[] { first, second }, cancellation.ContractIds);
        Assert.False(viewModel.CanCancelAcceptedJob);
        Assert.Equal(DispatchNavigationTarget.Jobs, navigationTarget);
        Assert.Contains("All 2 accepted jobs", viewModel.AcceptedContextText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PersistedTerminalCleanupIsVisibleAfterDispatchInitialization()
    {
        Guid contractId =
            Guid.Parse("33333333-3333-3333-3333-333333333333");
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            new StubPlanning(),
            new StubAcceptedContext(new(
                AcceptedDispatchPlanningContextStatus.CancellationCleanupPending,
                "A cancelled job still has preflight cleanup pending.",
                ContractId: contractId,
                AcceptedContractIds: [contractId])),
            cancellation: new StubAcceptedJobCancellation());

        await viewModel.InitializeAsync();

        Assert.True(viewModel.CanCancelAcceptedJob);
        Assert.False(viewModel.CanPrepare);
        Assert.Equal("Retry Job Cleanup", viewModel.CancelAcceptedJobButtonText);
        Assert.Contains(
            "cancelled job",
            viewModel.CancelAcceptedJobDialogText,
            StringComparison.Ordinal);
        Assert.Contains(
            "cleanup pending",
            viewModel.AcceptedContextText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedCancellationRefreshesRemainingTargetsAndStaysRetryableWithoutTechnicalDetails()
    {
        Guid first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var acceptedContext = new SequenceAcceptedContext(
            new(
                AcceptedDispatchPlanningContextStatus.MultipleAcceptedContracts,
                "2 accepted jobs are blocking Dispatch.",
                AcceptedContractIds: [first, second]),
            new(
                AcceptedDispatchPlanningContextStatus.ReservationUnavailable,
                "1 accepted job remains available for cancellation.",
                ContractId: second,
                AcceptedContractIds: [second]));
        var cancellation = new PartialFailureAcceptedJobCancellation(
            second,
            "sqlite path C:\\Users\\pilot\\OpenCareer.db");
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            new StubPlanning(),
            acceptedContext,
            cancellation: cancellation);

        await viewModel.InitializeAsync();
        await viewModel.CancelAcceptedJobAsync();

        Assert.Equal(new[] { first, second }, cancellation.ContractIds);
        Assert.Equal(2, acceptedContext.ReadCount);
        Assert.True(viewModel.CanCancelAcceptedJob);
        Assert.Equal("Cancel Accepted Job", viewModel.CancelAcceptedJobButtonText);
        Assert.Contains("Retry the remaining cancellation", viewModel.AcceptedContextText, StringComparison.Ordinal);
        Assert.Contains("diagnostic bundle", viewModel.AcceptedContextText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlite", viewModel.AcceptedContextText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenCareer.db", viewModel.AcceptedContextText, StringComparison.Ordinal);

        await viewModel.CancelAcceptedJobAsync();

        Assert.Equal(new[] { first, second, second }, cancellation.ContractIds);
        Assert.False(viewModel.CanCancelAcceptedJob);
        Assert.Contains("Accepted job cancelled", viewModel.AcceptedContextText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TerminalCleanupFailureRetainsExactDispatchRetryWithoutContextRefresh()
    {
        Guid first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var acceptedContext = new SequenceAcceptedContext(
            new(
                AcceptedDispatchPlanningContextStatus.MultipleAcceptedContracts,
                "2 accepted jobs are blocking Dispatch.",
                AcceptedContractIds: [first, second]),
            new(
                AcceptedDispatchPlanningContextStatus.NoAcceptedContract,
                "The terminal contracts are no longer listed as accepted."));
        var cancellation =
            new RetryableCleanupFailureAcceptedJobCancellation(second);
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            new StubPlanning(),
            acceptedContext,
            cancellation: cancellation);

        await viewModel.InitializeAsync();
        await viewModel.CancelAcceptedJobAsync();

        Assert.Equal(1, acceptedContext.ReadCount);
        Assert.Equal(new[] { first, second }, cancellation.ContractIds);
        Assert.True(viewModel.CanCancelAcceptedJob);
        Assert.Equal("Retry Job Cleanup", viewModel.CancelAcceptedJobButtonText);
        Assert.Contains("Retry the remaining cancellation", viewModel.AcceptedContextText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            RetryableCleanupFailureAcceptedJobCancellation.TechnicalMessage,
            viewModel.AcceptedContextText,
            StringComparison.Ordinal);

        await viewModel.CancelAcceptedJobAsync();

        Assert.Equal(new[] { first, second, second }, cancellation.ContractIds);
        Assert.False(viewModel.CanCancelAcceptedJob);
        Assert.Contains("Accepted job cancelled", viewModel.AcceptedContextText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedInitializationKeepsRefreshedAcceptedCancellationButBlocksStalePlanning()
    {
        Guid contractId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inventory = new ControllableInventory(new(
            InstalledAircraftInventoryStatus.Ready,
            "1 installed aircraft record(s).",
            [Item("aircraft-a", "Alpha")]));
        var accepted = new ControllableAcceptedContext(
            ReadyAcceptedContextValue(contractId));
        var planning = new StubPlanning();
        var preparation = new StubPreparation(
            PrepareDispatchPlanStatus.Prepared,
            "Should not be reached.");
        var cancellation = new StubAcceptedJobCancellation();
        var viewModel = new DispatchViewModel(
            inventory,
            Profiles(),
            planning,
            accepted,
            preparation,
            cancellation);

        await InitializeReadyAsync(viewModel);
        Assert.True(viewModel.CanPrepare);
        Assert.True(viewModel.CanCancelAcceptedJob);

        inventory.NextReadFailure = new IOException("Sensitive inventory path C:\\Users\\pilot\\Packages.");
        IOException failure = await Assert.ThrowsAsync<IOException>(
            () => viewModel.InitializeAsync());
        await viewModel.ReportInitializationFailureAsync(failure);

        Assert.False(viewModel.CanEvaluate);
        Assert.False(viewModel.CanPrepare);
        Assert.True(viewModel.CanCancelAcceptedJob);
        Assert.Empty(viewModel.AircraftOptions);
        Assert.Contains("cleanup was refreshed", viewModel.AcceptedContextText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagnostic bundle", viewModel.AcceptedContextText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Packages", viewModel.AcceptedContextText, StringComparison.Ordinal);

        await viewModel.EvaluateAsync(ValidInput());
        await viewModel.PrepareAsync(ValidInput());

        Assert.Equal(0, planning.CallCount);
        Assert.Equal(0, preparation.CallCount);
        Assert.Equal("AUTHORITY UNAVAILABLE", viewModel.ResultStatus);
        Assert.Equal("AUTHORITY UNAVAILABLE", viewModel.PreparationStatus);
    }

    [Fact]
    public async Task FailedInitializationAndAcceptedContextRecoveryUseJobsGuidanceWithoutStaleTargets()
    {
        Guid contractId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var inventory = new ControllableInventory(new(
            InstalledAircraftInventoryStatus.Ready,
            "1 installed aircraft record(s).",
            [Item("aircraft-a", "Alpha")]));
        var accepted = new ControllableAcceptedContext(
            ReadyAcceptedContextValue(contractId));
        var viewModel = new DispatchViewModel(
            inventory,
            Profiles(),
            new StubPlanning(),
            accepted,
            cancellation: new StubAcceptedJobCancellation());

        await viewModel.InitializeAsync();
        Assert.True(viewModel.CanCancelAcceptedJob);

        inventory.NextReadFailure = new IOException("Sensitive inventory failure.");
        accepted.NextReadFailure = new IOException("Sensitive contract database failure.");
        IOException failure = await Assert.ThrowsAsync<IOException>(
            () => viewModel.InitializeAsync());
        await viewModel.ReportInitializationFailureAsync(failure);

        Assert.False(viewModel.CanCancelAcceptedJob);
        Assert.Contains("use Jobs", viewModel.AcceptedContextText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagnostic bundle", viewModel.AcceptedContextText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sensitive inventory failure", viewModel.AcceptedContextText, StringComparison.Ordinal);
        Assert.DoesNotContain("Sensitive contract database failure", viewModel.AcceptedContextText, StringComparison.Ordinal);
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
    public async Task PreparationFailureKeepsAcceptedJobRecoverableAndRetryableWithoutTechnicalDetails()
    {
        Guid contractId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var accepted = new ControllableAcceptedContext(
            ReadyAcceptedContextValue(contractId));
        var preparation = new FailOncePreparation(
            new IOException("Sensitive sqlite path C:\\Users\\pilot\\OpenCareer.db"),
            new(
                PrepareDispatchPlanStatus.AlreadyPrepared,
                "The exact feasible dispatch plan was already prepared.",
                PreparedPlan(contractId)));
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            new StubPlanning(),
            accepted,
            preparation,
            new StubAcceptedJobCancellation());
        var navigationTargets = new List<DispatchNavigationTarget>();
        viewModel.NavigationRequested += (_, args) =>
            navigationTargets.Add(args.Target);
        await InitializeReadyAsync(viewModel);

        await viewModel.PrepareAsync(ValidInput());

        Assert.Equal(2, accepted.ReadCount);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanPrepare);
        Assert.True(viewModel.CanCancelAcceptedJob);
        Assert.Equal("PREPARATION NOT CONFIRMED", viewModel.PreparationStatus);
        Assert.Contains("Retry Prepare Plan", viewModel.PreparationDetail, StringComparison.Ordinal);
        Assert.Contains("cancel the accepted job", viewModel.PreparationDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagnostic bundle", viewModel.PreparationDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlite", viewModel.PreparationDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenCareer.db", viewModel.PreparationDetail, StringComparison.Ordinal);
        Assert.Empty(navigationTargets);

        await viewModel.PrepareAsync(ValidInput());

        Assert.Equal(2, preparation.CallCount);
        Assert.Equal("ALREADY PREPARED", viewModel.PreparationStatus);
        Assert.Equal(
            new[] { DispatchNavigationTarget.Jobs },
            navigationTargets);
    }

    [Fact]
    public async Task PreparationFailureAndContextRecoveryFailureClearStaleAcceptedJobActions()
    {
        Guid contractId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var accepted = new ControllableAcceptedContext(
            ReadyAcceptedContextValue(contractId));
        var preparation = new FailOncePreparation(
            new IOException("Sensitive preparation path C:\\Users\\pilot\\plan.json"),
            new(
                PrepareDispatchPlanStatus.AlreadyPrepared,
                "Should not be reached.",
                PreparedPlan(contractId)));
        var viewModel = new DispatchViewModel(
            Inventory(Item("aircraft-a", "Alpha")),
            Profiles(),
            new StubPlanning(),
            accepted,
            preparation,
            new StubAcceptedJobCancellation());
        await InitializeReadyAsync(viewModel);
        accepted.NextReadFailure =
            new IOException("Sensitive recovery path C:\\Users\\pilot\\context.json");

        await viewModel.PrepareAsync(ValidInput());

        Assert.Equal(2, accepted.ReadCount);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.CanPrepare);
        Assert.False(viewModel.CanCancelAcceptedJob);
        Assert.Equal("PREPARATION NOT CONFIRMED", viewModel.PreparationStatus);
        Assert.Contains("Reopen Dispatch", viewModel.PreparationDetail, StringComparison.Ordinal);
        Assert.Contains("Jobs", viewModel.PreparationDetail, StringComparison.Ordinal);
        Assert.Contains("diagnostic bundle", viewModel.PreparationDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plan.json", viewModel.PreparationDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("context.json", viewModel.PreparationDetail, StringComparison.Ordinal);
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
        new StubAcceptedContext(ReadyAcceptedContextValue(contractId));

    private static AcceptedDispatchPlanningContext ReadyAcceptedContextValue(
        Guid contractId) =>
        new(
            AcceptedDispatchPlanningContextStatus.Ready,
            "Accepted job loaded.",
            contractId,
            OpenCareer.Domain.Careers.ContractKind.Ferry,
            "KJFK",
            "KBOS",
            "aircraft-a",
            "contract:fixture:aircraft-v1",
            RouteDistanceNauticalMiles: 300,
            PayloadPounds: 250);

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
            "ICAO family unknown",
            "Powerplant class unknown",
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

    private sealed class ControllableInventory(InstalledAircraftInventorySnapshot snapshot)
        : IInstalledAircraftInventorySource
    {
        public Exception? NextReadFailure { get; set; }

        public Task<InstalledAircraftInventorySnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NextReadFailure is { } failure)
            {
                NextReadFailure = null;
                return Task.FromException<InstalledAircraftInventorySnapshot>(failure);
            }

            return Task.FromResult(snapshot);
        }
    }

    private sealed class ControllableAcceptedContext(AcceptedDispatchPlanningContext context)
        : IAcceptedDispatchPlanningContextSource
    {
        public Exception? NextReadFailure { get; set; }
        public int ReadCount { get; private set; }

        public Task<AcceptedDispatchPlanningContext> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (NextReadFailure is { } failure)
            {
                NextReadFailure = null;
                return Task.FromException<AcceptedDispatchPlanningContext>(failure);
            }

            return Task.FromResult(context);
        }
    }

    private sealed class SequenceAcceptedContext(
        params AcceptedDispatchPlanningContext[] contexts)
        : IAcceptedDispatchPlanningContextSource
    {
        public int ReadCount { get; private set; }

        public Task<AcceptedDispatchPlanningContext> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = Math.Min(ReadCount, contexts.Length - 1);
            ReadCount++;
            return Task.FromResult(contexts[index]);
        }
    }

    private sealed class StubAcceptedJobCancellation : IAcceptedJobCancellationService
    {
        public List<Guid> ContractIds { get; } = [];

        public Task<AcceptedJobCancellationResult> CancelAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ContractIds.Add(contractId);
            return Task.FromResult(new AcceptedJobCancellationResult(
                contractId,
                JobAcceptanceFleetBridge.GetReservationId(contractId),
                ReservationWasAlreadyReleased: false));
        }
    }

    private sealed class PartialFailureAcceptedJobCancellation(
        Guid failingContractId,
        string failureMessage)
        : IAcceptedJobCancellationService
    {
        private bool _failureRaised;

        public List<Guid> ContractIds { get; } = [];

        public Task<AcceptedJobCancellationResult> CancelAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ContractIds.Add(contractId);
            if (contractId == failingContractId && !_failureRaised)
            {
                _failureRaised = true;
                throw new InvalidOperationException(failureMessage);
            }

            return Task.FromResult(new AcceptedJobCancellationResult(
                contractId,
                JobAcceptanceFleetBridge.GetReservationId(contractId),
                ReservationWasAlreadyReleased: false));
        }
    }

    private sealed class RetryableCleanupFailureAcceptedJobCancellation(
        Guid failingContractId)
        : IAcceptedJobCancellationService
    {
        public const string TechnicalMessage =
            "INTERNAL terminal cleanup details";

        private bool _failureRaised;

        public List<Guid> ContractIds { get; } = [];

        public Task<AcceptedJobCancellationResult> CancelAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ContractIds.Add(contractId);
            if (contractId == failingContractId && !_failureRaised)
            {
                _failureRaised = true;
                throw new AcceptedJobCancellationCleanupException(
                    contractId,
                    new IOException(TechnicalMessage));
            }

            return Task.FromResult(new AcceptedJobCancellationResult(
                contractId,
                JobAcceptanceFleetBridge.GetReservationId(contractId),
                ReservationWasAlreadyReleased: false));
        }
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

    private sealed class FailOncePreparation(
        Exception failure,
        PrepareDispatchPlanResult retryResult)
        : IPreparedDispatchPlanService
    {
        public int CallCount { get; private set; }

        public Task<PrepareDispatchPlanResult> PrepareAsync(
            PrepareDispatchPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return CallCount == 1
                ? Task.FromException<PrepareDispatchPlanResult>(failure)
                : Task.FromResult(retryResult);
        }
    }
}
