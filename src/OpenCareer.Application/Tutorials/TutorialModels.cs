namespace OpenCareer.Application.Tutorials;

public enum TutorialFeatureState
{
    Available,
    Unavailable,
    ComingLater
}

public sealed record TutorialStep(
    string Id,
    int Version,
    string Title,
    string Body,
    string? NavigationTag,
    string? FocusElementKey,
    string FeatureKey,
    bool IsOptional = false);

public sealed record TutorialDefinition(
    string Id,
    int Version,
    IReadOnlyList<TutorialStep> Steps);

public sealed record TutorialProgress(
    string TutorialId,
    int CompletedVersion = 0,
    int SkippedVersion = 0,
    string? LastStepId = null);

public sealed record TutorialSnapshot(
    string? TutorialId,
    int TutorialVersion,
    bool IsActive,
    int CurrentIndex,
    int StepCount,
    TutorialStep? Step,
    TutorialFeatureState FeatureState)
{
    public static TutorialSnapshot Inactive { get; } =
        new(null, 0, false, -1, 0, null, TutorialFeatureState.Unavailable);

    public bool CanGoBack => IsActive && CurrentIndex > 0;
    public bool IsLastStep => IsActive && StepCount > 0 && CurrentIndex == StepCount - 1;
}

public sealed class TutorialNavigationRequestedEventArgs(string navigationTag) : EventArgs
{
    public string NavigationTag { get; } = navigationTag;
}
