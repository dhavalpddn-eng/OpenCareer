using System.Collections.Immutable;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class JobsViewModelTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 22, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefreshShowsOnlyCurrentCareerAirportBoard()
    {
        var boards =
            new FakeBoardStore(
                Board(
                    "KRME",
                    Offer(
                        "KRME",
                        "KSYR",
                        locked:
                            false)),
                Board(
                    "KDFW",
                    Offer(
                        "KDFW",
                        "KAUS",
                        locked:
                            false)));

        var viewModel =
            new JobsViewModel(
                boards,
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now));

        await viewModel.RefreshAsync();

        JobOfferItemViewModel offer =
            Assert.Single(
                viewModel.Offers);

        Assert.Equal(
            "KRME → KSYR",
            offer.Route);
        Assert.Equal(
            "LOCAL BOARD · KRME",
            viewModel.AirportText);
        Assert.True(
            offer.IsActive);
    }

    [Fact]
    public void OfferCardShowsAuthoritativePayAndProgressionBeforeAcceptance()
    {
        JobMarketOfferDraft offer =
            Offer(
                "KRME",
                "KSYR",
                locked: false) with
            {
                ContractTerms =
                    new JobMarketContractTermsEnvelope(
                        "test:paid-terms:v1",
                        new AircraftMissionRequirements(
                            AllowedAccess: AircraftAccess.Civilian,
                            MinimumRangeNauticalMiles: 100),
                        EstimatedFlightHours: 1,
                        PayloadPounds: 0,
                        DemandAttractiveness: 1,
                        Urgency: 0,
                        Difficulty: 0,
                        ReputationReward: 1)
            };

        var projected =
            new JobOfferItemViewModel(
                offer,
                Now);

        Assert.StartsWith(
            "PILOT PAY $",
            projected.CompensationText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "$0",
            projected.CompensationText,
            StringComparison.Ordinal);
        Assert.Equal(
            "+1 reputation on success",
            projected.ProgressionText);
    }

    [Fact]
    public void CalibrationCardCannotBeMistakenForPaidCareerWork()
    {
        JobMarketOfferDraft offer =
            DevelopmentFlight.CreateOffer(
                Guid.NewGuid(),
                Now);

        var projected =
            new JobOfferItemViewModel(
                offer,
                Now);

        Assert.Equal(
            "$0 CALIBRATION PAY",
            projected.CompensationText);
        Assert.Equal(
            "$0 PROGRESSION",
            projected.ProgressionText);
    }

    [Fact]
    public void PassengerOfferCardShowsImmutableCountAndPayloadTerms()
    {
        JobMarketOfferDraft offer =
            Offer(
                "KRME",
                "KSYR",
                locked:
                    false) with
            {
                Kind = ContractKind.Passenger,
                ContractTerms = Terms(
                    new AircraftMissionRequirements(
                        RequiredCapabilities:
                            AircraftCapability.Passenger,
                        MinimumPayloadPounds:
                            510,
                        MinimumRangeNauticalMiles:
                            100,
                        MinimumSeats:
                            4,
                        MinimumPassengerCapacity:
                            3),
                    payloadPounds:
                        510)
            };

        var projected =
            new JobOfferItemViewModel(
                offer,
                Now);

        Assert.Equal(
            "MANIFEST · 3 passengers · 510 lb incl. baggage",
            projected.AssignmentText);
    }

    [Fact]
    public void NamedCargoOfferCardShowsAuthoritativeCommodityAndLotMass()
    {
        var lot =
            new AcceptedCargoLot(
                LotId:
                    Guid.Parse(
                    "c2000000-0000-0000-0000-000000000001"),
                CommodityId:
                    "coffee.roasted",
                CommodityName:
                    "Roasted Coffee",
                Category:
                    CargoCommodityCategory.GeneralFreight,
                Quantity:
                    12m,
                Unit:
                    CargoUnitOfTrade.Case,
                MassPounds:
                    840,
                VolumeCubicFeet:
                    96,
                OriginIcao:
                    "KRME",
                DestinationIcao:
                    "KSYR",
                Shipper:
                    "Mohawk Valley Roasters",
                Consignee:
                    "Syracuse Hospitality Supply",
                DeclaredValue:
                    7200m,
                OriginMarketUnitValue:
                    500m,
                DestinationMarketUnitValue:
                    650m,
                HandlingRequirements:
                    CargoHandlingRequirement.None);
        JobMarketOfferDraft offer =
            Offer(
                "KRME",
                "KSYR",
                locked:
                    false) with
            {
                Kind = ContractKind.Cargo,
                ContractTerms = Terms(
                    new AircraftMissionRequirements(
                        RequiredCapabilities:
                            AircraftCapability.Cargo,
                        MinimumPayloadPounds:
                            840,
                        MinimumRangeNauticalMiles:
                            100),
                    payloadPounds:
                        840) with
                {
                    CargoLots =
                        ImmutableArray.Create(
                            lot)
                }
            };

        var projected =
            new JobOfferItemViewModel(
                offer,
                Now);

        Assert.Equal(
            "CARGO · Roasted Coffee · 840 lb",
            projected.AssignmentText);
    }

    [Fact]
    public void LegacyCargoOfferCardDoesNotInventACommodity()
    {
        JobMarketOfferDraft offer =
            Offer(
                "KRME",
                "KSYR",
                locked:
                    false) with
            {
                Kind = ContractKind.Cargo,
                ContractTerms = Terms(
                    new AircraftMissionRequirements(
                        RequiredCapabilities:
                            AircraftCapability.Cargo,
                        MinimumPayloadPounds:
                            840,
                        MinimumRangeNauticalMiles:
                            100),
                    payloadPounds:
                        840)
            };

        var projected =
            new JobOfferItemViewModel(
                offer,
                Now);

        Assert.Equal(
            "CARGO · 840 lb · commodity not specified",
            projected.AssignmentText);
    }

    [Fact]
    public async Task LockedAndExpiredOffersRemainVisibleButNotActive()
    {
        var expired =
            Offer(
                "KRME",
                "KSYR",
                locked:
                    false) with
            {
                ExpiresAt =
                    Now.AddMinutes(-1)
            };

        var locked =
            Offer(
                "KRME",
                "KALB",
                locked:
                    true);

        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(
                    Board(
                        "KRME",
                        expired,
                        locked)),
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now));

        await viewModel.RefreshAsync();

        Assert.Equal(
            2,
            viewModel.Offers.Count);
        Assert.Contains(
            viewModel.Offers,
            static offer =>
                offer.IsExpired);
        Assert.Contains(
            viewModel.Offers,
            static offer =>
                offer.IsLockedPreview);
        Assert.DoesNotContain(
            viewModel.Offers,
            static offer =>
                offer.IsActive);
    }

    [Fact]
    public async Task MissingPersistedBoardReportsEmptyState()
    {
        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(),
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now));

        await viewModel.RefreshAsync();

        Assert.Empty(
            viewModel.Offers);
        Assert.Contains(
            "No persisted job board",
            viewModel.StatusText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnboardingRequiredDoesNotInventLocalBoard()
    {
        var store =
            new FakeProfileStore(
                record:
                    null);

        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(
                    Board(
                        "KRME",
                        Offer(
                            "KRME",
                            "KSYR",
                            locked:
                                false))),
                new PlayerCareerRuntimeState(
                    store),
                new FixedTimeProvider(Now));

        await viewModel.RefreshAsync();

        Assert.Empty(
            viewModel.Offers);
        Assert.Contains(
            "onboarding",
            viewModel.AirportText,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectedAircraftEnablesVerifiedStartAndDelegatesOnce()
    {
        JobMarketOfferDraft offer =
            Offer(
                "KRME",
                "KSYR",
                locked:
                    false);

        var action =
            new FakeStartAction();

        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(
                    Board(
                        "KRME",
                        offer)),
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now),
                new CareerJobAircraftSelectionSource(
                    new FakeDiscovery()),
                action,
                logger:
                    null);

        await viewModel.RefreshAsync();

        CareerJobAircraftOption aircraft =
            Assert.Single(
                viewModel.AircraftOptions);

        await viewModel.SelectAircraftAsync(
            aircraft.AircraftId);

        JobOfferItemViewModel projected =
            Assert.Single(
                viewModel.Offers);

        var navigationTargets = new List<JobsNavigationTarget>();
        viewModel.NavigationRequested += (_, args) =>
            navigationTargets.Add(args.Target);

        Assert.True(
            projected.CanStart);

        await viewModel.StartOfferAsync(
            projected.OfferId);

        Assert.Equal(
            1,
            action.StartCount);
        Assert.Equal(
            projected.OfferId,
            action.LastOfferId);
        Assert.Equal(
            aircraft.AircraftId,
            action.LastAircraftId);
        Assert.Empty(navigationTargets);
    }

    [Fact]
    public async Task FirstTimeOfferAutomaticallyPreparesAndStartsFlight()
    {
        JobMarketOfferDraft offer = Offer("KRME", "KSYR", locked: false);
        var action = new FakeStartAction
        {
            ActionKind = CareerJobActionKind.AcceptForDispatch,
            CompleteStart = true
        };
        var viewModel = new JobsViewModel(
            new FakeBoardStore(Board("KRME", offer)),
            CareerRuntime("KRME"),
            new FixedTimeProvider(Now),
            new CareerJobAircraftSelectionSource(new FakeDiscovery()),
            action,
            logger: null);
        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync(
            Assert.Single(viewModel.AircraftOptions).AircraftId);

        var navigationTargets = new List<JobsNavigationTarget>();
        viewModel.NavigationRequested += (_, args) =>
            navigationTargets.Add(args.Target);

        JobOfferItemViewModel projected = Assert.Single(viewModel.Offers);
        Assert.Equal("Accept & Start Flight", projected.ActionButtonText);

        await viewModel.StartOfferAsync(projected.OfferId);

        Assert.Equal(1, action.AcceptCount);
        Assert.Equal(1, action.StartCount);
        Assert.Contains("Career flight started", viewModel.AcceptanceStatus, StringComparison.Ordinal);
        Assert.Equal(
            new[] { JobsNavigationTarget.CurrentFlight },
            navigationTargets);
    }

    [Fact]
    public async Task ConcurrentStartAttemptIsRejectedInsteadOfQueued()
    {
        JobMarketOfferDraft offer = Offer("KRME", "KSYR", locked: false);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var action = new FakeStartAction
        {
            ActionKind = CareerJobActionKind.AcceptForDispatch,
            CompleteStart = true,
            BeforeStart = async () =>
            {
                entered.TrySetResult();
                await release.Task;
            }
        };
        var viewModel = new JobsViewModel(
            new FakeBoardStore(Board("KRME", offer)),
            CareerRuntime("KRME"),
            new FixedTimeProvider(Now),
            new CareerJobAircraftSelectionSource(new FakeDiscovery()),
            action,
            logger: null);

        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync(
            Assert.Single(viewModel.AircraftOptions).AircraftId);

        Task firstStart = viewModel.StartOfferAsync(offer.OfferId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(viewModel.IsJobActionBusy);
        Assert.False(viewModel.CanRunJobActions);

        Task duplicateStart = viewModel.StartOfferAsync(offer.OfferId);
        await duplicateStart.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, action.AcceptCount);
        Assert.Equal(1, action.StartCount);
        Assert.False(firstStart.IsCompleted);

        release.TrySetResult();
        await firstStart.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(viewModel.IsJobActionBusy);
        Assert.True(viewModel.CanRunJobActions);
        Assert.Equal(1, action.AcceptCount);
        Assert.Equal(1, action.StartCount);
    }

    [Fact]
    public async Task AircraftSelectionCannotChangeWhileStartIsInProgress()
    {
        JobMarketOfferDraft offer = Offer("KRME", "KSYR", locked: false);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var discovery = new FakeDiscovery
        {
            Current = new InstalledAircraftDiscoverySnapshot(
                InstalledAircraftDiscoveryAvailability.Available,
                [
                    Installed("first-aircraft", "First Aircraft"),
                    Installed("second-aircraft", "Second Aircraft")
                ])
        };
        var action = new FakeStartAction
        {
            CompleteStart = true,
            BeforeStart = async () =>
            {
                entered.TrySetResult();
                await release.Task;
            }
        };
        var viewModel = new JobsViewModel(
            new FakeBoardStore(Board("KRME", offer)),
            CareerRuntime("KRME"),
            new FixedTimeProvider(Now),
            new CareerJobAircraftSelectionSource(discovery),
            action,
            logger: null);

        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync("first-aircraft");

        Task start = viewModel.StartOfferAsync(offer.OfferId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await viewModel.SelectAircraftAsync("second-aircraft")
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("first-aircraft", viewModel.SelectedAircraftId);
        Assert.Equal("first-aircraft", action.LastAircraftId);
        Assert.False(start.IsCompleted);

        release.TrySetResult();
        await start.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("first-aircraft", viewModel.SelectedAircraftId);
        Assert.Equal("first-aircraft", action.LastAircraftId);
    }

    [Fact]
    public async Task FailedAcceptanceDoesNotRequestDispatchNavigation()
    {
        JobMarketOfferDraft offer = Offer("KRME", "KSYR", locked: false);
        var action = new FakeStartAction
        {
            ActionKind = CareerJobActionKind.AcceptForDispatch,
            FailAcceptance = true
        };
        var viewModel = new JobsViewModel(
            new FakeBoardStore(Board("KRME", offer)),
            CareerRuntime("KRME"),
            new FixedTimeProvider(Now),
            new CareerJobAircraftSelectionSource(new FakeDiscovery()),
            action,
            logger: null);
        var navigationTargets = new List<JobsNavigationTarget>();
        viewModel.NavigationRequested += (_, args) =>
            navigationTargets.Add(args.Target);

        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync(
            Assert.Single(viewModel.AircraftOptions).AircraftId);
        await viewModel.StartOfferAsync(Assert.Single(viewModel.Offers).OfferId);

        Assert.Equal(1, action.AcceptCount);
        Assert.Empty(navigationTargets);
        Assert.Contains(
            "did not finish",
            viewModel.AcceptanceStatus,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            FakeStartAction.AcceptanceFailureMessage,
            viewModel.AcceptanceStatus,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartFailureAfterSessionCreationOpensCurrentFlightRecovery()
    {
        JobMarketOfferDraft offer =
            Offer(
                "KRME",
                "KSYR",
                locked:
                    false);
        var sessions =
            new FlightSessionCoordinator();
        var action =
            new FakeStartAction
            {
                BeforeStart = () =>
                {
                    sessions.Start(
                        Now,
                        contractId:
                            null,
                        sessionId:
                            Guid.Parse(
                                "a7000000-0000-0000-0000-000000000003"));
                    return Task.CompletedTask;
                }
            };
        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(
                    Board(
                        "KRME",
                        offer)),
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now),
                new CareerJobAircraftSelectionSource(
                    new FakeDiscovery()),
                action,
                logger:
                    null,
                flightSessions:
                    sessions);
        var navigationTargets =
            new List<JobsNavigationTarget>();
        viewModel.NavigationRequested += (_, args) =>
            navigationTargets.Add(args.Target);

        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync(
            Assert.Single(viewModel.AircraftOptions).AircraftId);
        await viewModel.StartOfferAsync(
            Assert.Single(viewModel.Offers).OfferId);

        Assert.True(viewModel.HasCurrentFlightSession);
        Assert.False(viewModel.CanStartJobActions);
        Assert.Equal(
            "FLIGHT SESSION · ACTIVE",
            viewModel.CurrentFlightSessionStatus);
        Assert.Contains(
            "Opening Current Flight for recovery",
            viewModel.AcceptanceStatus,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            FakeStartAction.StartFailureMessage,
            viewModel.AcceptanceStatus,
            StringComparison.Ordinal);
        Assert.Equal(
            new[] { JobsNavigationTarget.CurrentFlight },
            navigationTargets);
    }

    [Fact]
    public async Task AcceptedJobsCanBeCancelledDirectlyFromJobs()
    {
        Guid firstContractId = Guid.NewGuid();
        Guid secondContractId = Guid.NewGuid();
        var acceptedContext = new MutableAcceptedContext(
            firstContractId,
            secondContractId);
        var cancellation = new RecordingAcceptedCancellation(acceptedContext);
        var recovery =
            new FakeJobRecovery(
                new CareerJobPreFlightSessionRecoveryResult(
                    CareerJobPreFlightSessionRecoveryState.MultipleAcceptedContracts,
                    ContractId:
                        null,
                    "Two accepted jobs require a player choice."),
                new CareerJobPreFlightSessionRecoveryResult(
                    CareerJobPreFlightSessionRecoveryState.NothingToRecover,
                    ContractId:
                        null,
                    "No supported job requires recovery."));
        var viewModel = new JobsViewModel(
            new FakeBoardStore(Board("KRME", Offer("KRME", "KSYR", locked: false))),
            CareerRuntime("KRME"),
            new FixedTimeProvider(Now),
            aircraftSelection: null,
            startAction: null,
            logger: null,
            startRecovery: recovery,
            acceptedContext: acceptedContext,
            acceptedCancellation: cancellation);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.CanCancelAcceptedJobs);
        Assert.True(viewModel.HasJobRecoveryAttention);
        Assert.False(viewModel.CanStartJobActions);
        Assert.Equal(
            "Cancel All 2 Accepted Jobs",
            viewModel.CancelAcceptedJobsButtonText);

        await viewModel.CancelAcceptedJobsAsync();

        Assert.Equal(
            new[] { firstContractId, secondContractId }.OrderBy(static id => id),
            cancellation.CancelledContractIds);
        Assert.False(viewModel.CanCancelAcceptedJobs);
        Assert.False(viewModel.HasJobRecoveryAttention);
        Assert.True(viewModel.CanStartJobActions);
        Assert.Equal(
            "No Accepted Job to Cancel",
            viewModel.CancelAcceptedJobsButtonText);
        Assert.Contains(
            "All 2 accepted jobs were cancelled",
            viewModel.AcceptanceStatus,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedAcceptedJobCancellationRemainsRetryableWithoutRawErrorText()
    {
        Guid contractId =
            Guid.Parse(
                "c3000000-0000-0000-0000-000000000001");
        var acceptedContext =
            new MutableAcceptedContext(
                contractId);
        var cancellation =
            new FailingAcceptedCancellation();
        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(
                    Board(
                        "KRME",
                        Offer(
                            "KRME",
                            "KSYR",
                            locked:
                                false))),
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now),
                aircraftSelection:
                    null,
                startAction:
                    null,
                logger:
                    null,
                acceptedContext:
                    acceptedContext,
                acceptedCancellation:
                    cancellation);

        await viewModel.RefreshAsync();
        await viewModel.CancelAcceptedJobsAsync();

        Assert.Equal(1, cancellation.CallCount);
        Assert.True(viewModel.CanCancelAcceptedJobs);
        Assert.Equal(
            "Cancel Accepted Job",
            viewModel.CancelAcceptedJobsButtonText);
        Assert.Contains(
            "remains available to retry",
            viewModel.AcceptanceStatus,
            StringComparison.Ordinal);
        Assert.Contains(
            "diagnostic bundle",
            viewModel.AcceptanceStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            FailingAcceptedCancellation.TechnicalMessage,
            viewModel.AcceptanceStatus,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActiveCareerFlightCanBeOpenedDirectlyFromJobs()
    {
        Guid contractId = Guid.Parse(
            "a7000000-0000-0000-0000-000000000001");
        CareerJobPlayableLoopReadinessSource readiness =
            ActiveFlightReadiness(contractId);
        var viewModel = new JobsViewModel(
            new FakeBoardStore(Board("KRME", Offer("KRME", "KSYR", locked: false))),
            CareerRuntime("KRME"),
            new FixedTimeProvider(Now),
            aircraftSelection: null,
            startAction: null,
            logger: null,
            playableReadiness: readiness);

        var navigationTargets = new List<JobsNavigationTarget>();
        viewModel.NavigationRequested += (_, args) =>
            navigationTargets.Add(args.Target);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasCurrentFlightSession);
        Assert.False(viewModel.CanStartJobActions);
        Assert.Equal("CAREER FLIGHT IN PROGRESS", viewModel.CurrentFlightSessionStatus);
        Assert.Contains("Accepted", viewModel.CurrentFlightSessionDetail, StringComparison.Ordinal);

        viewModel.OpenCurrentFlight();

        Assert.Equal(
            new[] { JobsNavigationTarget.CurrentFlight },
            navigationTargets);
    }

    [Fact]
    public async Task MissingCareerFlightDoesNotExposeOrNavigateRecoveryAction()
    {
        CareerJobPlayableLoopReadinessSource readiness =
            ActiveFlightReadiness(contractId: null);
        var viewModel = new JobsViewModel(
            new FakeBoardStore(Board("KRME", Offer("KRME", "KSYR", locked: false))),
            CareerRuntime("KRME"),
            new FixedTimeProvider(Now),
            aircraftSelection: null,
            startAction: null,
            logger: null,
            playableReadiness: readiness);

        var navigationTargets = new List<JobsNavigationTarget>();
        viewModel.NavigationRequested += (_, args) =>
            navigationTargets.Add(args.Target);

        await viewModel.RefreshAsync();
        viewModel.OpenCurrentFlight();

        Assert.False(viewModel.HasCurrentFlightSession);
        Assert.Empty(navigationTargets);
    }

    [Fact]
    public async Task WaitingJobRecoveryIsVisibleWithoutMutatingTheBoardSurface()
    {
        Guid contractId =
            Guid.Parse(
                "c1000000-0000-0000-0000-000000000071");
        var recovery =
            new FakeJobRecovery(
                new CareerJobPreFlightSessionRecoveryResult(
                    CareerJobPreFlightSessionRecoveryState.WaitingForReservation,
                    contractId,
                    "The recoverable contract does not currently own its deterministic Fleet reservation."));
        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(
                    Board(
                        "KRME",
                        Offer(
                            "KRME",
                            "KSYR",
                            locked:
                                false))),
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now),
                aircraftSelection:
                    null,
                startAction:
                    null,
                logger:
                    null,
                startRecovery:
                    recovery);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasJobRecoveryAttention);
        Assert.False(viewModel.CanStartJobActions);
        Assert.True(viewModel.CanRunJobActions);
        Assert.Equal(
            "JOB RECOVERY · RESERVATION MISSING",
            viewModel.JobRecoveryStatus);
        Assert.Contains(
            "deterministic Fleet reservation",
            viewModel.JobRecoveryDetail,
            StringComparison.Ordinal);
        Assert.Single(viewModel.Offers);
        Assert.Equal(1, recovery.CallCount);
    }

    [Fact]
    public async Task RecoveryAttentionRejectsStaleStartInvocationAtViewModelBoundary()
    {
        JobMarketOfferDraft offer =
            Offer(
                "KRME",
                "KSYR",
                locked:
                    false);
        var action =
            new FakeStartAction();
        var recovery =
            new FakeJobRecovery(
                new CareerJobPreFlightSessionRecoveryResult(
                    CareerJobPreFlightSessionRecoveryState.NothingToRecover,
                    ContractId:
                        null,
                    "No supported job requires recovery."),
                new CareerJobPreFlightSessionRecoveryResult(
                    CareerJobPreFlightSessionRecoveryState.WaitingForAircraft,
                    Guid.Parse(
                        "c1000000-0000-0000-0000-000000000073"),
                    "The contract-owned aircraft is unavailable."));
        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(
                    Board(
                        "KRME",
                        offer)),
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now),
                new CareerJobAircraftSelectionSource(
                    new FakeDiscovery()),
                action,
                logger:
                    null,
                startRecovery:
                    recovery);

        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync("fixture-aircraft");
        await viewModel.RefreshAsync();
        int acceptCount =
            action.AcceptCount;
        int startCount =
            action.StartCount;

        await viewModel.StartOfferAsync(offer.OfferId);

        Assert.False(viewModel.CanStartJobActions);
        Assert.Equal(acceptCount, action.AcceptCount);
        Assert.Equal(startCount, action.StartCount);
        Assert.Contains(
            "Resolve the job recovery attention",
            viewModel.AcceptanceStatus,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulRetryClearsPriorJobRecoveryAttention()
    {
        var recovery =
            new FakeJobRecovery(
                new CareerJobPreFlightSessionRecoveryResult(
                    CareerJobPreFlightSessionRecoveryState.WaitingForAircraft,
                    Guid.Parse(
                        "c1000000-0000-0000-0000-000000000072"),
                    "The contract-owned aircraft is unavailable."),
                new CareerJobPreFlightSessionRecoveryResult(
                    CareerJobPreFlightSessionRecoveryState.NothingToRecover,
                    ContractId:
                        null,
                    "No supported job requires recovery."));
        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(),
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now),
                aircraftSelection:
                    null,
                startAction:
                    null,
                logger:
                    null,
                startRecovery:
                    recovery);

        await viewModel.RefreshAsync();
        Assert.True(viewModel.HasJobRecoveryAttention);

        await viewModel.RefreshAsync();

        Assert.False(viewModel.HasJobRecoveryAttention);
        Assert.True(viewModel.CanStartJobActions);
        Assert.Equal(
            "JOB RECOVERY · READY",
            viewModel.JobRecoveryStatus);
        Assert.Equal(2, recovery.CallCount);
    }

    [Fact]
    public async Task RecoveryFailureShowsSafeRetryGuidanceWithoutRawErrorText()
    {
        var recovery =
            new FakeJobRecovery(
                new InvalidOperationException(
                    "synthetic technical recovery detail"));
        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(),
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now),
                aircraftSelection:
                    null,
                startAction:
                    null,
                logger:
                    null,
                startRecovery:
                    recovery);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasJobRecoveryAttention);
        Assert.Equal(
            "JOB RECOVERY · RETRY REQUIRED",
            viewModel.JobRecoveryStatus);
        Assert.Contains(
            "No persisted job was cleared",
            viewModel.JobRecoveryDetail,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "synthetic technical recovery detail",
            viewModel.JobRecoveryDetail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContractlessFlightSessionCanBeOpenedDirectlyFromJobs()
    {
        var sessions = new FlightSessionCoordinator();
        sessions.Start(
            Now,
            contractId: null,
            sessionId: Guid.Parse(
                "a7000000-0000-0000-0000-000000000002"));
        var viewModel = new JobsViewModel(
            new FakeBoardStore(Board("KRME", Offer("KRME", "KSYR", locked: false))),
            CareerRuntime("KRME"),
            new FixedTimeProvider(Now),
            aircraftSelection: null,
            startAction: null,
            logger: null,
            flightSessions: sessions);

        var navigationTargets = new List<JobsNavigationTarget>();
        viewModel.NavigationRequested += (_, args) =>
            navigationTargets.Add(args.Target);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasCurrentFlightSession);
        Assert.False(viewModel.CanStartJobActions);
        Assert.Equal("FLIGHT SESSION · ACTIVE", viewModel.CurrentFlightSessionStatus);
        Assert.Contains(
            "before accepting another job",
            viewModel.CurrentFlightSessionDetail,
            StringComparison.Ordinal);

        viewModel.OpenCurrentFlight();

        Assert.Equal(
            new[] { JobsNavigationTarget.CurrentFlight },
            navigationTargets);
    }

    [Fact]
    public async Task CurrentFlightSessionRejectsStaleStartInvocationAtViewModelBoundary()
    {
        JobMarketOfferDraft offer =
            Offer(
                "KRME",
                "KSYR",
                locked:
                    false);
        var action =
            new FakeStartAction();
        var sessions =
            new FlightSessionCoordinator();
        Guid acceptedContractId =
            Guid.Parse(
                "c1000000-0000-0000-0000-000000000074");
        var acceptedContext =
            new MutableAcceptedContext(
                acceptedContractId);
        var cancellation =
            new RecordingAcceptedCancellation(
                acceptedContext);
        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(
                    Board(
                        "KRME",
                        offer)),
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now),
                new CareerJobAircraftSelectionSource(
                    new FakeDiscovery()),
                action,
                logger:
                    null,
                acceptedContext:
                    acceptedContext,
                acceptedCancellation:
                    cancellation,
                flightSessions:
                    sessions);

        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync("fixture-aircraft");

        sessions.Start(
            Now,
            contractId:
                null,
            sessionId:
                Guid.Parse(
                    "a7000000-0000-0000-0000-000000000003"));

        await viewModel.StartOfferAsync(offer.OfferId);

        Assert.True(viewModel.HasCurrentFlightSession);
        Assert.False(viewModel.CanStartJobActions);
        Assert.True(viewModel.CanCancelAcceptedJobs);
        Assert.Equal(0, action.AcceptCount);
        Assert.Equal(0, action.StartCount);
        Assert.Contains(
            "Open Current Flight",
            viewModel.AcceptanceStatus,
            StringComparison.Ordinal);

        await viewModel.CancelAcceptedJobsAsync();

        Assert.Equal(
            new[] { acceptedContractId },
            cancellation.CancelledContractIds);
        Assert.True(viewModel.HasCurrentFlightSession);
        Assert.False(viewModel.CanStartJobActions);
    }

    [Fact]
    public async Task AircraftOnlyRefreshConsumesLateDiscoveryWithoutRefillingBoard()
    {
        JobBoardState board =
            Board(
                "KRME",
                Offer(
                    "KRME",
                    "KSYR",
                    locked:
                        false));

        var refill =
            new FakeBoardRefill(
                board);

        var discovery =
            new FakeDiscovery();

        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(
                    board),
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now),
                new CareerJobAircraftSelectionSource(
                    discovery),
                new FakeStartAction(),
                logger:
                    null,
                boardRefill:
                    refill);

        await viewModel.RefreshAsync();

        Assert.Single(
            viewModel.AircraftOptions);

        discovery.Current =
            new InstalledAircraftDiscoverySnapshot(
                InstalledAircraftDiscoveryAvailability.Available,
                [
                    Installed(
                        "fixture-aircraft",
                        "Fixture Aircraft"),
                    Installed(
                        "late-aircraft",
                        "Late Aircraft")
                ]);

        await viewModel.RefreshAircraftAndReadinessAsync();

        Assert.Equal(
            2,
            viewModel.AircraftOptions.Count);
        Assert.Equal(
            1,
            refill.CallCount);
    }

    [Fact]
    public async Task ProductionRefreshBoundaryInvokesBoardRefillExactlyOnce()
    {
        JobBoardState board =
            Board(
                "KRME",
                Offer(
                    "KRME",
                    "KSYR",
                    locked:
                        false));

        var refill =
            new FakeBoardRefill(
                board);

        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(
                    board),
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now),
                aircraftSelection:
                    null,
                startAction:
                    null,
                logger:
                    null,
                boardRefill:
                    refill);

        await viewModel.RefreshAsync();

        Assert.Equal(
            1,
            refill.CallCount);
        Assert.Single(
            viewModel.Offers);
    }

    [Fact]
    public async Task RefreshPublishesBusyStateUntilBoardLoadCompletes()
    {
        JobBoardState board =
            Board(
                "KRME",
                Offer(
                    "KRME",
                    "KSYR",
                    locked: false));
        var refill =
            new BlockingBoardRefill(board);
        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(board),
                CareerRuntime("KRME"),
                new FixedTimeProvider(Now),
                aircraftSelection: null,
                startAction: null,
                logger: null,
                boardRefill: refill);

        Task refresh =
            viewModel.RefreshAsync();

        await refill.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.True(viewModel.IsRefreshing);
        Assert.False(refresh.IsCompleted);

        refill.Release.TrySetResult();
        await refresh.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.False(viewModel.IsRefreshing);
        Assert.Single(viewModel.Offers);
    }

    [Fact]
    public async Task ReplacingOptionsRetainsSelectionAndReadinessThroughTransientPickerEvents()
    {
        var discovery = new FakeDiscovery();
        var viewModel = SelectionViewModel(discovery);
        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync("fixture-aircraft");
        AssertReady(viewModel, "fixture-aircraft");

        string? projectedSelection = viewModel.SelectedAircraftId;
        var pickerEvents = new List<Task>();
        var notifications = new List<string?>();
        // Model the ItemsSource reset and OneWay selection-binding echo, including
        // null events queued while the refresh gate is held. This is not a WinUI test.
        viewModel.PropertyChanged += (_, e) =>
        {
            notifications.Add(e.PropertyName);
            if (e.PropertyName == nameof(JobsViewModel.AircraftOptions))
            {
                projectedSelection = null;
                pickerEvents.Add(viewModel.SelectAircraftAsync(null));
            }
            else if (e.PropertyName == nameof(JobsViewModel.SelectedAircraftId))
            {
                projectedSelection = viewModel.SelectedAircraftId;
                pickerEvents.Add(viewModel.SelectAircraftAsync(projectedSelection));
            }
        };

        for (int i = 0; i < 3; i++)
        {
            var previous = Assert.Single(viewModel.AircraftOptions);
            pickerEvents.Clear();
            notifications.Clear();
            discovery.Current = new(InstalledAircraftDiscoveryAvailability.Available,
                [Installed("fixture-aircraft", $"Refreshed Aircraft {i}")]);

            await viewModel.RefreshAircraftAndReadinessAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(pickerEvents).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotSame(previous, Assert.Single(viewModel.AircraftOptions));
            Assert.Same(Assert.Single(viewModel.AircraftOptions), viewModel.SelectedAircraftOption);
            Assert.Equal("fixture-aircraft", projectedSelection);
            Assert.True(notifications.IndexOf(nameof(JobsViewModel.SelectedAircraftId))
                > notifications.IndexOf(nameof(JobsViewModel.AircraftOptions)));
            AssertReady(viewModel, "fixture-aircraft");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthoritativeRemovalClearsSelectionAndReadiness(bool anotherAircraftRemains)
    {
        var discovery = new FakeDiscovery();
        var viewModel = SelectionViewModel(discovery);
        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync("fixture-aircraft");
        string? projectedSelection = viewModel.SelectedAircraftId;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(JobsViewModel.SelectedAircraftId))
                projectedSelection = viewModel.SelectedAircraftId;
        };
        discovery.Current = new(InstalledAircraftDiscoveryAvailability.Available,
            anotherAircraftRemains ? [Installed("aircraft-b", "Aircraft B")] : []);

        await viewModel.RefreshAircraftAndReadinessAsync();
        await viewModel.SelectAircraftAsync(null);

        Assert.Null(viewModel.SelectedAircraftId);
        Assert.Null(viewModel.SelectedAircraftOption);
        Assert.Null(projectedSelection);
        var offer = Assert.Single(viewModel.Offers);
        Assert.False(offer.CanStart);
        Assert.Null(offer.StartInputState);
        Assert.Equal("Select an installed aircraft to verify this offer.", offer.ActionText);
    }

    [Fact]
    public async Task AddingAircraftRetainsSelectionAndSwitchingUsesTheNewIdAcrossRefresh()
    {
        var discovery = new FakeDiscovery();
        var viewModel = SelectionViewModel(discovery);
        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync("fixture-aircraft");
        discovery.Current = new(InstalledAircraftDiscoveryAvailability.Available,
            [Installed("fixture-aircraft", "Aircraft A"), Installed("aircraft-b", "Aircraft B")]);

        await viewModel.RefreshAircraftAndReadinessAsync();
        Assert.Equal(2, viewModel.AircraftOptions.Count);
        AssertReady(viewModel, "fixture-aircraft");
        await viewModel.SelectAircraftAsync("aircraft-b");
        AssertReady(viewModel, "aircraft-b");

        for (int i = 0; i < 3; i++)
        {
            await viewModel.RefreshAircraftAndReadinessAsync();
            await viewModel.SelectAircraftAsync(null);
            AssertReady(viewModel, "aircraft-b");
        }
    }

    [Fact]
    public async Task TransientNullRetainsAvailableSelectionWithoutRequiringAnotherRefresh()
    {
        var viewModel = SelectionViewModel(new FakeDiscovery());
        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync(null);
        Assert.Null(viewModel.SelectedAircraftId);
        await viewModel.SelectAircraftAsync("fixture-aircraft");

        await viewModel.SelectAircraftAsync(null);

        AssertReady(viewModel, "fixture-aircraft");
    }

    [Fact]
    public async Task UnavailableDiscoveryRetainsPickerButBlocksReadinessAndStartUntilLiveEvidenceReturns()
    {
        var discovery = new FakeDiscovery();
        var action = new FakeStartAction();
        var viewModel = SelectionViewModel(discovery, action);
        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync("fixture-aircraft");
        AssertReady(viewModel, "fixture-aircraft");
        var retained = viewModel.SelectedAircraftOption;
        int reads = action.AvailabilityCount;
        discovery.Current = InstalledAircraftDiscoverySnapshot.Unavailable;

        for (int i = 0; i < 3; i++)
        {
            await viewModel.RefreshAircraftAndReadinessAsync();
            await viewModel.SelectAircraftAsync(null);
            Assert.Equal("fixture-aircraft", viewModel.SelectedAircraftId);
            Assert.Same(retained, viewModel.SelectedAircraftOption);
            Assert.False(Assert.Single(viewModel.Offers).CanStart);
        }
        await viewModel.StartOfferAsync(Assert.Single(viewModel.Offers).OfferId);
        Assert.Equal(0, action.StartCount);
        Assert.Equal(reads, action.AvailabilityCount); // no evaluation of retained installation history

        discovery.Current = new(InstalledAircraftDiscoveryAvailability.Available,
            [Installed("fixture-aircraft", "Returned Aircraft")]);
        await viewModel.RefreshAircraftAndReadinessAsync();
        AssertReady(viewModel, "fixture-aircraft");
        Assert.Equal("Returned Aircraft", viewModel.SelectedAircraftOption!.DisplayName);
        await viewModel.StartOfferAsync(Assert.Single(viewModel.Offers).OfferId);
        Assert.Equal(1, action.StartCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartRechecksDiscoveryEvenBeforeNextPeriodicRefresh(bool authoritativeRemoval)
    {
        var discovery = new FakeDiscovery();
        var action = new FakeStartAction();
        var viewModel = SelectionViewModel(discovery, action);
        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync("fixture-aircraft");
        AssertReady(viewModel, "fixture-aircraft");
        discovery.Current = authoritativeRemoval
            ? new(InstalledAircraftDiscoveryAvailability.Available, [])
            : InstalledAircraftDiscoverySnapshot.Unavailable;

        await viewModel.StartOfferAsync(Assert.Single(viewModel.Offers).OfferId);

        Assert.Equal(0, action.StartCount);
        Assert.False(Assert.Single(viewModel.Offers).CanStart);
        Assert.Equal(authoritativeRemoval ? null : "fixture-aircraft", viewModel.SelectedAircraftId);
    }

    private static JobsViewModel SelectionViewModel(FakeDiscovery discovery, FakeStartAction? action = null) =>
        new(new FakeBoardStore(Board("KRME", Offer("KRME", "KSYR", locked: false))),
            CareerRuntime("KRME"), new FixedTimeProvider(Now),
            new CareerJobAircraftSelectionSource(discovery), action ?? new FakeStartAction(), logger: null);

    [Fact]
    public async Task UnchangedAircraftRefreshDoesNotRepeatDispatchReadiness()
    {
        var discovery = new FakeDiscovery();
        var action = new FakeStartAction();
        var viewModel = SelectionViewModel(discovery, action);
        await viewModel.RefreshAsync();
        Assert.Equal(0, action.AvailabilityCount);
        await viewModel.SelectAircraftAsync("fixture-aircraft");
        Assert.Equal(1, action.AvailabilityCount);
        IReadOnlyList<CareerJobAircraftOption> retainedOptions = viewModel.AircraftOptions;
        CareerJobAircraftOption retainedSelection = Assert.IsType<CareerJobAircraftOption>(
            viewModel.SelectedAircraftOption);
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        for (int i = 0; i < 5; i++)
        {
            discovery.Current = new(InstalledAircraftDiscoveryAvailability.Available,
                [Installed("fixture-aircraft", "Fixture Aircraft")]); // new instances, same facts
            await viewModel.RefreshAircraftAndReadinessAsync();
            AssertReady(viewModel, "fixture-aircraft");
        }
        Assert.Same(retainedOptions, viewModel.AircraftOptions);
        Assert.Same(retainedSelection, viewModel.SelectedAircraftOption);
        Assert.DoesNotContain(nameof(JobsViewModel.AircraftOptions), notifications);
        Assert.DoesNotContain(nameof(JobsViewModel.SelectedAircraftId), notifications);
        Assert.Equal(1, action.AvailabilityCount);
        discovery.Current = new(InstalledAircraftDiscoveryAvailability.Available,
            [Installed("fixture-aircraft", "Fixture Aircraft"), Installed("aircraft-b", "Aircraft B")]);
        await viewModel.RefreshAircraftAndReadinessAsync();
        Assert.Equal(2, action.AvailabilityCount);
        await viewModel.SelectAircraftAsync("aircraft-b");
        Assert.Equal(3, action.AvailabilityCount);
        await viewModel.RefreshAircraftAndReadinessAsync();
        Assert.Equal(3, action.AvailabilityCount);
    }

    [Fact]
    public async Task RealCatalogChangesRefreshOptionsOnceAndPreserveOnlyPresentSelection()
    {
        var discovery = new FakeDiscovery();
        var viewModel = SelectionViewModel(discovery);
        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync("fixture-aircraft");
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        async Task ApplyAndAssertSingleOptionsRefresh(params AircraftRegistryObservation[] aircraft)
        {
            notifications.Clear();
            discovery.Current = new(
                InstalledAircraftDiscoveryAvailability.Available,
                aircraft);
            await viewModel.RefreshAircraftAndReadinessAsync();
            Assert.Single(notifications, static name =>
                name == nameof(JobsViewModel.AircraftOptions));
        }

        await ApplyAndAssertSingleOptionsRefresh(
            Installed("fixture-aircraft", "Fixture Aircraft"),
            Installed("aircraft-b", "Aircraft B"));
        Assert.Equal("fixture-aircraft", viewModel.SelectedAircraftId);

        await ApplyAndAssertSingleOptionsRefresh(
            Installed("fixture-aircraft", "Renamed Fixture Aircraft"),
            Installed("aircraft-b", "Aircraft B"));
        Assert.Equal("fixture-aircraft", viewModel.SelectedAircraftId);

        await ApplyAndAssertSingleOptionsRefresh(
            Installed("fixture-aircraft", "Renamed Fixture Aircraft"));
        Assert.Equal("fixture-aircraft", viewModel.SelectedAircraftId);

        await ApplyAndAssertSingleOptionsRefresh(
            Installed("aircraft-b", "Aircraft B"));
        Assert.Null(viewModel.SelectedAircraftId);
        Assert.False(Assert.Single(viewModel.Offers).CanStart);
    }

    [Fact]
    public async Task TimerRefreshesQueueBehindSelectionWithoutOverlappingOrRepeatingReadiness()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = new FakeStartAction { BeforeRead = async () => { entered.SetResult(); await release.Task; } };
        var viewModel = SelectionViewModel(new FakeDiscovery(), action);
        await viewModel.RefreshAsync();
        Task select = viewModel.SelectAircraftAsync("fixture-aircraft");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task[] refreshes = Enumerable.Range(0, 5).Select(_ => viewModel.RefreshAircraftAndReadinessAsync()).ToArray();
        Assert.All(refreshes, task => Assert.False(task.IsCompleted));
        Assert.Equal(1, action.AvailabilityCount);
        release.SetResult();
        await Task.WhenAll(refreshes.Append(select)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, action.AvailabilityCount);
        AssertReady(viewModel, "fixture-aircraft");
    }

    [Fact]
    public async Task UnchangedRefreshStillExpiresOffersWithoutDispatchIo()
    {
        var clock = new FixedTimeProvider(Now);
        var action = new FakeStartAction();
        var viewModel = new JobsViewModel(new FakeBoardStore(Board("KRME", Offer("KRME", "KSYR", false))),
            CareerRuntime("KRME"), clock, new CareerJobAircraftSelectionSource(new FakeDiscovery()), action, logger: null);
        await viewModel.RefreshAsync();
        await viewModel.SelectAircraftAsync("fixture-aircraft");
        AssertReady(viewModel, "fixture-aircraft");
        clock.Now = Now.AddHours(3);
        await viewModel.RefreshAircraftAndReadinessAsync();
        Assert.True(Assert.Single(viewModel.Offers).IsExpired);
        Assert.False(Assert.Single(viewModel.Offers).CanStart);
        Assert.Equal(1, action.AvailabilityCount);
    }

    private static void AssertReady(JobsViewModel viewModel, string aircraftId)
    {
        Assert.Equal(aircraftId, viewModel.SelectedAircraftId);
        Assert.Same(Assert.Single(viewModel.AircraftOptions, option => option.AircraftId == aircraftId),
            viewModel.SelectedAircraftOption);
        var offer = Assert.Single(viewModel.Offers);
        Assert.Equal(CareerJobStartInputState.Ready, offer.StartInputState);
        Assert.Equal("READY TO START", offer.AvailabilityText);
        Assert.True(offer.CanStart);
    }

    private static PlayerCareerRuntimeState CareerRuntime(
        string airportIcao)
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse(
                    "a6000000-0000-0000-0000-000000000001"),
                airportIcao,
                Now.AddDays(-10));

        return new PlayerCareerRuntimeState(
            new FakeProfileStore(
                new PlayerCareerProfileStoreRecord(
                    Revision:
                        1,
                    profile,
                    SavedAt:
                        Now.AddDays(-1))));
    }

    private static CareerJobPlayableLoopReadinessSource ActiveFlightReadiness(
        Guid? contractId)
    {
        var sessions = new FlightSessionCoordinator();
        PersistedJobContract[] contracts = [];

        if (contractId is { } activeContractId)
        {
            sessions.Start(
                Now,
                activeContractId,
                sessionId: activeContractId);

            var contract = new JobContract(
                ContractId: activeContractId,
                EmployerId: null,
                Kind: ContractKind.Ferry,
                ServiceTrack: ServiceTrack.CivilianEmployment,
                OriginIcao: "KRME",
                DestinationIcao: "KSYR",
                Compensation: new ContractCompensation(
                    CompensationModel.PilotWage,
                    500m,
                    0m,
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),
                OfferedAt: Now.AddHours(-1),
                MustStartBy: null,
                MustCompleteBy: Now.AddHours(2),
                AircraftRequirements: new AircraftMissionRequirements(
                    AllowedAccess: AircraftAccess.Civilian),
                Status: ContractStatus.InProgress,
                AcceptedAt: Now.AddMinutes(-30),
                StartedAt: Now,
                CompletedAt: null);

            contract.Validate();
            contracts = [new PersistedJobContract(contract, Version: 2)];
        }

        return new CareerJobPlayableLoopReadinessSource(
            sessions,
            new StaticContractRuntimeSource(contracts),
            new JobFlightCompletionEvidenceTracker(
                sessions,
                new EmptyFlightEvidenceSource()));
    }

    private static JobBoardState Board(
        string airportIcao,
        params JobMarketOfferDraft[] offers)
    {
        var board =
            new JobBoardState(
                airportIcao,
                Now.AddMinutes(-10),
                offers.ToImmutableArray(),
                ImmutableHashSet<Guid>.Empty);

        board.Validate();
        return board;
    }

    private static JobMarketOfferDraft Offer(
        string origin,
        string destination,
        bool locked) =>
        new(
            Guid.NewGuid(),
            ServiceTrack.CivilianEmployment,
            ContractKind.Ferry,
            JobScenarioKind.Standard,
            origin,
            destination,
            DistanceNm:
                100,
            EstimatedFlightHours:
                1,
            OfferedAt:
                Now.AddHours(-1),
            ExpiresAt:
                Now.AddHours(2),
            IsLockedPreview:
                locked,
            RouteStrength:
                0.5,
            RelationshipStrength:
                0.5,
            MarketSelectionWeight:
                1);

    private static JobMarketContractTermsEnvelope Terms(
        AircraftMissionRequirements requirements,
        double payloadPounds) =>
        new(
            AuthorityId:
                "tests:jobs-card-assignment:v1",
            AircraftRequirements:
                requirements,
            EstimatedFlightHours:
                1,
            PayloadPounds:
                payloadPounds,
            DemandAttractiveness:
                1,
            Urgency:
                0,
            Difficulty:
                0);

    private sealed class FakeBoardRefill(
        JobBoardState board)
        : ICareerJobBoardRefillService
    {
        public int CallCount { get; private set; }

        public Task<JobBoardState> RefillAsync(
            PlayerCareerProfile profile,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            profile.Validate();
            CallCount++;

            Assert.Equal(
                profile.Location.CurrentAirportIcao,
                board.AirportIcao);

            return Task.FromResult(
                board);
        }
    }

    private sealed class BlockingBoardRefill(
        JobBoardState board)
        : ICareerJobBoardRefillService
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<JobBoardState> RefillAsync(
            PlayerCareerProfile profile,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            profile.Validate();
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return board;
        }
    }

    private sealed class FakeDiscovery
        : IInstalledAircraftDiscoverySource
    {
        public InstalledAircraftDiscoverySnapshot Current { get; set; } =
            new(
                InstalledAircraftDiscoveryAvailability.Available,
                [
                    Installed(
                        "fixture-aircraft",
                        "Fixture Aircraft")
                ]);
    }

    private static AircraftRegistryObservation Installed(
        string aircraftId,
        string displayName) =>
        new(
            aircraftId,
            ProviderId:
                "fixture",
            ProviderRecordId:
                displayName,
            AircraftDataConfidence.Verified,
            IsInstalled:
                true,
            DisplayName:
                displayName);

    private sealed class FakeStartAction
        : ICareerJobStartAction
    {
        public const string AcceptanceFailureMessage =
            "Synthetic acceptance failure.";
        public const string StartFailureMessage =
            "Synthetic start result intentionally stops after delegation.";

        public int AvailabilityCount { get; private set; }
        public int StartCount { get; private set; }
        public int AcceptCount { get; private set; }
        public Guid? LastOfferId { get; private set; }
        public string? LastAircraftId { get; private set; }
        public CareerJobActionKind ActionKind { get; init; } =
            CareerJobActionKind.StartPreparedFlight;
        public bool FailAcceptance { get; init; }
        public bool CompleteStart { get; init; }

        public Func<Task>? BeforeRead { get; init; }
        public Func<Task>? BeforeStart { get; init; }

        public async Task<CareerJobStartActionAvailability> ReadAvailabilityAsync(
            Guid offerId,
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AvailabilityCount++;
            if (BeforeRead is not null) await BeforeRead();
            return
                new CareerJobStartActionAvailability(
                    CanStart:
                        true,
                    CareerJobStartInputState.Ready,
                    "Authoritative start inputs are ready.",
                    ActionKind);
        }

        public Task<JobAcceptanceFleetResult> AcceptAsync(
            Guid offerId,
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcceptCount++;
            LastOfferId = offerId;
            LastAircraftId = aircraftId;

            if (FailAcceptance)
                throw new IOException(
                    AcceptanceFailureMessage);

            return Task.FromResult<JobAcceptanceFleetResult>(null!);
        }

        public async Task<CareerJobPlayableStartResult> StartAsync(
            Guid offerId,
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            LastOfferId =
                offerId;
            LastAircraftId =
                aircraftId;

            if (BeforeStart is not null)
                await BeforeStart();

            if (CompleteStart)
            {
                return null!;
            }

            throw new InvalidOperationException(
                StartFailureMessage);
        }
    }

    private sealed class FakeJobRecovery
        : ICareerJobPreFlightSessionRecoveryService
    {
        private readonly Queue<object> _outcomes;

        public FakeJobRecovery(params object[] outcomes)
        {
            _outcomes =
                new Queue<object>(outcomes);
        }

        public int CallCount { get; private set; }

        public Task<CareerJobPreFlightSessionRecoveryResult> RecoverAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            object outcome =
                _outcomes.Count > 1
                    ? _outcomes.Dequeue()
                    : _outcomes.Peek();

            return outcome is Exception exception
                ? Task.FromException<CareerJobPreFlightSessionRecoveryResult>(exception)
                : Task.FromResult(
                    (CareerJobPreFlightSessionRecoveryResult)outcome);
        }
    }

    private sealed class MutableAcceptedContext(
        params Guid[] contractIds)
        : IAcceptedDispatchPlanningContextSource
    {
        private readonly List<Guid> _contractIds = contractIds.ToList();

        public void Remove(Guid contractId) =>
            _contractIds.Remove(contractId);

        public Task<AcceptedDispatchPlanningContext> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _contractIds.Count switch
                {
                    0 => new AcceptedDispatchPlanningContext(
                        AcceptedDispatchPlanningContextStatus.NoAcceptedContract,
                        "No accepted job is waiting for dispatch preparation."),
                    1 => new AcceptedDispatchPlanningContext(
                        AcceptedDispatchPlanningContextStatus.Ready,
                        "One accepted job is waiting.",
                        ContractId: _contractIds[0],
                        AcceptedContractIds: _contractIds.ToArray()),
                    _ => new AcceptedDispatchPlanningContext(
                        AcceptedDispatchPlanningContextStatus.MultipleAcceptedContracts,
                        "Multiple accepted jobs are waiting.",
                        AcceptedContractIds: _contractIds.ToArray())
                });
        }
    }

    private sealed class RecordingAcceptedCancellation(
        MutableAcceptedContext context)
        : IAcceptedJobCancellationService
    {
        public List<Guid> CancelledContractIds { get; } = [];

        public Task<AcceptedJobCancellationResult> CancelAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CancelledContractIds.Add(contractId);
            context.Remove(contractId);

            return Task.FromResult(
                new AcceptedJobCancellationResult(
                    contractId,
                    $"job:{contractId:N}",
                    ReservationWasAlreadyReleased: false));
        }
    }

    private sealed class FailingAcceptedCancellation
        : IAcceptedJobCancellationService
    {
        public const string TechnicalMessage =
            "INTERNAL reservation-store failure details";

        public int CallCount { get; private set; }

        public Task<AcceptedJobCancellationResult> CancelAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            return Task.FromException<AcceptedJobCancellationResult>(
                new IOException(
                    TechnicalMessage));
        }
    }

    private sealed class FakeBoardStore(
        params JobBoardState[] boards)
        : IJobBoardStateStore
    {
        public Task SaveAsync(
            JobBoardState state,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobBoardState?> GetAsync(
            string airportIcao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                boards.SingleOrDefault(
                    board =>
                        string.Equals(
                            board.AirportIcao,
                            airportIcao,
                            StringComparison.Ordinal)));
        }

        public Task<IReadOnlyList<JobBoardState>> LoadAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobBoardState>>(
                boards);
    }

    private sealed class FakeProfileStore(
        PlayerCareerProfileStoreRecord? record)
        : IPlayerCareerProfileStore
    {
        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StaticContractRuntimeSource(
        IReadOnlyList<PersistedJobContract> contracts)
        : IJobContractRuntimeSource
    {
        public bool IsInitialized => true;
        public IReadOnlyList<PersistedJobContract> Current => contracts;

        public PersistedJobContract? Find(Guid contractId) =>
            contracts.SingleOrDefault(
                contract => contract.Contract.ContractId == contractId);
    }

    private sealed class EmptyFlightEvidenceSource
        : IFlightStateEvidenceSource
    {
        public FlightStateEvidence? Current => null;

        public event Action<FlightStateEvidence?>? EvidenceChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset now)
        : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() =>
            Now;
    }
}
