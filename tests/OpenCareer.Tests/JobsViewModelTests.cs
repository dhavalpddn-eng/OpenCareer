using System.Collections.Immutable;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

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

    private static JobsViewModel SelectionViewModel(FakeDiscovery discovery) =>
        new(new FakeBoardStore(Board("KRME", Offer("KRME", "KSYR", locked: false))),
            CareerRuntime("KRME"), new FixedTimeProvider(Now),
            new CareerJobAircraftSelectionSource(discovery), new FakeStartAction(), logger: null);

    private static void AssertReady(JobsViewModel viewModel, string aircraftId)
    {
        Assert.Equal(aircraftId, viewModel.SelectedAircraftId);
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
        public int StartCount { get; private set; }
        public Guid? LastOfferId { get; private set; }
        public string? LastAircraftId { get; private set; }

        public Task<CareerJobStartActionAvailability> ReadAvailabilityAsync(
            Guid offerId,
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            LastOfferId =
                offerId;
            LastAircraftId =
                aircraftId;

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
