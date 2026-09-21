using OpenCareer.Application.Tutorials;

namespace OpenCareer.Tests;

public sealed class FirstSimulatorConnectionTutorialTests
{
    [Fact]
    public void CatalogDefinesOneStepConnectionTutorial()
    {
        TutorialDefinition tutorial =
            Assert.IsType<TutorialDefinition>(
                new AppTutorialCatalog().Get(
                    AppTutorialCatalog.FirstSimulatorConnectionId));

        Assert.Equal(1, tutorial.Version);

        TutorialStep step = Assert.Single(tutorial.Steps);
        Assert.Equal("simulator-connected", step.Id);
        Assert.Equal("dashboard", step.NavigationTag);
        Assert.Equal("shell", step.FeatureKey);
    }

    [Fact]
    public async Task PendingConnectionTutorialStartsOnce()
    {
        var store = new MemoryProgressStore();
        var coordinator = Coordinator(store);

        bool started = await coordinator.TryStartPendingAsync(
            AppTutorialCatalog.FirstSimulatorConnectionId);

        Assert.True(started);
        Assert.True(coordinator.Current.IsActive);
        Assert.Equal(
            AppTutorialCatalog.FirstSimulatorConnectionId,
            coordinator.Current.TutorialId);

        await coordinator.NextAsync();

        Assert.False(coordinator.Current.IsActive);

        TutorialProgress saved = await store.GetAsync(
            AppTutorialCatalog.FirstSimulatorConnectionId);

        Assert.Equal(1, saved.CompletedVersion);

        bool restarted = await coordinator.TryStartPendingAsync(
            AppTutorialCatalog.FirstSimulatorConnectionId);

        Assert.False(restarted);
        Assert.False(coordinator.Current.IsActive);
    }

    [Fact]
    public async Task PendingConnectionTutorialDoesNotInterruptActiveTutorial()
    {
        var store = new MemoryProgressStore();
        var coordinator = Coordinator(store);

        await coordinator.StartAsync(
            AppTutorialCatalog.AppIntroId,
            resume: false);

        string? activeId = coordinator.Current.TutorialId;
        string? activeStep = coordinator.Current.Step?.Id;

        bool started = await coordinator.TryStartPendingAsync(
            AppTutorialCatalog.FirstSimulatorConnectionId);

        Assert.False(started);
        Assert.Equal(activeId, coordinator.Current.TutorialId);
        Assert.Equal(activeStep, coordinator.Current.Step?.Id);
    }

    [Fact]
    public async Task SkippedConnectionTutorialIsNotOfferedAgain()
    {
        var store = new MemoryProgressStore();

        await store.SaveAsync(
            new TutorialProgress(
                AppTutorialCatalog.FirstSimulatorConnectionId,
                SkippedVersion: 1));

        var coordinator = Coordinator(store);

        bool started = await coordinator.TryStartPendingAsync(
            AppTutorialCatalog.FirstSimulatorConnectionId);

        Assert.False(started);
        Assert.False(coordinator.Current.IsActive);
    }

    private static TutorialCoordinator Coordinator(
        ITutorialProgressStore store) =>
        new(
            new AppTutorialCatalog(),
            new AlwaysAvailableReadiness(),
            store);

    private sealed class AlwaysAvailableReadiness :
        ITutorialFeatureReadiness
    {
        public TutorialFeatureState GetState(
            string featureKey) =>
            TutorialFeatureState.Available;
    }

    private sealed class MemoryProgressStore :
        ITutorialProgressStore
    {
        private readonly Dictionary<string, TutorialProgress> _values =
            new(StringComparer.Ordinal);

        public Task<TutorialProgress> GetAsync(
            string tutorialId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _values.TryGetValue(
                    tutorialId,
                    out TutorialProgress? progress)
                    ? progress
                    : new TutorialProgress(tutorialId));
        }

        public Task SaveAsync(
            TutorialProgress progress,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[progress.TutorialId] = progress;
            return Task.CompletedTask;
        }
    }
}
