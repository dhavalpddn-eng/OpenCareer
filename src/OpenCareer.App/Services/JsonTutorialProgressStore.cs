using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Tutorials;

namespace OpenCareer.App.Services;

public sealed class JsonTutorialProgressStore : ITutorialProgressStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<JsonTutorialProgressStore> _logger;
    private readonly OpenCareerDataPaths _paths;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true
    };

    public JsonTutorialProgressStore(
        ILogger<JsonTutorialProgressStore> logger,
        OpenCareerDataPaths paths)
    {
        _logger = logger;
        _paths = paths;
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
        if (!File.Exists(_paths.TutorialPreferencesFile))
            return new PreferencesDocument();

        PreferencesDocument? document;
        try
        {
            await using FileStream stream = File.OpenRead(_paths.TutorialPreferencesFile);
            document = await JsonSerializer
                .DeserializeAsync<PreferencesDocument>(
                    stream,
                    _serializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Tutorial preference file is invalid: {Path}",
                _paths.TutorialPreferencesFile);
            throw;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "Unable to read tutorial preferences: {Path}",
                _paths.TutorialPreferencesFile);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                ex,
                "Tutorial preferences are not readable: {Path}",
                _paths.TutorialPreferencesFile);
            throw;
        }

        if (document is null || document.Tutorials is null)
        {
            _logger.LogWarning(
                "Tutorial preference file has no usable progress document: {Path}",
                _paths.TutorialPreferencesFile);
            throw new InvalidDataException(
                "The tutorial preference file has no usable progress document.");
        }

        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            _logger.LogWarning(
                "Tutorial preference schema {SchemaVersion} is unsupported at {Path}.",
                document.SchemaVersion,
                _paths.TutorialPreferencesFile);
            throw new InvalidDataException(
                $"Tutorial preference schema {document.SchemaVersion} is unsupported.");
        }

        return document;
    }

    private async Task WriteAsync(
        PreferencesDocument document,
        CancellationToken cancellationToken)
    {
        string tempPath = _paths.TutorialPreferencesFile + ".tmp";
        bool committed = false;

        try
        {
            _paths.EnsureDirectories();

            await using (FileStream stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    _serializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _paths.TutorialPreferencesFile, overwrite: true);
            committed = true;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "Unable to save tutorial preferences: {Path}",
                _paths.TutorialPreferencesFile);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                ex,
                "Tutorial preferences are not writable: {Path}",
                _paths.TutorialPreferencesFile);
            throw;
        }
        finally
        {
            if (!committed)
                TryDeleteTemporaryFile(tempPath);
        }
    }

    private void TryDeleteTemporaryFile(string tempPath)
    {
        try
        {
            File.Delete(tempPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "Unable to clean temporary tutorial preferences: {Path}",
                tempPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                ex,
                "Temporary tutorial preferences are not writable: {Path}",
                tempPath);
        }
    }

    private sealed class PreferencesDocument
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public Dictionary<string, TutorialProgress> Tutorials { get; set; } =
            new(StringComparer.Ordinal);
    }
}
