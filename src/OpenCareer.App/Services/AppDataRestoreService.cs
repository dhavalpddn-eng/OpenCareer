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

    public SettingsActionResult CancelPendingDatabaseRestore()
    {
        if (!HasPendingDatabaseRestore)
        {
            return new SettingsActionResult(
                true,
                "No database restore is currently staged.");
        }

        try
        {
            File.Delete(paths.PendingDatabaseRestoreFile);
            logger.LogInformation("Cancelled the pending OpenCareer database restore.");

            return new SettingsActionResult(
                true,
                "Staged database restore cancelled. Current career data will remain active after restart.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Failed to cancel the pending OpenCareer database restore.");

            return new SettingsActionResult(
                false,
                "The staged restore could not be cancelled. Keep OpenCareer open, verify local storage is available, and retry before restarting.");
        }
    }

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
                "The selected backup could not be validated and no restore was staged. Choose another OpenCareer backup or create a diagnostic bundle, then retry.");
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
