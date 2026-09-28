using OpenCareer.App.ViewModels;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class CareerViewModelTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 22, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MissingProfileShowsOnboardingState()
    {
        var store = new FakeProfileStore();
        var runtime = new PlayerCareerRuntimeState(store);
        var viewModel =
            ViewModel(
                store,
                runtime);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.HasCareer);
        Assert.False(viewModel.CanStartCareer);
        Assert.Contains(
            "starting airport",
            viewModel.StatusText,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartCareerPersistsProfileAndUpdatesRuntime()
    {
        var store = new FakeProfileStore();
        var runtime = new PlayerCareerRuntimeState(store);
        var viewModel =
            ViewModel(
                store,
                runtime);

        await viewModel.InitializeAsync();

        viewModel.HomeAirportIcao =
            "kdfw";

        Assert.True(viewModel.CanStartCareer);

        await viewModel.StartCareerAsync();

        PlayerCareerProfileStoreRecord current =
            Assert.IsType<PlayerCareerProfileStoreRecord>(
                runtime.Current);

        Assert.Equal(1, store.SaveCount);
        Assert.Equal(
            "KDFW",
            current.Profile.Location.HomeAirportIcao);
        Assert.Equal(
            "KDFW",
            current.Profile.Location.CurrentAirportIcao);
        Assert.Equal(
            Now,
            current.Profile.CreatedAt);
        Assert.True(viewModel.HasCareer);
        Assert.False(viewModel.CanStartCareer);
        Assert.Contains(
            "Open Jobs",
            viewModel.StatusText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidAirportDoesNotCreateProfile()
    {
        var store = new FakeProfileStore();
        var runtime = new PlayerCareerRuntimeState(store);
        var viewModel =
            ViewModel(
                store,
                runtime);

        await viewModel.InitializeAsync();

        viewModel.HomeAirportIcao =
            "DF1";

        await viewModel.StartCareerAsync();

        Assert.Equal(0, store.SaveCount);
        Assert.Null(runtime.Current);
        Assert.Contains(
            "four-letter airport ICAO",
            viewModel.StatusText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingCareerCannotBeCreatedAgain()
    {
        PlayerCareerProfile existing =
            PlayerCareerProfile.Start(
                Guid.Parse("35ec9ffb-df03-4130-90df-e93cc82c8264"),
                "KAFW",
                Now.AddHours(-1));

        var store =
            new FakeProfileStore(
                new PlayerCareerProfileStoreRecord(
                    Revision:
                        2,
                    existing,
                    SavedAt:
                        Now.AddMinutes(-30)));

        var runtime =
            new PlayerCareerRuntimeState(
                store);

        var viewModel =
            ViewModel(
                store,
                runtime);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasCareer);
        Assert.False(viewModel.CanStartCareer);
        Assert.Equal(
            "KAFW",
            viewModel.HomeAirportIcao);
        Assert.Contains(
            "CURRENT KAFW",
            viewModel.LocationText,
            StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCount);
    }

    private static CareerViewModel ViewModel(
        FakeProfileStore store,
        PlayerCareerRuntimeState runtime) =>
        new(
            new PlayerCareerOnboardingCoordinator(
                store,
                runtime),
            runtime,
            new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(
        DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            now;
    }

    private sealed class FakeProfileStore
        : IPlayerCareerProfileStore
    {
        private PlayerCareerProfileStoreRecord? _record;

        public FakeProfileStore(
            PlayerCareerProfileStoreRecord? record = null)
        {
            _record = record;
        }

        public int SaveCount { get; private set; }

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_record is not null
                || expectedRevision is not null)
            {
                throw new PlayerCareerProfileConcurrencyException(
                    "Player career profile already exists.");
            }

            SaveCount++;

            var saved =
                new PlayerCareerProfileStoreRecord(
                    Revision:
                        1,
                    profile,
                    savedAt);

            saved.Validate();
            _record = saved;

            return Task.FromResult(saved);
        }
    }
}
