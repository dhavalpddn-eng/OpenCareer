namespace OpenCareer.Application.Settings;

public interface IAppSettingsService
{
    AppPreferences Current { get; }

    event EventHandler? Changed;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(
        AppPreferences preferences,
        CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);
}
