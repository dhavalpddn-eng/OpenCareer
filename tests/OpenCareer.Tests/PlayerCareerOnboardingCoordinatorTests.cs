using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class PlayerCareerOnboardingCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 21, 4, 0, 0, TimeSpan.Zero);

    private static readonly Guid CareerId =
        Guid.Parse("79597ed6-cde2-45e3-a719-f1fe24dc297d");

    [Fact]
    public async Task CreatePersistsFirstProfileAndPublishesRuntime()
    {
        var store = new FakeStore();
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerOnboardingCoordinator(
                store,
                runtime);

        PlayerCareerProfileStoreRecord created =
            await coordinator.CreateAsync(
                CareerId,
                "krme",
                Epoch);

        Assert.Equal(1, created.Revision);
        Assert.Equal(CareerId, created.Profile.CareerId);
        Assert.Equal(Epoch, created.Profile.CreatedAt);
        Assert.Equal("KRME", created.Profile.Location.HomeAirportIcao);
        Assert.Equal("KRME", created.Profile.Location.CurrentAirportIcao);
        Assert.Equal(Epoch, created.SavedAt);
        Assert.Null(store.LastExpectedRevision);
        Assert.Equal(1, store.SaveCount);
        Assert.Same(created, runtime.Current);
        Assert.False(runtime.OnboardingRequired);
    }

    [Fact]
    public async Task ExistingProfilePreventsOnboardingWrite()
    {
        PlayerCareerProfileStoreRecord existing =
            ExistingRecord();
        var store = new FakeStore(existing);
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerOnboardingCoordinator(
                store,
                runtime);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.CreateAsync(
                CareerId,
                "KRME",
                Epoch));

        Assert.Equal(0, store.SaveCount);
        Assert.Same(existing, runtime.Current);
        Assert.False(runtime.OnboardingRequired);
    }

    [Fact]
    public async Task RepeatedCreateDoesNotWriteSecondProfile()
    {
        var store = new FakeStore();
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerOnboardingCoordinator(
                store,
                runtime);

        PlayerCareerProfileStoreRecord first =
            await coordinator.CreateAsync(
                CareerId,
                "KRME",
                Epoch);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.CreateAsync(
                Guid.Parse("43210080-fabe-430b-8a06-06187704a4df"),
                "KDFW",
                Epoch.AddMinutes(1)));

        Assert.Equal(1, store.SaveCount);
        Assert.Same(first, runtime.Current);
    }

    [Fact]
    public async Task ConcurrentCreateAttemptsPersistExactlyOnce()
    {
        var store = new FakeStore
        {
            PauseNextSave = true
        };
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerOnboardingCoordinator(
                store,
                runtime);

        Task<PlayerCareerProfileStoreRecord> first =
            coordinator.CreateAsync(
                CareerId,
                "KRME",
                Epoch);

        await store.SaveStarted.Task;

        Task<PlayerCareerProfileStoreRecord> second =
            coordinator.CreateAsync(
                Guid.Parse("9ec24c00-a89e-4764-a6aa-73ac318e5b55"),
                "KDFW",
                Epoch.AddMinutes(1));

        store.AllowSave.SetResult();

        PlayerCareerProfileStoreRecord created = await first;

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await second);

        Assert.Equal(1, store.SaveCount);
        Assert.Equal(CareerId, created.Profile.CareerId);
        Assert.Same(created, runtime.Current);
    }

    private static PlayerCareerProfileStoreRecord ExistingRecord()
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse("085c0ae1-9d5d-4e8a-a566-eb00d1723df4"),
                "KALB",
                Epoch.AddHours(-1));

        return new PlayerCareerProfileStoreRecord(
            Revision: 3,
            profile,
            SavedAt: Epoch.AddMinutes(-30));
    }

    private sealed class FakeStore
        : IPlayerCareerProfileStore
    {
        private PlayerCareerProfileStoreRecord? _record;

        public FakeStore(
            PlayerCareerProfileStoreRecord? record = null)
        {
            _record = record;
        }

        public int SaveCount { get; private set; }

        public long? LastExpectedRevision { get; private set; }

        public bool PauseNextSave { get; set; }

        public TaskCompletionSource SaveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowSave { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_record);
        }

        public async Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            LastExpectedRevision = expectedRevision;

            if (_record is not null || expectedRevision is not null)
            {
                throw new PlayerCareerProfileConcurrencyException(
                    "Player career profile already exists.");
            }

            if (PauseNextSave)
            {
                PauseNextSave = false;
                SaveStarted.SetResult();
                await AllowSave.Task.WaitAsync(cancellationToken);
            }

            var saved = new PlayerCareerProfileStoreRecord(
                Revision: 1,
                profile,
                savedAt);
            saved.Validate();
            _record = saved;
            return saved;
        }
    }
}
