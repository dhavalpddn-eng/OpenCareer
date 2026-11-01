using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteBackupArchiveRestoreService
{
    private readonly OpenCareerDatabaseOptions _options;
    private readonly SqliteDatabaseSnapshotService _snapshots;
    private readonly ILogger<SqliteBackupArchiveRestoreService> _logger;
    private readonly Action<string> _deleteDatabaseSidecars;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public SqliteBackupArchiveRestoreService(
        OpenCareerDatabaseOptions options,
        SqliteDatabaseSnapshotService snapshots,
        ILogger<SqliteBackupArchiveRestoreService> logger)
        : this(
            options,
            snapshots,
            logger,
            DeleteDatabaseSidecarsOrThrow)
    {
    }

    internal SqliteBackupArchiveRestoreService(
        OpenCareerDatabaseOptions options,
        SqliteDatabaseSnapshotService snapshots,
        ILogger<SqliteBackupArchiveRestoreService> logger,
        Action<string> deleteDatabaseSidecars)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deleteDatabaseSidecars = deleteDatabaseSidecars
            ?? throw new ArgumentNullException(nameof(deleteDatabaseSidecars));
    }

    public async Task<OpenCareerBackupManifest> StageRestoreAsync(
        string backupArchivePath,
        string stagedDatabasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupArchivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedDatabasePath);

        string archivePath = Path.GetFullPath(backupArchivePath);
        string stagePath = Path.GetFullPath(stagedDatabasePath);
        string databasePath = Path.GetFullPath(_options.DatabasePath);

        if (!File.Exists(archivePath))
            throw new FileNotFoundException("OpenCareer backup archive was not found.", archivePath);

        if (string.Equals(stagePath, archivePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Restore staging path must differ from the backup archive.",
                nameof(stagedDatabasePath));
        }

        if (string.Equals(stagePath, databasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Restore staging path must differ from the live database.",
                nameof(stagedDatabasePath));
        }

        string? stageDirectory = Path.GetDirectoryName(stagePath);
        if (!string.IsNullOrWhiteSpace(stageDirectory))
            Directory.CreateDirectory(stageDirectory);

        string candidate = stagePath + $".candidate-{Guid.NewGuid():N}.tmp";

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            OpenCareerBackupManifest manifest =
                await ExtractAndValidateArchiveAsync(
                    archivePath,
                    candidate,
                    cancellationToken).ConfigureAwait(false);

            if (!manifest.SqliteSnapshotIncluded)
            {
                throw new InvalidDataException(
                    "Backup archive does not contain an OpenCareer SQLite snapshot.");
            }

            await SqliteDatabaseSnapshotService
                .ValidateSnapshotAsync(candidate, cancellationToken)
                .ConfigureAwait(false);

            File.Move(candidate, stagePath, overwrite: true);

            _logger.LogInformation(
                "Validated and staged OpenCareer SQLite restore from {BackupArchive} at {StagePath}.",
                archivePath,
                stagePath);

            return manifest;
        }
        finally
        {
            TryDelete(candidate);
            _gate.Release();
        }
    }

    public async Task ApplyStagedRestoreAsync(
        string stagedDatabasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedDatabasePath);

        string stagePath = Path.GetFullPath(stagedDatabasePath);
        string databasePath = Path.GetFullPath(_options.DatabasePath);

        if (string.Equals(stagePath, databasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Restore staging path must differ from the live database.",
                nameof(stagedDatabasePath));
        }

        string? databaseDirectory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(databaseDirectory))
            Directory.CreateDirectory(databaseDirectory);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        string rollbackSnapshot =
            databasePath + $".rollback-{Guid.NewGuid():N}.tmp";
        bool liveDatabaseExists = File.Exists(databasePath);
        bool preserveRollbackSnapshot = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await SqliteDatabaseSnapshotService
                .ValidateSnapshotAsync(stagePath, cancellationToken)
                .ConfigureAwait(false);

            if (liveDatabaseExists)
            {
                await _snapshots
                    .CreateSnapshotAsync(rollbackSnapshot, cancellationToken)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                SqliteConnection.ClearAllPools();
                _deleteDatabaseSidecars(databasePath);

                await CopyAndValidateStagedSnapshotAsync(
                        stagePath,
                        databasePath)
                    .ConfigureAwait(false);

                File.Delete(stagePath);
            }
            catch
            {
                SqliteConnection.ClearAllPools();
                DeleteDatabaseSidecarsBestEffort(databasePath);

                if (liveDatabaseExists && File.Exists(rollbackSnapshot))
                {
                    preserveRollbackSnapshot = true;

                    try
                    {
                        await CopyAndValidateRollbackSnapshotAsync(
                                rollbackSnapshot,
                                databasePath)
                            .ConfigureAwait(false);

                        preserveRollbackSnapshot = false;
                    }
                    catch (Exception rollbackException)
                    {
                        _logger.LogError(
                            rollbackException,
                            "OpenCareer database rollback could not be validated. The rollback snapshot was preserved at {RollbackSnapshotPath} for recovery.",
                            rollbackSnapshot);

                        throw;
                    }
                }
                else
                {
                    TryDelete(databasePath);
                }

                throw;
            }

            TryDelete(rollbackSnapshot);

            _logger.LogInformation(
                "Applied staged OpenCareer SQLite restore to {DatabasePath}.",
                databasePath);
        }
        finally
        {
            if (!preserveRollbackSnapshot)
                TryDelete(rollbackSnapshot);

            _gate.Release();
        }
    }

    public async Task<bool> CancelStagedRestoreAsync(
        string stagedDatabasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedDatabasePath);

        string stagePath = Path.GetFullPath(stagedDatabasePath);
        string databasePath = Path.GetFullPath(_options.DatabasePath);

        if (string.Equals(stagePath, databasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Restore staging path must differ from the live database.",
                nameof(stagedDatabasePath));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(stagePath))
                return false;

            File.Delete(stagePath);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static async Task CopyAndValidateRollbackSnapshotAsync(
        string rollbackSnapshotPath,
        string databasePath)
    {
        File.Copy(
            rollbackSnapshotPath,
            databasePath,
            overwrite: true);

        await SqliteDatabaseSnapshotService
            .ValidateSnapshotAsync(databasePath, CancellationToken.None)
            .ConfigureAwait(false);
    }

    internal static async Task CopyAndValidateStagedSnapshotAsync(
        string stagedSnapshotPath,
        string databasePath)
    {
        File.Copy(
            stagedSnapshotPath,
            databasePath,
            overwrite: true);

        await SqliteDatabaseSnapshotService
            .ValidateSnapshotAsync(databasePath, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task<OpenCareerBackupManifest> ExtractAndValidateArchiveAsync(
        string archivePath,
        string restoreCandidate,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Read,
            leaveOpen: false);

        ZipArchiveEntry manifestEntry = GetUniqueRootEntry(
            archive,
            OpenCareerBackupManifest.ManifestEntryName);

        OpenCareerBackupManifest manifest;
        try
        {
            await using Stream manifestStream = manifestEntry.Open();
            manifest = await JsonSerializer
                .DeserializeAsync<OpenCareerBackupManifest>(
                    manifestStream,
                    _jsonOptions,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    "Backup manifest could not be read.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Backup manifest could not be read.",
                ex);
        }

        manifest.Validate();

        if (!manifest.SqliteSnapshotIncluded)
            return manifest;

        ZipArchiveEntry databaseEntry = GetUniqueRootEntry(
            archive,
            OpenCareerBackupManifest.DatabaseEntryName);

        if (databaseEntry.Length <= 0)
            throw new InvalidDataException("Backup SQLite snapshot is empty.");

        await using Stream source = databaseEntry.Open();
        await using var destination = new FileStream(
            restoreCandidate,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

        await source.CopyToAsync(destination, cancellationToken)
            .ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken)
            .ConfigureAwait(false);

        return manifest;
    }

    private static ZipArchiveEntry GetUniqueRootEntry(
        ZipArchive archive,
        string entryName)
    {
        ZipArchiveEntry[] matches = archive.Entries
            .Where(entry =>
                string.Equals(
                    entry.FullName.Replace('\\', '/'),
                    entryName,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidDataException(
                $"Backup archive is missing required entry '{entryName}'."),
            _ => throw new InvalidDataException(
                $"Backup archive contains duplicate '{entryName}' entries.")
        };
    }

    private static void DeleteDatabaseSidecarsOrThrow(string databasePath)
    {
        DeleteIfExists(databasePath + "-wal");
        DeleteIfExists(databasePath + "-shm");
    }

    private static void DeleteDatabaseSidecarsBestEffort(string databasePath)
    {
        TryDelete(databasePath + "-wal");
        TryDelete(databasePath + "-shm");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
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
            // Cleanup must not hide the primary restore result.
        }
    }
}
