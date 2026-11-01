using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Settings;

namespace OpenCareer.App.Services;

public sealed class JsonAppSettingsService :
    IAppSettingsService,
    IAppSettingsRecoveryState
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
    private bool _initializationFailed;

    public JsonAppSettingsService(
        ILogger<JsonAppSettingsService> logger,
        OpenCareerDataPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }

    public AppPreferences Current => Volatile.Read(ref _current);

    public bool RequiresReset => Volatile.Read(ref _initializationFailed);

    public event EventHandler? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                AppPreferences preferences =
                    await ReadAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _current, preferences);
                Volatile.Write(ref _initializationFailed, false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is JsonException
                    or IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                Volatile.Write(ref _initializationFailed, true);
                throw;
            }
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
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (RequiresReset)
            {
                throw new InvalidDataException(
                    "Application preferences could not be loaded. Reset them before saving changes.");
            }

            changed = !Equals(_current, preferences);
            if (!changed)
                return;

            await WriteAsync(
                new PreferencesDocument
                {
                    Preferences = preferences
                },
                cancellationToken);

            Volatile.Write(ref _current, preferences);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        bool changed;
        bool recovered;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            changed = !Equals(_current, AppPreferences.Default);
            recovered = RequiresReset;
            await WriteAsync(
                new PreferencesDocument
                {
                    Preferences = AppPreferences.Default
                },
                cancellationToken).ConfigureAwait(false);

            Volatile.Write(ref _current, AppPreferences.Default);
            Volatile.Write(ref _initializationFailed, false);
        }
        finally
        {
            _gate.Release();
        }

        if (changed || recovered)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<AppPreferences> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = File.OpenRead(_paths.SettingsFile);
            PreferencesDocument? document = await JsonSerializer
                .DeserializeAsync<PreferencesDocument>(
                    stream,
                    _serializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (document is null ||
                document.Preferences is not AppPreferences preferences)
            {
                _logger.LogWarning(
                    "Settings file has no usable preferences document at {Path}.",
                    _paths.SettingsFile);
                throw new InvalidDataException(
                    "The settings file has no usable preferences document.");
            }

            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                _logger.LogWarning(
                    "Settings schema {SchemaVersion} is unsupported at {Path}.",
                    document.SchemaVersion,
                    _paths.SettingsFile);
                throw new InvalidDataException(
                    $"Settings schema {document.SchemaVersion} is unsupported.");
            }

            ValidatePreferences(preferences);
            return preferences;
        }
        catch (FileNotFoundException)
        {
            return AppPreferences.Default;
        }
        catch (DirectoryNotFoundException)
        {
            return AppPreferences.Default;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Settings file is invalid: {Path}", _paths.SettingsFile);
            throw;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Unable to read settings: {Path}", _paths.SettingsFile);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Settings file is not readable: {Path}", _paths.SettingsFile);
            throw;
        }
    }

    private void ValidatePreferences(AppPreferences preferences)
    {
        if (Enum.IsDefined(preferences.MeasurementSystem) &&
            Enum.IsDefined(preferences.InputHints))
        {
            return;
        }

        _logger.LogWarning(
            "Settings file contains unsupported preference values at {Path}.",
            _paths.SettingsFile);
        throw new InvalidDataException(
            "The settings file contains unsupported preference values.");
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
