using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteConflictCampaignStoreTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RoundTripRestoresReservedMission()
    {
        string dir = TempDir();
        try
        {
            var store = Store(dir);
            var checkpoint = Checkpoint("roundtrip");
            var saved = await store.SaveAsync(checkpoint, null);
            var loaded = await store.LoadAsync(checkpoint.CampaignId);

            Assert.NotNull(loaded);
            Assert.Equal(1, saved.Revision);
            Assert.Equal(checkpoint.CampaignId, loaded!.Checkpoint.CampaignId);
            Assert.Equal(checkpoint.MilitaryCareer, loaded.Checkpoint.MilitaryCareer);
            Assert.Equal(checkpoint.PlayerCombatState, loaded.Checkpoint.PlayerCombatState);
            Assert.Equal(checkpoint.World.TheaterId, loaded.Checkpoint.World.TheaterId);
            Assert.Equal(checkpoint.World.TheaterSeed, loaded.Checkpoint.World.TheaterSeed);
            Assert.Equal(checkpoint.World.Units, loaded.Checkpoint.World.Units);
            Assert.Equal(checkpoint.World.Sectors, loaded.Checkpoint.World.Sectors);
            Assert.Equal(
                checkpoint.CombatSupportMissions,
                loaded.Checkpoint.CombatSupportMissions);
            Assert.Equal(
                loaded.Checkpoint.CombatSupportMissions.Single().MissionId,
                loaded.Checkpoint.World.SupportRequests.Single().ReservedMissionId);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task StaleRevisionIsRejected()
    {
        string dir = TempDir();
        try
        {
            var store = Store(dir);
            var checkpoint = Checkpoint("revision");
            var first = await store.SaveAsync(checkpoint, null);
            var second = await store.SaveAsync(
                checkpoint with { SavedAt = checkpoint.SavedAt.AddMinutes(1) },
                first.Revision);

            Assert.Equal(2, second.Revision);

            await Assert.ThrowsAsync<ConflictCampaignConcurrencyException>(
                () => store.SaveAsync(
                    checkpoint with { SavedAt = checkpoint.SavedAt.AddMinutes(2) },
                    first.Revision));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReservedRequestMustHaveRecoverableMission()
    {
        var checkpoint = Checkpoint("invalid") with
        {
            CombatSupportMissions = Array.Empty<AirSupportMission>()
        };

        Assert.Throws<ArgumentException>(() => checkpoint.Validate());
    }

    private static ConflictCampaignCheckpoint Checkpoint(string id)
    {
        var friendly = new GroundUnitState(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35, -97),
            0.75, 0.80, 0.70, true);

        var hostile = new GroundUnitState(
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            ConflictSide.Hostile,
            GroundUnitRole.Armor,
            new GeoPoint(35.05, -96.95),
            0.85, 0.90, 0, true);

        var world = ConflictWorldState.Create(
            "FICTIONAL-RECOVERY",
            0xC0FFEEUL,
            Epoch,
            new[] { friendly, hostile },
            new[]
            {
                new ConflictSectorState(
                    "RECOVERY-S1",
                    new GeoPoint(35.025, -96.975),
                    0.45,
                    0.55)
            });

        var request = new AirSupportRequest(
            "recovery-cas-001",
            SupportRequestType.CloseAirSupport,
            SupportUrgency.Priority,
            friendly.UnitId,
            hostile.UnitId,
            hostile.Position,
            0.25,
            Epoch,
            Epoch.AddHours(1));

        world = world with { SupportRequests = new[] { request } };

        var missionId =
            Guid.Parse("30000000-0000-0000-0000-000000000001");

        world = SupportRequestLifecycle.Reserve(
            world,
            request.RequestId,
            missionId,
            Epoch.AddMinutes(1));

        var mission = AirSupportMission.Accept(
            world.SupportRequests.Single(),
            missionId,
            Epoch.AddMinutes(1));

        return ConflictCampaignCheckpoint.Create(
            id,
            world,
            new MilitaryCareerState(
                MilitaryAffiliation.Reserve,
                MilitaryQualification.MilitaryFlight
                    | MilitaryQualification.CloseAirSupport,
                0.62,
                4,
                1),
            new PlayerCombatState(0.10, 0.05, 0.02),
            new[] { mission },
            null,
            null,
            Epoch.AddMinutes(2));
    }

    private static SqliteConflictCampaignStore Store(string dir) =>
        new(
            new OpenCareerDatabaseOptions(Path.Combine(dir, "opencareer.db")),
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
}
