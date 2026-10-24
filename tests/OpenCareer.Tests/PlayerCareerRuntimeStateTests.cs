using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class PlayerCareerRuntimeStateTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 21, 3, 0, 0, TimeSpan.Zero);

    private static readonly Guid CareerId =
        Guid.Parse("c195b28f-d036-45d0-8d2a-1f64caaec5a9");

    [Fact]
    public async Task InitializeLoadsPersistedProfileOnce()
    {
        PlayerCareerProfileStoreRecord record = Record();
        var store = new FakeStore(record);
        var runtime = new PlayerCareerRuntimeState(store);

        PlayerCareerProfileStoreRecord? first =
            await runtime.InitializeAsync();
        PlayerCareerProfileStoreRecord? second =
            await runtime.InitializeAsync();

        Assert.True(runtime.IsInitialized);
        Assert.False(runtime.OnboardingRequired);
        Assert.Same(record, first);
        Assert.Same(record, second);
        Assert.Same(record, runtime.Current);
        Assert.Equal(1, store.LoadCount);
    }

    [Fact]
    public async Task EmptyStoreRequiresOnboarding()
    {
        var store = new FakeStore(null);
        var runtime = new PlayerCareerRuntimeState(store);

        PlayerCareerProfileStoreRecord? recovered =
            await runtime.InitializeAsync();

        Assert.True(runtime.IsInitialized);
        Assert.True(runtime.OnboardingRequired);
        Assert.Null(recovered);
        Assert.Null(runtime.Current);
        Assert.Equal(1, store.LoadCount);
    }

    [Fact]
    public async Task FailedInitializationCanBeRetried()
    {
        PlayerCareerProfileStoreRecord record = Record();
        var store = new FakeStore(record)
        {
            FailNextLoad = true
        };
        var runtime = new PlayerCareerRuntimeState(store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.InitializeAsync());

        Assert.False(runtime.IsInitialized);
        Assert.False(runtime.OnboardingRequired);
        Assert.Null(runtime.Current);

        Assert.Same(record, await runtime.InitializeAsync());
        Assert.Equal(2, store.LoadCount);
    }

    [Fact]
    public async Task NewlyCreatedProfileCanReplaceOnboardingRequiredState()
    {
        var runtime = new PlayerCareerRuntimeState(
            new FakeStore(null));

        await runtime.InitializeAsync();
        Assert.True(runtime.OnboardingRequired);

        PlayerCareerProfileStoreRecord record = Record();
        runtime.Replace(record);

        Assert.True(runtime.IsInitialized);
        Assert.False(runtime.OnboardingRequired);
        Assert.Same(record, runtime.Current);
    }

    [Fact]
    public void ReplaceRejectsOlderRevisionAndDifferentCareerIdentity()
    {
        var runtime = new PlayerCareerRuntimeState(
            new FakeStore(null));

        PlayerCareerProfileStoreRecord original = Record() with
        {
            Revision = 2
        };
        runtime.Replace(original);

        Assert.Throws<PlayerCareerProfileConcurrencyException>(
            () => runtime.Replace(original with { Revision = 1 }));

        PlayerCareerProfile differentCareer =
            PlayerCareerProfile.Start(
                Guid.Parse("a47f252b-70d7-4dca-a8ad-1dd846a96864"),
                "KDFW",
                Epoch);

        Assert.Throws<PlayerCareerProfileConcurrencyException>(
            () => runtime.Replace(
                new PlayerCareerProfileStoreRecord(
                    Revision: 3,
                    differentCareer,
                    SavedAt: Epoch.AddMinutes(5))));
    }

    private static PlayerCareerProfileStoreRecord Record()
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                CareerId,
                "KRME",
                Epoch);

        return new PlayerCareerProfileStoreRecord(
            Revision: 1,
            profile,
            SavedAt: Epoch.AddMinutes(1));
    }

    private sealed class FakeStore
        : IPlayerCareerProfileStore
    {
        private readonly PlayerCareerProfileStoreRecord? _record;

        public FakeStore(
            PlayerCareerProfileStoreRecord? record)
        {
            _record = record;
        }

        public int LoadCount { get; private set; }

        public bool FailNextLoad { get; set; }

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;

            if (FailNextLoad)
            {
                FailNextLoad = false;
                throw new InvalidOperationException(
                    "Synthetic player career recovery failure.");
            }

            return Task.FromResult(_record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "Save is not used by runtime recovery tests.");
    }
}
