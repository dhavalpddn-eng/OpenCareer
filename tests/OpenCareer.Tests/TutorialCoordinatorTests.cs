using OpenCareer.Application.Tutorials;

namespace OpenCareer.Tests;

public sealed class TutorialCoordinatorTests
{
    [Fact]
    public async Task NewProfileStartsAppIntroAtFirstStep()
    {
        var store = new MemoryProgressStore();
        var coordinator = CreateCoordinator(store, version: 1);

        await coordinator.InitializeAsync();

        Assert.True(coordinator.Current.IsActive);
        Assert.Equal("one", coordinator.Current.Step?.Id);
        Assert.Equal(0, coordinator.Current.CurrentIndex);
        Assert.Equal(2, coordinator.Current.StepCount);
    }

    [Fact]
    public async Task SkipSuppressesAutomaticIntroForCurrentVersion()
    {
        var store = new MemoryProgressStore();
        var coordinator = CreateCoordinator(store, version: 1);
        await coordinator.InitializeAsync();

        await coordinator.SkipAsync();

        var nextLaunch = CreateCoordinator(store, version: 1);
        await nextLaunch.InitializeAsync();

        Assert.False(nextLaunch.Current.IsActive);
        TutorialProgress saved = await store.GetAsync(AppTutorialCatalog.AppIntroId);
        Assert.Equal(1, saved.SkippedVersion);
    }

    [Fact]
    public async Task CompletingTutorialPersistsCompletedVersion()
    {
        var store = new MemoryProgressStore();
        var coordinator = CreateCoordinator(store, version: 3);
        await coordinator.InitializeAsync();

        await coordinator.NextAsync();
        Assert.Equal("two", coordinator.Current.Step?.Id);

        await coordinator.NextAsync();

        Assert.False(coordinator.Current.IsActive);
        TutorialProgress saved = await store.GetAsync(AppTutorialCatalog.AppIntroId);
        Assert.Equal(3, saved.CompletedVersion);
        Assert.Null(saved.LastStepId);
    }

    [Fact]
    public async Task NewTutorialVersionAppearsAfterOlderVersionWasCompleted()
    {
        var store = new MemoryProgressStore();
        await store.SaveAsync(new TutorialProgress(
            AppTutorialCatalog.AppIntroId,
            CompletedVersion: 1));

        var coordinator = CreateCoordinator(store, version: 2);
        await coordinator.InitializeAsync();

        Assert.True(coordinator.Current.IsActive);
        Assert.Equal(2, coordinator.Current.TutorialVersion);
    }

    [Fact]
    public async Task ExplicitRestartReplaysCompletedTutorial()
    {
        var store = new MemoryProgressStore();
        await store.SaveAsync(new TutorialProgress(
            AppTutorialCatalog.AppIntroId,
            CompletedVersion: 1));

        var coordinator = CreateCoordinator(store, version: 1);
        await coordinator.RestartAsync(AppTutorialCatalog.AppIntroId);

        Assert.True(coordinator.Current.IsActive);
        Assert.Equal("one", coordinator.Current.Step?.Id);
    }

    [Fact]
    public async Task ResumeReturnsToLastPersistedStep()
    {
        var store = new MemoryProgressStore();
        await store.SaveAsync(new TutorialProgress(
            AppTutorialCatalog.AppIntroId,
            LastStepId: "two"));

        var coordinator = CreateCoordinator(store, version: 1);
        await coordinator.InitializeAsync();

        Assert.Equal("two", coordinator.Current.Step?.Id);
        Assert.Equal(1, coordinator.Current.CurrentIndex);
    }

    [Fact]
    public async Task SnapshotUsesFeatureReadinessForCurrentStep()
    {
        var store = new MemoryProgressStore();
        var coordinator = new TutorialCoordinator(
            new TestCatalog(1),
            new TestReadiness(TutorialFeatureState.ComingLater),
            store);

        await coordinator.InitializeAsync();

        Assert.Equal(TutorialFeatureState.ComingLater, coordinator.Current.FeatureState);
    }

    [Fact]
    public void BuiltInFirstJobTutorialPlacesEngineStartAndTaxiBeforeFly()
    {
        var catalog = new AppTutorialCatalog();

        TutorialDefinition firstJob =
            Assert.IsType<TutorialDefinition>(
                catalog.Get(AppTutorialCatalog.FirstJobId));

        string[] ids =
            firstJob.Steps
                .Select(step => step.Id)
                .ToArray();

        Assert.Equal(3, firstJob.Version);
        Assert.Equal(10, ids.Length);
        Assert.Equal(
            new[] { "job-prepare", "job-engine-start", "job-taxi-out", "job-fly" },
            ids[4..8]);
    }

    [Fact]
    public void BuiltInCatalogContainsSpecializedTutorialPreviews()
    {
        var catalog = new AppTutorialCatalog();

        Assert.NotNull(catalog.Get(AppTutorialCatalog.BannerTowId));
        Assert.NotNull(catalog.Get(AppTutorialCatalog.CarrierTakeoffId));
        Assert.NotNull(catalog.Get(AppTutorialCatalog.CarrierLandingId));
        Assert.Equal(8, catalog.Get(AppTutorialCatalog.BannerTowId)?.Steps.Count);
        Assert.Equal(5, catalog.Get(AppTutorialCatalog.CarrierTakeoffId)?.Steps.Count);
        Assert.Equal(6, catalog.Get(AppTutorialCatalog.CarrierLandingId)?.Steps.Count);
    }

    private static TutorialCoordinator CreateCoordinator(
        MemoryProgressStore store,
        int version) =>
        new(
            new TestCatalog(version),
            new TestReadiness(TutorialFeatureState.Available),
            store);

    private sealed class TestCatalog(int version) : ITutorialCatalog
    {
        public TutorialDefinition? Get(string tutorialId)
        {
            if (!string.Equals(
                    tutorialId,
                    AppTutorialCatalog.AppIntroId,
                    StringComparison.Ordinal))
            {
                return null;
            }

            return new TutorialDefinition(
                AppTutorialCatalog.AppIntroId,
                version,
                [
                    new TutorialStep("one", 1, "One", "Body", "dashboard", null, "test"),
                    new TutorialStep("two", 1, "Two", "Body", "jobs", null, "test")
                ]);
        }
    }

    private sealed class TestReadiness(TutorialFeatureState state) : ITutorialFeatureReadiness
    {
        public TutorialFeatureState GetState(string featureKey) => state;
    }

    private sealed class MemoryProgressStore : ITutorialProgressStore
    {
        private readonly Dictionary<string, TutorialProgress> _progress = new(StringComparer.Ordinal);

        public Task<TutorialProgress> GetAsync(
            string tutorialId,
            CancellationToken cancellationToken = default)
        {
            TutorialProgress value = _progress.TryGetValue(tutorialId, out TutorialProgress? saved)
                ? saved
                : new TutorialProgress(tutorialId);
            return Task.FromResult(value);
        }

        public Task SaveAsync(
            TutorialProgress progress,
            CancellationToken cancellationToken = default)
        {
            _progress[progress.TutorialId] = progress;
            return Task.CompletedTask;
        }
    }
}
