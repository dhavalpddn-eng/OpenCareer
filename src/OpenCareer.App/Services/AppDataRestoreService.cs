using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.App.Services;

public sealed class AppDataRestoreService(
    OpenCareerDataPaths paths,
    SqliteBackupArchiveRestoreService restore,
    ILogger<AppDataRestoreService> logger)
{
    public bool HasPendingDatabaseRestore =>
        File.Exists(paths.PendingDatabaseRestoreFile);

    public async Task<SettingsActionResult> StageDatabaseRestoreAsync(
        string backupArchivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupArchivePath);
        paths.EnsureDirectories();

        try
        {
            OpenCareerBackupManifest manifest = await restore
                .StageRestoreAsync(
                    backupArchivePath,
                    paths.PendingDatabaseRestoreFile,
                    cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Staged OpenCareer database restore from {BackupArchive}; backup created at {CreatedAt}.",
                backupArchivePath,
                manifest.CreatedAt);

            return new SettingsActionResult(
                true,
                "Backup validated and staged. Restart OpenCareer to apply the restored database.",
                paths.PendingDatabaseRestoreFile);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or NotSupportedException
                or SqliteException)
        {
            logger.LogError(
                ex,
                "Failed to stage OpenCareer database restore from {BackupArchive}.",
                backupArchivePath);

            return new SettingsActionResult(
                false,
                $"Restore staging failed: {ex.Message}");
        }
    }

    public async Task<bool> ApplyPendingDatabaseRestoreAsync(
        CancellationToken cancellationToken = default)
    {
        if (!HasPendingDatabaseRestore)
            return false;

        await restore
            .ApplyStagedRestoreAsync(
                paths.PendingDatabaseRestoreFile,
                cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Applied pending OpenCareer database restore.");

        return true;
    }
}
