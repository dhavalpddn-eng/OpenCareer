using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.App.Services;
using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteBackupArchiveRestoreServiceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StageDoesNotChangeLiveDatabaseUntilApplied()
    {
        string dir = TempDir();

        try
        {
            string livePath = Path.Combine(dir, "live.db");
            string sourcePath = Path.Combine(dir, "source.db");
            string sourceSnapshot = Path.Combine(dir, "source-snapshot.db");
            string archivePath = Path.Combine(dir, "backup.zip");
            string stagedPath = Path.Combine(dir, "pending.db");

            await Store(livePath).SaveAsync(
                Checkpoint("live-campaign"),
                expectedRevision: null);

            await Store(sourcePath).SaveAsync(
                Checkpoint("backup-campaign"),
                expectedRevision: null);

            await new SqliteDatabaseSnapshotService(
                    new OpenCareerDatabaseOptions(sourcePath))
                .CreateSnapshotAsync(sourceSnapshot);

            await CreateBackupArchiveAsync(
                archivePath,
                sourceSnapshot);

            var restore = RestoreService(livePath);

            OpenCareerBackupManifest manifest =
                await restore.StageRestoreAsync(
                    archivePath,
                    stagedPath);

            Assert.True(manifest.SqliteSnapshotIncluded);
            Assert.True(File.Exists(stagedPath));

            var stillLive = await Store(livePath)
                .LoadAsync("live-campaign");

            Assert.NotNull(stillLive);
            Assert.Null(
                await Store(livePath).LoadAsync("backup-campaign"));
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    [Fact]
    public async Task ApplyStagedRestoreReplacesDatabaseWithValidatedBackup()
    {
        string dir = TempDir();

        try
        {
            string livePath = Path.Combine(dir, "live.db");
            string sourcePath = Path.Combine(dir, "source.db");
            string sourceSnapshot = Path.Combine(dir, "source-snapshot.db");
            string archivePath = Path.Combine(dir, "backup.zip");
            string stagedPath = Path.Combine(dir, "pending.db");

            await Store(livePath).SaveAsync(
                Checkpoint("live-campaign"),
                expectedRevision: null);

            ConflictCampaignCheckpoint backupCheckpoint =
                Checkpoint("backup-campaign");

            await Store(sourcePath).SaveAsync(
                backupCheckpoint,
                expectedRevision: null);

            await new SqliteDatabaseSnapshotService(
                    new OpenCareerDatabaseOptions(sourcePath))
                .CreateSnapshotAsync(sourceSnapshot);

            await CreateBackupArchiveAsync(
                archivePath,
                sourceSnapshot);

            var restore = RestoreService(livePath);

            await restore.StageRestoreAsync(
                archivePath,
                stagedPath);

            await restore.ApplyStagedRestoreAsync(stagedPath);

            Assert.False(File.Exists(stagedPath));

            var restoredStore = Store(livePath);
            ConflictCampaignStoreRecord? restored =
                await restoredStore.LoadAsync("backup-campaign");

            Assert.NotNull(restored);
            Assert.Equal(
                backupCheckpoint.MilitaryCareer,
                restored!.Checkpoint.MilitaryCareer);
            Assert.Equal(
                backupCheckpoint.PlayerCombatState,
                restored.Checkpoint.PlayerCombatState);

            Assert.Null(
                await restoredStore.LoadAsync("live-campaign"));

            await SqliteDatabaseSnapshotService
                .ValidateSnapshotAsync(livePath);
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    [Fact]
    public async Task CorruptDatabaseIsRejectedBeforeLiveDatabaseChanges()
    {
        string dir = TempDir();

        try
        {
            string livePath = Path.Combine(dir, "live.db");
            string archivePath = Path.Combine(dir, "bad-backup.zip");
            string stagedPath = Path.Combine(dir, "pending.db");

            await Store(livePath).SaveAsync(
                Checkpoint("live-campaign"),
                expectedRevision: null);

            await CreateBackupArchiveAsync(
                archivePath,
                databaseBytes: "not-a-sqlite-database"u8.ToArray());

            var restore = RestoreService(livePath);

            await Assert.ThrowsAnyAsync<Exception>(
                () => restore.StageRestoreAsync(
                    archivePath,
                    stagedPath));

            Assert.False(File.Exists(stagedPath));

            ConflictCampaignStoreRecord? live =
                await Store(livePath).LoadAsync("live-campaign");

            Assert.NotNull(live);
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    [Fact]
    public async Task ArchiveWithoutSqliteSnapshotCannotBeStaged()
    {
        string dir = TempDir();

        try
        {
            string livePath = Path.Combine(dir, "live.db");
            string archivePath = Path.Combine(dir, "no-db.zip");
            string stagedPath = Path.Combine(dir, "pending.db");

            await Store(livePath).SaveAsync(
                Checkpoint("live-campaign"),
                expectedRevision: null);

            await CreateBackupArchiveWithoutDatabaseAsync(archivePath);

            var restore = RestoreService(livePath);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => restore.StageRestoreAsync(
                    archivePath,
                    stagedPath));

            Assert.False(File.Exists(stagedPath));
            Assert.NotNull(
                await Store(livePath).LoadAsync("live-campaign"));
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    [Theory]
    [InlineData("{ malformed manifest detail must stay technical")]
    [InlineData("{\"schemaVersion\":2,\"appDataRoot\":\"OpenCareer\",\"includedFiles\":[\"opencareer.db\"],\"sqliteSnapshotIncluded\":true,\"note\":\"test\"}")]
    [InlineData("{\"schemaVersion\":2,\"createdAt\":\"2026-09-20T03:00:00Z\",\"appDataRoot\":\"OpenCareer\",\"includedFiles\":null,\"sqliteSnapshotIncluded\":true,\"note\":\"test\"}")]
    [InlineData("{\"schemaVersion\":2,\"createdAt\":\"2026-09-20T03:00:00Z\",\"appDataRoot\":\"OpenCareer\",\"includedFiles\":[\"opencareer.db\"],\"sqliteSnapshotIncluded\":true,\"note\":null}")]
    public async Task InvalidManifestIsRejectedAsImportedData(
        string manifestJson)
    {
        string dir = TempDir();

        try
        {
            string livePath = Path.Combine(dir, "live.db");
            string archivePath = Path.Combine(dir, "bad-manifest.zip");
            string stagedPath = Path.Combine(dir, "pending.db");

            await Store(livePath).SaveAsync(
                Checkpoint("live-campaign"),
                expectedRevision: null);

            await CreateBackupArchiveWithRawManifestAsync(
                archivePath,
                manifestJson);

            InvalidDataException error =
                await Assert.ThrowsAsync<InvalidDataException>(
                    () => RestoreService(livePath).StageRestoreAsync(
                        archivePath,
                        stagedPath));

            Assert.Contains("manifest", error.Message);
            Assert.False(File.Exists(stagedPath));
            Assert.NotNull(
                await Store(livePath).LoadAsync("live-campaign"));
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    [Theory]
    [InlineData("{ parser detail must not reach player-facing text")]
    [InlineData("{\"schemaVersion\":2,\"appDataRoot\":\"OpenCareer\",\"includedFiles\":[\"opencareer.db\"],\"sqliteSnapshotIncluded\":true,\"note\":\"test\"}")]
    public async Task InvalidManifestReturnsSafeRestoreActionFailure(
        string manifestJson)
    {
        string dir = TempDir();

        try
        {
            var paths = new OpenCareerDataPaths(dir);
            paths.EnsureDirectories();
            string archivePath = Path.Combine(
                paths.BackupsFolder,
                "opencareer-backup-bad-manifest.zip");

            await CreateBackupArchiveWithRawManifestAsync(
                archivePath,
                manifestJson);

            var restore = new AppDataRestoreService(
                paths,
                RestoreService(paths.DatabaseFile),
                NullLogger<AppDataRestoreService>.Instance);

            SettingsActionResult result =
                await restore.StageDatabaseRestoreAsync(archivePath);

            Assert.False(result.Success);
            Assert.Contains("could not be validated", result.Message);
            Assert.DoesNotContain("parser detail", result.Message);
            Assert.DoesNotContain("creation timestamp", result.Message);
            Assert.False(File.Exists(paths.PendingDatabaseRestoreFile));
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    [Fact]
    public async Task FailedReplacementKeepsPriorPendingRestoreVisible()
    {
        string dir = TempDir();

        try
        {
            var paths = new OpenCareerDataPaths(dir);
            paths.EnsureDirectories();
            byte[] pendingRestore =
                "existing pending restore must remain recoverable"u8.ToArray();
            await File.WriteAllBytesAsync(
                paths.PendingDatabaseRestoreFile,
                pendingRestore);

            string archivePath = Path.Combine(
                paths.BackupsFolder,
                "opencareer-backup-invalid-replacement.zip");
            await CreateBackupArchiveWithRawManifestAsync(
                archivePath,
                "{ replacement parser detail must stay technical");

            var restore = new AppDataRestoreService(
                paths,
                RestoreService(paths.DatabaseFile),
                NullLogger<AppDataRestoreService>.Instance);

            SettingsActionResult result =
                await restore.StageDatabaseRestoreAsync(archivePath);

            Assert.False(result.Success);
            Assert.Contains("still pending", result.Message);
            Assert.Contains("Cancel", result.Message);
            Assert.DoesNotContain("parser detail", result.Message);
            Assert.Equal(
                pendingRestore,
                await File.ReadAllBytesAsync(
                    paths.PendingDatabaseRestoreFile));
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    [Fact]
    public async Task DataDirectorySetupFailureReturnsSafeRestoreActionFailure()
    {
        string dir = TempDir();

        try
        {
            string blockedRoot = Path.Combine(dir, "blocked-profile-root");
            await File.WriteAllTextAsync(
                blockedRoot,
                "technical path collision detail");

            var paths = new OpenCareerDataPaths(blockedRoot);
            var restore = new AppDataRestoreService(
                paths,
                RestoreService(paths.DatabaseFile),
                NullLogger<AppDataRestoreService>.Instance);

            SettingsActionResult result =
                await restore.StageDatabaseRestoreAsync(
                    Path.Combine(dir, "unused-backup.zip"));

            Assert.False(result.Success);
            Assert.Contains("could not be validated", result.Message);
            Assert.DoesNotContain("blocked-profile-root", result.Message);
            Assert.DoesNotContain("path collision", result.Message);
            Assert.False(File.Exists(paths.PendingDatabaseRestoreFile));
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    [Fact]
    public async Task FailedRollbackValidationPreservesRecoverySnapshot()
    {
        string dir = TempDir();

        try
        {
            string livePath = Path.Combine(dir, "live.db");
            string rollbackPath = Path.Combine(dir, "rollback.db");
            byte[] corruptSnapshot = "not-a-sqlite-database"u8.ToArray();

            await File.WriteAllBytesAsync(
                rollbackPath,
                corruptSnapshot);

            await Assert.ThrowsAnyAsync<Exception>(
                () => SqliteBackupArchiveRestoreService
                    .CopyAndValidateRollbackSnapshotAsync(
                        rollbackPath,
                        livePath));

            Assert.True(File.Exists(rollbackPath));
            Assert.Equal(
                corruptSnapshot,
                await File.ReadAllBytesAsync(rollbackPath));
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    [Fact]
    public async Task FailedPostCopyValidationPreservesStagedSnapshot()
    {
        string dir = TempDir();

        try
        {
            string stagedPath = Path.Combine(dir, "pending.db");
            string livePath = Path.Combine(dir, "live.db");
            byte[] corruptSnapshot = "not-a-sqlite-database"u8.ToArray();

            await File.WriteAllBytesAsync(
                stagedPath,
                corruptSnapshot);

            await Assert.ThrowsAnyAsync<Exception>(
                () => SqliteBackupArchiveRestoreService
                    .CopyAndValidateStagedSnapshotAsync(
                        stagedPath,
                        livePath));

            Assert.True(File.Exists(stagedPath));
            Assert.Equal(
                corruptSnapshot,
                await File.ReadAllBytesAsync(stagedPath));
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    [Fact]
    public async Task SidecarCleanupFailureRestoresLiveDatabaseAndPreservesStage()
    {
        string dir = TempDir();

        try
        {
            string livePath = Path.Combine(dir, "live.db");
            string sourcePath = Path.Combine(dir, "source.db");
            string sourceSnapshot = Path.Combine(dir, "source-snapshot.db");
            string archivePath = Path.Combine(dir, "backup.zip");
            string stagedPath = Path.Combine(dir, "pending.db");

            await Store(livePath).SaveAsync(
                Checkpoint("live-campaign"),
                expectedRevision: null);

            await Store(sourcePath).SaveAsync(
                Checkpoint("backup-campaign"),
                expectedRevision: null);

            await new SqliteDatabaseSnapshotService(
                    new OpenCareerDatabaseOptions(sourcePath))
                .CreateSnapshotAsync(sourceSnapshot);

            await CreateBackupArchiveAsync(
                archivePath,
                sourceSnapshot);

            var restore = RestoreService(livePath);
            await restore.StageRestoreAsync(archivePath, stagedPath);
            byte[] stagedSnapshot = await File.ReadAllBytesAsync(stagedPath);

            var failingRestore = RestoreService(
                livePath,
                _ => throw new IOException("simulated sidecar cleanup failure"));

            IOException error = await Assert.ThrowsAsync<IOException>(
                () => failingRestore.ApplyStagedRestoreAsync(stagedPath));

            Assert.Contains("sidecar cleanup", error.Message);
            Assert.True(File.Exists(stagedPath));
            Assert.Equal(
                stagedSnapshot,
                await File.ReadAllBytesAsync(stagedPath));
            Assert.NotNull(
                await Store(livePath).LoadAsync("live-campaign"));
            Assert.Null(
                await Store(livePath).LoadAsync("backup-campaign"));
            await SqliteDatabaseSnapshotService
                .ValidateSnapshotAsync(livePath);
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    [Fact]
    public async Task CancelWaitsForInProgressRestoreMutation()
    {
        string dir = TempDir();
        var cleanupStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            string livePath = Path.Combine(dir, "live.db");
            string sourcePath = Path.Combine(dir, "source.db");
            string sourceSnapshot = Path.Combine(dir, "source-snapshot.db");
            string archivePath = Path.Combine(dir, "backup.zip");
            string stagedPath = Path.Combine(dir, "pending.db");

            await Store(livePath).SaveAsync(
                Checkpoint("live-campaign"),
                expectedRevision: null);
            await Store(sourcePath).SaveAsync(
                Checkpoint("backup-campaign"),
                expectedRevision: null);
            await new SqliteDatabaseSnapshotService(
                    new OpenCareerDatabaseOptions(sourcePath))
                .CreateSnapshotAsync(sourceSnapshot);
            await CreateBackupArchiveAsync(archivePath, sourceSnapshot);

            var restore = RestoreService(
                livePath,
                _ =>
                {
                    cleanupStarted.TrySetResult(true);
                    releaseCleanup.Task.GetAwaiter().GetResult();
                });
            await restore.StageRestoreAsync(archivePath, stagedPath);

            Task applyTask = Task.Run(
                () => restore.ApplyStagedRestoreAsync(stagedPath));
            Task<bool>? cancelTask = null;

            try
            {
                await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancelTask = restore.CancelStagedRestoreAsync(stagedPath);

                Assert.False(cancelTask.IsCompleted);
            }
            finally
            {
                releaseCleanup.TrySetResult(true);
            }

            await applyTask;
            Assert.NotNull(cancelTask);
            Assert.False(await cancelTask!);
            Assert.False(File.Exists(stagedPath));
            Assert.NotNull(
                await Store(livePath).LoadAsync("backup-campaign"));
        }
        finally
        {
            releaseCleanup.TrySetResult(true);
            DeleteTempDirectory(dir);
        }
    }

    private static SqliteBackupArchiveRestoreService RestoreService(
        string livePath,
        Action<string>? deleteDatabaseSidecars = null)
    {
        var options = new OpenCareerDatabaseOptions(livePath);
        var snapshots = new SqliteDatabaseSnapshotService(options);

        return deleteDatabaseSidecars is null
            ? new SqliteBackupArchiveRestoreService(
                options,
                snapshots,
                NullLogger<SqliteBackupArchiveRestoreService>.Instance)
            : new SqliteBackupArchiveRestoreService(
                options,
                snapshots,
                NullLogger<SqliteBackupArchiveRestoreService>.Instance,
                deleteDatabaseSidecars);
    }

    private static SqliteConflictCampaignStore Store(string path) =>
        new(
            new OpenCareerDatabaseOptions(path),
            NullLogger<SqliteConflictCampaignStore>.Instance);

    private static async Task CreateBackupArchiveAsync(
        string archivePath,
        string databaseSnapshotPath)
    {
        byte[] bytes = await File.ReadAllBytesAsync(
            databaseSnapshotPath);

        await CreateBackupArchiveAsync(
            archivePath,
            bytes);
    }

    private static async Task CreateBackupArchiveAsync(
        string archivePath,
        byte[] databaseBytes)
    {
        var manifest = new OpenCareerBackupManifest(
            OpenCareerBackupManifest.CurrentSchemaVersion,
            Epoch,
            OpenCareerBackupManifest.ExpectedAppDataRoot,
            new[] { OpenCareerBackupManifest.DatabaseEntryName },
            SqliteSnapshotIncluded: true,
            Note: "test");

        manifest.Validate();

        await using FileStream file = File.Create(archivePath);
        using var archive = new ZipArchive(
            file,
            ZipArchiveMode.Create,
            leaveOpen: true);

        ZipArchiveEntry database = archive.CreateEntry(
            OpenCareerBackupManifest.DatabaseEntryName);

        await using (Stream stream = database.Open())
            await stream.WriteAsync(databaseBytes);

        ZipArchiveEntry manifestEntry = archive.CreateEntry(
            OpenCareerBackupManifest.ManifestEntryName);

        await using Stream manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(
            manifestStream,
            manifest);
    }

    private static async Task CreateBackupArchiveWithoutDatabaseAsync(
        string archivePath)
    {
        var manifest = new OpenCareerBackupManifest(
            OpenCareerBackupManifest.CurrentSchemaVersion,
            Epoch,
            OpenCareerBackupManifest.ExpectedAppDataRoot,
            Array.Empty<string>(),
            SqliteSnapshotIncluded: false,
            Note: "test");

        await using FileStream file = File.Create(archivePath);
        using var archive = new ZipArchive(
            file,
            ZipArchiveMode.Create,
            leaveOpen: true);

        ZipArchiveEntry manifestEntry = archive.CreateEntry(
            OpenCareerBackupManifest.ManifestEntryName);

        await using Stream manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(
            manifestStream,
            manifest);
    }

    private static async Task CreateBackupArchiveWithRawManifestAsync(
        string archivePath,
        string manifestJson)
    {
        await using FileStream file = File.Create(archivePath);
        using var archive = new ZipArchive(
            file,
            ZipArchiveMode.Create,
            leaveOpen: true);

        ZipArchiveEntry manifestEntry = archive.CreateEntry(
            OpenCareerBackupManifest.ManifestEntryName);

        await using Stream stream = manifestEntry.Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(manifestJson);
    }

    private static ConflictCampaignCheckpoint Checkpoint(
        string campaignId)
    {
        var friendly = new GroundUnitState(
            Guid.Parse("81000000-0000-0000-0000-000000000001"),
            ConflictSide.Friendly,
            GroundUnitRole.Command,
            new GeoPoint(34.9, -97.1),
            Strength: 0.9,
            Readiness: 0.88,
            Pressure: 0,
            IsMobile: false);

        var hostile = new GroundUnitState(
            Guid.Parse("82000000-0000-0000-0000-000000000001"),
            ConflictSide.Hostile,
            GroundUnitRole.Armor,
            new GeoPoint(35.1, -96.9),
            Strength: 0.8,
            Readiness: 0.84,
            Pressure: 0,
            IsMobile: true);

        ConflictWorldState world = ConflictWorldState.Create(
            "FICTIONAL-RESTORE",
            theaterSeed: 0xA551UL,
            updatedAt: Epoch,
            units: new[] { friendly, hostile },
            sectors: new[]
            {
                new ConflictSectorState(
                    "RESTORE-S1",
                    new GeoPoint(35, -97),
                    FriendlyControl: 0.51,
                    IntelligenceConfidence: 0.64)
            });

        return ConflictCampaignCheckpoint.Create(
            campaignId,
            world,
            new MilitaryCareerState(
                MilitaryAffiliation.Reserve,
                MilitaryQualification.MilitaryFlight
                    | MilitaryQualification.Reconnaissance,
                Trust: 0.61,
                SuccessfulOperations: 3,
                FailedOperations: 1),
            new PlayerCombatState(
                AirframeDamage: 0.04,
                PropulsionDamage: 0.03,
                SystemsDamage: 0.01),
            combatSupportMissions: null,
            areaSupportMissions: null,
            airOperationMissions: null,
            savedAt: Epoch);
    }

    private static string TempDir()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteTempDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // SQLite cleanup must not make assertions platform-specific.
        }
    }
}
