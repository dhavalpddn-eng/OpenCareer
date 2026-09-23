using System.Collections.Immutable;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class JobsViewModelTests
{
    private static readonly Guid CareerId =
        Guid.Parse(
            "a6000000-0000-0000-0000-000000000001");

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

        PlayerCareerRuntimeState career =
            CareerRuntime("KRME");

        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(
                    Board(
                        "KRME",
                        offer)),
                career,
                new FixedTimeProvider(Now),
                new CareerJobAircraftSelectionSource(
                    career,
                    new TestOwnershipStore(
                        CareerAircraftTestData.Snapshot(
                            CareerId,
                            CareerAircraftTestData.Owned(
                                CareerId,
                                "owned-fixture",
                                "fixture-aircraft",
                                "Fixture Aircraft"))),
                    new TestAircraftAvailabilityStore()),
                action,
                logger:
                    null);

        await viewModel.RefreshAsync();

        CareerJobAircraftOption aircraft =
            Assert.Single(
                viewModel.AircraftOptions);

        await viewModel.SelectAircraftAsync(
            aircraft.SelectionId);

        Assert.Equal(
            aircraft.SelectionId,
            viewModel.SelectedAircraftSelectionId);
        Assert.Equal(
            aircraft.AircraftId,
            viewModel.SelectedAircraftId);

        JobOfferItemViewModel projected =
            Assert.Single(
                viewModel.Offers);

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
    }

    [Fact]
    public async Task ContractSuppliedAircraftOnlyEnablesItsAssignedOffer()
    {
        JobMarketOfferDraft assignedOffer =
            ProviderOffer(
                Guid.Parse(
                    "89000000-0000-0000-0000-000000000001"),
                Guid.Parse(
                    "89000000-0000-0000-0000-000000000011"),
                "KSYR");

        JobMarketOfferDraft otherOffer =
            ProviderOffer(
                Guid.Parse(
                    "89000000-0000-0000-0000-000000000002"),
                Guid.Parse(
                    "89000000-0000-0000-0000-000000000012"),
                "KALB");

        JobBoardState board =
            Board(
                "KRME",
                assignedOffer,
                otherOffer);

        var boardStore =
            new FakeBoardStore(
                board);

        PlayerCareerRuntimeState career =
            CareerRuntime("KRME");

        var action =
            new FakeStartAction();

        var viewModel =
            new JobsViewModel(
                boardStore,
                career,
                new FixedTimeProvider(Now),
                new CareerJobAircraftSelectionSource(
                    career,
                    new TestOwnershipStore(
                        CareerAircraftTestData.Snapshot(
                            CareerId)),
                    new TestAircraftAvailabilityStore(),
                    boardStore,
                    new FixedTimeProvider(Now)),
                action,
                logger:
                    null);

        await viewModel.RefreshAsync();

        CareerJobAircraftOption assignedAircraft =
            Assert.Single(
                viewModel.AircraftOptions,
                option =>
                    option.ProviderOfferId
                    == assignedOffer.OfferId);

        await viewModel.SelectAircraftAsync(
            assignedAircraft.SelectionId);

        Assert.True(
            Assert.Single(
                viewModel.Offers,
                offer =>
                    offer.OfferId
                    == assignedOffer.OfferId)
                .CanStart);

        Assert.False(
            Assert.Single(
                viewModel.Offers,
                offer =>
                    offer.OfferId
                    == otherOffer.OfferId)
                .CanStart);

        Assert.Equal(
            (
                assignedOffer.OfferId,
                assignedAircraft.AircraftId,
                assignedAircraft.ProviderAircraftInstanceId
            ),
            Assert.Single(
                action.AvailabilityRequests));

        await viewModel.StartOfferAsync(
            assignedOffer.OfferId);

        Assert.Equal(
            assignedAircraft.ProviderAircraftInstanceId,
            action.LastProviderAircraftInstanceId);
    }

    [Fact]
    public async Task AircraftOnlyRefreshConsumesUpdatedOwnershipWithoutRefillingBoard()
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

        PlayerCareerRuntimeState career =
            CareerRuntime("KRME");

        var ownership =
            new TestOwnershipStore(
                CareerAircraftTestData.Snapshot(
                    CareerId,
                    CareerAircraftTestData.Owned(
                        CareerId,
                        "owned-fixture",
                        "fixture-aircraft",
                        "Fixture Aircraft")));

        var viewModel =
            new JobsViewModel(
                new FakeBoardStore(
                    board),
                career,
                new FixedTimeProvider(Now),
                new CareerJobAircraftSelectionSource(
                    career,
                    ownership,
                    new TestAircraftAvailabilityStore()),
                new FakeStartAction(),
                logger:
                    null,
                boardRefill:
                    refill);

        await viewModel.RefreshAsync();

        Assert.Single(
            viewModel.AircraftOptions);

        ownership.Snapshot =
            CareerAircraftTestData.Snapshot(
                CareerId,
                CareerAircraftTestData.Owned(
                    CareerId,
                    "owned-fixture",
                    "fixture-aircraft",
                    "Fixture Aircraft"),
                CareerAircraftTestData.Owned(
                    CareerId,
                    "owned-late",
                    "late-aircraft",
                    "Late Aircraft"));

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

    private static PlayerCareerRuntimeState CareerRuntime(
        string airportIcao)
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                CareerId,
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

    private static JobMarketOfferDraft ProviderOffer(
        Guid offerId,
        Guid providerInstanceId,
        string destination)
    {
        var requirements =
            new AircraftMissionRequirements(
                AllowedAccess:
                    AircraftAccess.Civilian,
                MinimumRangeNauticalMiles:
                    100);

        return new(
            offerId,
            ServiceTrack.CivilianEmployment,
            ContractKind.Ferry,
            JobScenarioKind.Standard,
            "KRME",
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
                false,
            RouteStrength:
                0.5,
            RelationshipStrength:
                0.5,
            MarketSelectionWeight:
                1,
            ContractTerms:
                new JobMarketContractTermsEnvelope(
                    PersistedJobContractTermsSource.AuthorityId,
                    requirements,
                    EstimatedFlightHours:
                        1,
                    PayloadPounds:
                        0,
                    DemandAttractiveness:
                        1,
                    Urgency:
                        0,
                    Difficulty:
                        0,
                    RequiredPilotQualifications:
                        PilotQualificationState.Entry,
                    AuthorizedAircraftAccess:
                        AircraftAccess.Civilian,
                    ProviderAircraft:
                        new ProviderAircraftAssignment(
                            providerInstanceId,
                            "provider-aircraft",
                            "Provider Aircraft",
                            "KRME")));
    }

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

    private sealed class FakeStartAction
        : ICareerJobStartAction
    {
        public int StartCount { get; private set; }
        public Guid? LastOfferId { get; private set; }
        public string? LastAircraftId { get; private set; }
        public Guid? LastProviderAircraftInstanceId { get; private set; }
        public List<(Guid OfferId, string AircraftId, Guid? ProviderAircraftInstanceId)> AvailabilityRequests { get; } = [];

        public Task<CareerJobStartActionAvailability> ReadAvailabilityAsync(
            Guid offerId,
            string aircraftId,
            Guid? selectedProviderAircraftInstanceId = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AvailabilityRequests.Add(
                (
                    offerId,
                    aircraftId,
                    selectedProviderAircraftInstanceId
                ));

            return Task.FromResult(
                new CareerJobStartActionAvailability(
                    CanStart:
                        true,
                    CareerJobStartInputState.Ready,
                    "Authoritative start inputs are ready."));
        }

        public Task<CareerJobPlayableStartResult> StartAsync(
            Guid offerId,
            string aircraftId,
            Guid? selectedProviderAircraftInstanceId = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            LastOfferId =
                offerId;
            LastAircraftId =
                aircraftId;
            LastProviderAircraftInstanceId =
                selectedProviderAircraftInstanceId;

            throw new InvalidOperationException(
                "Synthetic start result intentionally stops after delegation.");
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

    private sealed class FixedTimeProvider(
        DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            now;
    }
}
