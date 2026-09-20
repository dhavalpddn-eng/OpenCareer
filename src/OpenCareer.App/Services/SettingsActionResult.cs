namespace OpenCareer.App.Services;

public sealed record SettingsActionResult(
    bool Success,
    string Message,
    string? Path = null);
