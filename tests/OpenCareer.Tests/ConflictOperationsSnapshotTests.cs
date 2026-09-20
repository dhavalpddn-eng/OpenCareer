using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class ConflictOperationsSnapshotTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SnapshotProjectsCampaignMapThreatsAndActiveMission()
    {
        var friendly = new GroundUnitState(
            Guid.Parse("a1000000-0000-0000-0000-000000000001"),
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35, -97),
            0.8, 0.75, 0.7, true);

        var hostile = new GroundUnitState(
            Guid.Parse("a2000000-0000-0000-0000-000000000001"),
            ConflictSide.Hostile,
            GroundUnitRole.AirDefense,
            new GeoPoint(35.03, -96.97),
            0.9, 0.9, 0, false);

        var threat = new ThreatState(
            Guid.Parse("a3000000-0000-0000-0000-000000000001"),
            hostile.UnitId,
            ConflictSide.Hostile,
            AirThreatType.AirDefense,
            hostile.Position,
            15,
            0.81,
            true);

        var world = ConflictWorldState.Create(
            "FICTIONAL-SNAPSHOT",
            0x5150UL,
            Epoch,
            new[] { friendly, hostile },
            new[]
            {
                new ConflictSectorState(
                    "SNAPSHOT-S1",
                    new GeoPoint(35.01, -96.99),
                    0.48,
                    0.55)
            },
            new[] { threat });

        var request = new AirSupportRequest(
            "snapshot-cas-001",
            SupportRequestType.CloseAirSupport,
            SupportUrgency.Immediate,
            friendly.UnitId,
            hostile.UnitId,
            hostile.Position,
            0.30,
            Epoch,
            Epoch.AddHours(1));

        world = world with { SupportRequests = new[] { request } };

        Guid missionId =
            Guid.Parse("a4000000-0000-0000-0000-000000000001");

        world = SupportRequestLifecycle.Reserve(
            world,
            request.RequestId,
            missionId,
            Epoch.AddMinutes(1));

        var mission = AirSupportMission.Accept(
            world.SupportRequests.Single(),
            missionId,
            Epoch.AddMinutes(1));

        var checkpoint = ConflictCampaignCheckpoint.Create(
            "snapshot-campaign",
            world,
            new MilitaryCareerState(
                MilitaryAffiliation.Reserve,
                MilitaryQualification.MilitaryFlight
                    | MilitaryQualification.CloseAirSupport,
                0.72,
                5,
                1),
            new PlayerCombatState(0.05, 0, 0),
            new[] { mission },
            null,
            null,
            Epoch.AddMinutes(1));

        ConflictOperationsSnapshot snapshot =
            ConflictOperationsSnapshotBuilder.Build(checkpoint);

        Assert.Equal("snapshot-campaign", snapshot.CampaignId);
        Assert.Equal("FICTIONAL-SNAPSHOT", snapshot.TheaterId);
        Assert.StartsWith("operation:", snapshot.OperationId);
        Assert.StartsWith("Operation ", snapshot.OperationName);
        Assert.Equal(ConflictSide.Friendly, snapshot.FriendlyFaction.Side);
        Assert.Equal(ConflictSide.Hostile, snapshot.HostileFaction.Side);
        Assert.NotEqual(
            snapshot.FriendlyFaction.DisplayName,
            snapshot.HostileFaction.DisplayName);
        Assert.Equal(2, snapshot.Units.Length);
        Assert.Single(snapshot.Threats);
        Assert.Single(snapshot.SupportRequests);
        Assert.NotNull(snapshot.ActiveOperation);
        Assert.Equal(
            SupportRequestType.CloseAirSupport,
            snapshot.ActiveOperation!.Type);
        Assert.Equal(
            AirSupportMissionStage.Accepted.ToString(),
            snapshot.ActiveOperation.Stage);
        Assert.InRange(snapshot.FriendlyControlAverage, 0, 1);
        Assert.Equal(0.72, snapshot.MilitaryTrust);
        Assert.NotEmpty(snapshot.Front.Points);
    }
}
