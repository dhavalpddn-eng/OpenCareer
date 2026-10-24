using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
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

    private static SqliteBackupArchiveRestoreService RestoreService(
        string livePath)
    {
        var options = new OpenCareerDatabaseOptions(livePath);
        return new SqliteBackupArchiveRestoreService(
            options,
            new SqliteDatabaseSnapshotService(options),
            NullLogger<SqliteBackupArchiveRestoreService>.Instance);
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
