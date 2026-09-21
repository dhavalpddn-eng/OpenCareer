namespace OpenCareer.Application.Settings;

public enum MeasurementSystem
{
    Aviation,
    Metric
}

public enum InputHintPreference
{
    Both,
    ControllerFirst,
    KeyboardFirst
}

public sealed record AppPreferences(
    MeasurementSystem MeasurementSystem = MeasurementSystem.Aviation,
    InputHintPreference InputHints = InputHintPreference.Both,
    bool ShowChecklistEveryFlight = true,
    bool AutomaticallyOfferTutorials = true,
    bool ReduceMotion = false,
    bool AllowOptionalOnlineServices = false)
{
    public static AppPreferences Default { get; } = new();
}
