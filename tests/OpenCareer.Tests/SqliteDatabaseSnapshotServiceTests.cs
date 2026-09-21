using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteDatabaseSnapshotServiceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SnapshotPreservesMilitaryCampaignAtPointInTime()
    {
        string dir = TempDir();

        try
        {
            string livePath = Path.Combine(dir, "opencareer.db");
            string snapshotPath = Path.Combine(dir, "backup.db");

            var liveStore = Store(livePath);
            ConflictCampaignCheckpoint checkpoint = Checkpoint("backup-campaign");

            ConflictCampaignStoreRecord first =
                await liveStore.SaveAsync(checkpoint, expectedRevision: null);

            var snapshots = new SqliteDatabaseSnapshotService(
                new OpenCareerDatabaseOptions(livePath));

            await snapshots.CreateSnapshotAsync(snapshotPath);

            ConflictCampaignStoreRecord second =
                await liveStore.SaveAsync(
                    checkpoint with
                    {
                        SavedAt = checkpoint.SavedAt.AddMinutes(5)
                    },
                    first.Revision);

            Assert.Equal(2, second.Revision);

            var snapshotStore = Store(snapshotPath);
            ConflictCampaignStoreRecord? recovered =
                await snapshotStore.LoadAsync(checkpoint.CampaignId);

            Assert.NotNull(recovered);
            Assert.Equal(1, recovered!.Revision);
            Assert.Equal(
                checkpoint.CampaignId,
                recovered.Checkpoint.CampaignId);
            Assert.Equal(
                checkpoint.MilitaryCareer,
                recovered.Checkpoint.MilitaryCareer);
            Assert.Equal(
                checkpoint.PlayerCombatState,
                recovered.Checkpoint.PlayerCombatState);
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    [Fact]
    public async Task SnapshotPassesSqliteIntegrityValidation()
    {
        string dir = TempDir();

        try
        {
            string livePath = Path.Combine(dir, "opencareer.db");
            string snapshotPath = Path.Combine(dir, "backup.db");

            await Store(livePath).SaveAsync(
                Checkpoint("integrity-campaign"),
                expectedRevision: null);

            var snapshots = new SqliteDatabaseSnapshotService(
                new OpenCareerDatabaseOptions(livePath));

            await snapshots.CreateSnapshotAsync(snapshotPath);
            await SqliteDatabaseSnapshotService.ValidateSnapshotAsync(
                snapshotPath);

            Assert.True(File.Exists(snapshotPath));
            Assert.True(new FileInfo(snapshotPath).Length > 0);
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    [Fact]
    public async Task SnapshotCannotOverwriteLiveDatabase()
    {
        string dir = TempDir();

        try
        {
            string livePath = Path.Combine(dir, "opencareer.db");

            await Store(livePath).SaveAsync(
                Checkpoint("same-path"),
                expectedRevision: null);

            var snapshots = new SqliteDatabaseSnapshotService(
                new OpenCareerDatabaseOptions(livePath));

            await Assert.ThrowsAsync<ArgumentException>(
                () => snapshots.CreateSnapshotAsync(livePath));
        }
        finally
        {
            DeleteTempDirectory(dir);
        }
    }

    private static ConflictCampaignCheckpoint Checkpoint(
        string campaignId)
    {
        var friendly = new GroundUnitState(
            Guid.Parse("71000000-0000-0000-0000-000000000001"),
            ConflictSide.Friendly,
            GroundUnitRole.Command,
            new GeoPoint(34.95, -97.05),
            Strength: 0.85,
            Readiness: 0.90,
            Pressure: 0,
            IsMobile: false);

        var hostile = new GroundUnitState(
            Guid.Parse("72000000-0000-0000-0000-000000000001"),
            ConflictSide.Hostile,
            GroundUnitRole.Armor,
            new GeoPoint(35.05, -96.95),
            Strength: 0.80,
            Readiness: 0.82,
            Pressure: 0,
            IsMobile: true);

        ConflictWorldState world = ConflictWorldState.Create(
            "FICTIONAL-BACKUP",
            theaterSeed: 0xBACC0FUL,
            updatedAt: Epoch,
            units: new[] { friendly, hostile },
            sectors: new[]
            {
                new ConflictSectorState(
                    "BACKUP-S1",
                    new GeoPoint(35, -97),
                    FriendlyControl: 0.52,
                    IntelligenceConfidence: 0.66)
            });

        return ConflictCampaignCheckpoint.Create(
            campaignId,
            world,
            new MilitaryCareerState(
                MilitaryAffiliation.Reserve,
                MilitaryQualification.MilitaryFlight
                    | MilitaryQualification.Patrol,
                Trust: 0.58,
                SuccessfulOperations: 2,
                FailedOperations: 1),
            new PlayerCombatState(
                AirframeDamage: 0.06,
                PropulsionDamage: 0.02,
                SystemsDamage: 0.03),
            combatSupportMissions: null,
            areaSupportMissions: null,
            airOperationMissions: null,
            savedAt: Epoch);
    }

    private static SqliteConflictCampaignStore Store(string path) =>
        new(
            new OpenCareerDatabaseOptions(path),
            NullLogger<SqliteConflictCampaignStore>.Instance);

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
            // Cleanup must not make a passing SQLite assertion platform-specific.
        }
    }
}
