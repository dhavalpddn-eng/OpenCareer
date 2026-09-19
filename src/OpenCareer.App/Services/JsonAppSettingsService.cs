using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Settings;

namespace OpenCareer.App.Services;

public sealed class JsonAppSettingsService : IAppSettingsService
{
    private const int CurrentSchemaVersion = 1;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<JsonAppSettingsService> _logger;
    private readonly OpenCareerDataPaths _paths;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true
    };

    private AppPreferences _current = AppPreferences.Default;

    public JsonAppSettingsService(
        ILogger<JsonAppSettingsService> logger,
        OpenCareerDataPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }

    public AppPreferences Current => Volatile.Read(ref _current);

    public event EventHandler? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PreferencesDocument document = await ReadAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _current, document.Preferences ?? AppPreferences.Default);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(
        AppPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        bool changed;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            changed = !Equals(_current, preferences);
            if (!changed)
                return;

            await WriteAsync(
                new PreferencesDocument
                {
                    Preferences = preferences
                },
                cancellationToken).ConfigureAwait(false);

            Volatile.Write(ref _current, preferences);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task ResetAsync(CancellationToken cancellationToken = default) =>
        UpdateAsync(AppPreferences.Default, cancellationToken);

    private async Task<PreferencesDocument> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.SettingsFile))
            return new PreferencesDocument();

        try
        {
            await using FileStream stream = File.OpenRead(_paths.SettingsFile);
            PreferencesDocument? document = await JsonSerializer
                .DeserializeAsync<PreferencesDocument>(
                    stream,
                    _serializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (document is null || document.SchemaVersion != CurrentSchemaVersion)
            {
                _logger.LogWarning(
                    "Ignoring unsupported settings schema at {Path}.",
                    _paths.SettingsFile);
                return new PreferencesDocument();
            }

            return document;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Settings file is invalid: {Path}", _paths.SettingsFile);
            return new PreferencesDocument();
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Unable to read settings: {Path}", _paths.SettingsFile);
            return new PreferencesDocument();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Settings file is not readable: {Path}", _paths.SettingsFile);
            return new PreferencesDocument();
        }
    }

    private async Task WriteAsync(
        PreferencesDocument document,
        CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        string temporaryPath = _paths.SettingsFile + ".tmp";

        try
        {
            await using (FileStream stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    _serializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _paths.SettingsFile, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Preserve the original settings write error.
        }
    }

    private sealed class PreferencesDocument
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public AppPreferences? Preferences { get; set; } = AppPreferences.Default;
    }
}
