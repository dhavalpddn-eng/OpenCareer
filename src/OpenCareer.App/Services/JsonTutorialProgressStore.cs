using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Tutorials;

namespace OpenCareer.App.Services;

public sealed class JsonTutorialProgressStore : ITutorialProgressStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<JsonTutorialProgressStore> _logger;
    private readonly string _filePath;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true
    };

    public JsonTutorialProgressStore(ILogger<JsonTutorialProgressStore> logger)
    {
        _logger = logger;
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _filePath = Path.Combine(root, "OpenCareer", "ui-preferences.json");
    }

    public async Task<TutorialProgress> GetAsync(
        string tutorialId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PreferencesDocument document = await ReadAsync(cancellationToken).ConfigureAwait(false);
            return document.Tutorials.TryGetValue(tutorialId, out TutorialProgress? progress)
                ? progress
                : new TutorialProgress(tutorialId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        TutorialProgress progress,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PreferencesDocument document = await ReadAsync(cancellationToken).ConfigureAwait(false);
            document.Tutorials[progress.TutorialId] = progress;
            await WriteAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PreferencesDocument> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return new PreferencesDocument();

        try
        {
            await using FileStream stream = File.OpenRead(_filePath);
            PreferencesDocument? document = await JsonSerializer
                .DeserializeAsync<PreferencesDocument>(
                    stream,
                    _serializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (document is null || document.SchemaVersion != CurrentSchemaVersion)
            {
                _logger.LogWarning(
                    "Ignoring unsupported tutorial preference schema at {Path}.",
                    _filePath);
                return new PreferencesDocument();
            }

            return document;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Tutorial preference file is invalid: {Path}", _filePath);
            return new PreferencesDocument();
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Unable to read tutorial preferences: {Path}", _filePath);
            return new PreferencesDocument();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Tutorial preferences are not readable: {Path}", _filePath);
            return new PreferencesDocument();
        }
    }

    private async Task WriteAsync(
        PreferencesDocument document,
        CancellationToken cancellationToken)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = _filePath + ".tmp";
            await using (FileStream stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    _serializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Unable to save tutorial preferences: {Path}", _filePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Tutorial preferences are not writable: {Path}", _filePath);
        }
    }

    private sealed class PreferencesDocument
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public Dictionary<string, TutorialProgress> Tutorials { get; set; } =
            new(StringComparer.Ordinal);
    }
}
