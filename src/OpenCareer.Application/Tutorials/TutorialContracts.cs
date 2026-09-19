namespace OpenCareer.Application.Tutorials;

public interface ITutorialCatalog
{
    TutorialDefinition? Get(string tutorialId);
}

public interface ITutorialFeatureReadiness
{
    TutorialFeatureState GetState(string featureKey);
}

public interface ITutorialProgressStore
{
    Task<TutorialProgress> GetAsync(string tutorialId, CancellationToken cancellationToken = default);

    Task SaveAsync(TutorialProgress progress, CancellationToken cancellationToken = default);
}
