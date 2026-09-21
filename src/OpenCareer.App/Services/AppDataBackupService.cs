using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OpenCareer.App.Services;

public sealed class AppDataBackupService(
    OpenCareerDataPaths paths,
    ILogger<AppDataBackupService> logger)
{
    public async Task<SettingsActionResult> CreateBackupAsync(
        CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();

        string fileName =
            $"opencareer-backup-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.zip";
        string destination = Path.Combine(paths.BackupsFolder, fileName);
        string temporary = destination + ".tmp";

        try
        {
            var includedFiles = new List<string>();

            await using (FileStream output = File.Create(temporary))
            {
                using (var archive = new ZipArchive(
                           output,
                           ZipArchiveMode.Create,
                           leaveOpen: true))
                {
                    foreach (string file in EnumerateBackupFiles())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string relative = Path.GetRelativePath(paths.Root, file);
                        ZipArchiveEntry entry = archive.CreateEntry(
                            relative,
                            CompressionLevel.Optimal);

                        await using Stream destinationStream = entry.Open();
                        await using var sourceStream = new FileStream(
                            file,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);

                        await sourceStream
                            .CopyToAsync(destinationStream, cancellationToken)
                            .ConfigureAwait(false);

                        includedFiles.Add(relative);
                    }

                    ZipArchiveEntry manifestEntry =
                        archive.CreateEntry("backup-manifest.json");

                    await using Stream manifestStream = manifestEntry.Open();
                    await JsonSerializer.SerializeAsync(
                        manifestStream,
                        new
                        {
                            schemaVersion = 1,
                            createdAt = DateTimeOffset.Now,
                            appDataRoot = "OpenCareer",
                            includedFiles,
                            note = "Current local application-data backup. SQLite career-save-consistent backup will be added with persistent FlightSession storage."
                        },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, destination, overwrite: false);
            logger.LogInformation(
                "Created OpenCareer local-data backup at {Path}.",
                destination);

            return new SettingsActionResult(
                true,
                $"Backup created: {fileName}",
                destination);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporary);
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDelete(temporary);
            logger.LogError(ex, "Failed to create OpenCareer local-data backup.");
            return new SettingsActionResult(false, $"Backup failed: {ex.Message}");
        }
    }

    private IEnumerable<string> EnumerateBackupFiles()
    {
        if (!Directory.Exists(paths.Root))
            yield break;

        string backupRoot = Path.GetFullPath(paths.BackupsFolder);
        string diagnosticsRoot = Path.GetFullPath(paths.DiagnosticsFolder);

        foreach (string file in Directory.EnumerateFiles(
                     paths.Root,
                     "*",
                     SearchOption.AllDirectories))
        {
            string fullPath = Path.GetFullPath(file);
            if (IsUnder(fullPath, backupRoot) || IsUnder(fullPath, diagnosticsRoot))
                continue;

            if (fullPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return fullPath;
        }
    }

    private static bool IsUnder(string filePath, string folderPath)
    {
        string normalizedFolder = folderPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return filePath.StartsWith(
            normalizedFolder,
            StringComparison.OrdinalIgnoreCase);
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
            // Preserve the original backup error.
        }
    }
}
