using System.Collections.Immutable;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Careers;
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
