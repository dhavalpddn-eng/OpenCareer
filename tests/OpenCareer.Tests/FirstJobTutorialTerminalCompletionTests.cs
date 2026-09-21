using OpenCareer.Application.Tutorials;

namespace OpenCareer.Tests;

public sealed class FirstJobTutorialTerminalCompletionTests
{
    [Fact]
    public void FirstJobEndsAtOperationCompleteUntilDebriefEvidenceExists()
    {
        TutorialDefinition firstJob =
            Assert.IsType<TutorialDefinition>(
                new AppTutorialCatalog().Get(
                    AppTutorialCatalog.FirstJobId));

        Assert.Equal(12, firstJob.Version);
        Assert.Equal("job-complete", firstJob.Steps[^1].Id);
        Assert.DoesNotContain(
            firstJob.Steps,
            static step => step.Id == "job-debrief");
    }

    [Fact]
    public async Task NextFromOperationCompleteFinishesBuiltInTutorial()
    {
        var store = new MemoryProgressStore();

        await store.SaveAsync(
            new TutorialProgress(
                AppTutorialCatalog.FirstJobId,
                LastStepId: "job-complete"));

        var coordinator = new TutorialCoordinator(
            new AppTutorialCatalog(),
            new AlwaysAvailableReadiness(),
            store);

        await coordinator.StartAsync(
            AppTutorialCatalog.FirstJobId,
            resume: true);

        Assert.True(coordinator.Current.IsActive);
        Assert.True(coordinator.Current.IsLastStep);
        Assert.Equal(
            "job-complete",
            coordinator.Current.Step?.Id);

        await coordinator.NextAsync();

        Assert.False(coordinator.Current.IsActive);

        TutorialProgress saved =
            await store.GetAsync(
                AppTutorialCatalog.FirstJobId);

        Assert.Equal(12, saved.CompletedVersion);
        Assert.Null(saved.LastStepId);
    }

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
