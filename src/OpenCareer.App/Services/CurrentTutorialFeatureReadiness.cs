using OpenCareer.Application.Tutorials;

namespace OpenCareer.App.Services;

public sealed class CurrentTutorialFeatureReadiness : ITutorialFeatureReadiness
{
    private static readonly IReadOnlyDictionary<string, TutorialFeatureState> States =
        new Dictionary<string, TutorialFeatureState>(StringComparer.Ordinal)
        {
            ["shell"] = TutorialFeatureState.Available,
            ["dashboard"] = TutorialFeatureState.Available,
            ["career-loop"] = TutorialFeatureState.Available,
            ["current-flight"] = TutorialFeatureState.Available,
            ["settings"] = TutorialFeatureState.Available,

            ["dispatch"] = TutorialFeatureState.ComingLater,
            ["jobs"] = TutorialFeatureState.ComingLater,
            ["world"] = TutorialFeatureState.ComingLater,
            ["aircraft"] = TutorialFeatureState.ComingLater,
            ["bases"] = TutorialFeatureState.ComingLater,
            ["maintenance"] = TutorialFeatureState.ComingLater,
            ["company"] = TutorialFeatureState.ComingLater,
            ["finances"] = TutorialFeatureState.ComingLater,
            ["markets"] = TutorialFeatureState.ComingLater,
            ["military"] = TutorialFeatureState.ComingLater,
            ["logbook"] = TutorialFeatureState.ComingLater,
            ["career"] = TutorialFeatureState.ComingLater,
            ["first-job"] = TutorialFeatureState.ComingLater,
            ["mission-banner-tow"] = TutorialFeatureState.ComingLater,
            ["mission-carrier-takeoff"] = TutorialFeatureState.ComingLater,
            ["mission-carrier-landing"] = TutorialFeatureState.ComingLater
        };

    public TutorialFeatureState GetState(string featureKey) =>
        States.TryGetValue(featureKey, out TutorialFeatureState state)
            ? state
            : TutorialFeatureState.Unavailable;
}
