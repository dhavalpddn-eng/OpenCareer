using OpenCareer.Application.Military;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class MilitaryCampaignMissionServiceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AcceptedMissionIsPersistedAndFailureClosesReservation()
    {
        var store = new MemoryStore();
        var current = await store.SaveAsync(Checkpoint(), null);
        var campaigns = new ConflictCampaignCoordinator(store);
        var operations = new ConflictOperationsService();
        var service = new MilitaryCampaignMissionService(
            new MilitaryDispatchService(operations),
            operations,
            campaigns);

        Guid missionId =
            Guid.Parse("91000000-0000-0000-0000-000000000001");

        MilitaryMissionAcceptanceResult accepted =
            await service.AcceptAsync(
                current,
                current.Checkpoint.World.SupportRequests.Single().RequestId,
                missionId,
                Epoch.AddMinutes(1),
                Fighter(),
                aircraftAssignedForOperation: true);

        Assert.Equal(2, accepted.Record.Revision);
        Assert.Single(
            accepted.Record.Checkpoint.CombatSupportMissions);
        Assert.Equal(
            SupportRequestStatus.Reserved,
            accepted.Record.Checkpoint.World.SupportRequests.Single().Status);

        double trustBefore =
            accepted.Record.Checkpoint.MilitaryCareer.Trust;

        ConflictCampaignStoreRecord failed =
            await service.FailAsync(
                accepted.Record,
                missionId,
                Epoch.AddMinutes(2));

        Assert.Equal(3, failed.Revision);
        Assert.Empty(failed.Checkpoint.CombatSupportMissions);
        Assert.Equal(
            SupportRequestStatus.Failed,
            failed.Checkpoint.World.SupportRequests.Single().Status);
        Assert.True(failed.Checkpoint.MilitaryCareer.Trust < trustBefore);
        Assert.Equal(
            accepted.Record.Checkpoint.MilitaryCareer.FailedOperations + 1,
            failed.Checkpoint.MilitaryCareer.FailedOperations);
    }

    [Fact]
    public async Task TerminalCampaignCannotAcceptNewMilitaryMission()
    {
        var store = new MemoryStore();
        ConflictCampaignCheckpoint checkpoint = Checkpoint();

        checkpoint = checkpoint with
        {
            CampaignState = checkpoint.CampaignState with
            {
                Outcome = ConflictCampaignOutcome.Victory,
                Objectives = Array.Empty<ConflictStrategicObjective>()
            }
        };

        var current = await store.SaveAsync(
            checkpoint,
            expectedRevision: null);

        var campaigns = new ConflictCampaignCoordinator(store);
        var operations = new ConflictOperationsService();
        var service = new MilitaryCampaignMissionService(
            new MilitaryDispatchService(operations),
            operations,
            campaigns);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptAsync(
                current,
                current.Checkpoint.World.SupportRequests.Single().RequestId,
                Guid.Parse("92500000-0000-0000-0000-000000000001"),
                Epoch.AddMinutes(1),
                Fighter(),
                aircraftAssignedForOperation: true));
    }

    [Fact]
    public async Task SecondActiveMilitaryMissionIsRejected()
    {
        var store = new MemoryStore();
        var current = await store.SaveAsync(Checkpoint(), null);
        var campaigns = new ConflictCampaignCoordinator(store);
        var operations = new ConflictOperationsService();
        var service = new MilitaryCampaignMissionService(
            new MilitaryDispatchService(operations),
            operations,
            campaigns);

        var first = await service.AcceptAsync(
            current,
            current.Checkpoint.World.SupportRequests.Single().RequestId,
            Guid.Parse("92000000-0000-0000-0000-000000000001"),
            Epoch.AddMinutes(1),
            Fighter(),
            aircraftAssignedForOperation: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptAsync(
                first.Record,
                "another-request",
                Guid.Parse("92000000-0000-0000-0000-000000000002"),
                Epoch.AddMinutes(2),
                Fighter(),
                aircraftAssignedForOperation: true));
    }

    private static ConflictCampaignCheckpoint Checkpoint()
    {
        var friendly = new GroundUnitState(
            Guid.Parse("93000000-0000-0000-0000-000000000001"),
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35, -97),
            0.8,
            0.8,
            0.8,
            true);

        var hostile = new GroundUnitState(
            Guid.Parse("94000000-0000-0000-0000-000000000001"),
            ConflictSide.Hostile,
            GroundUnitRole.Armor,
            new GeoPoint(35.03, -96.97),
            0.9,
            0.9,
            0,
            true);

        var world = ConflictWorldState.Create(
            "FICTIONAL-MISSION-SERVICE",
            0x5151UL,
            Epoch,
            new[] { friendly, hostile },
            new[]
            {
                new ConflictSectorState(
                    "MISSION-S1",
                    new GeoPoint(35.01, -96.99),
                    0.5,
                    0.6)
            });

        world = world with
        {
            SupportRequests = new[]
            {
                new AirSupportRequest(
                    "mission-cas-001",
                    SupportRequestType.CloseAirSupport,
                    SupportUrgency.Priority,
                    friendly.UnitId,
                    hostile.UnitId,
                    hostile.Position,
                    0.25,
                    Epoch,
                    Epoch.AddHours(1))
            }
        };

        return ConflictCampaignCheckpoint.Create(
            "player-campaign",
            world,
            new MilitaryCareerState(
                MilitaryAffiliation.Reserve,
                MilitaryQualification.MilitaryFlight
                    | MilitaryQualification.CloseAirSupport,
                0.60,
                2,
                0),
            PlayerCombatState.Undamaged,
            null,
            null,
            null,
            Epoch);
    }

    private static AircraftCapabilityProfile Fighter() =>
        new(
            "mission-fighter",
            "Mission Fighter",
            AircraftCapability.Military | AircraftCapability.Fighter,
            AircraftAccess.Military,
            10_000,
            1_000,
            440,
            1,
            2,
            true,
            true,
            true);

    private sealed class MemoryStore : IConflictCampaignStore
    {
        private ConflictCampaignStoreRecord? _record;

        public Task<ConflictCampaignStoreRecord?> LoadAsync(
            string campaignId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_record);
        }

        public Task<ConflictCampaignStoreRecord> SaveAsync(
            ConflictCampaignCheckpoint checkpoint,
            long? expectedRevision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            checkpoint.Validate();

            if (_record is null)
            {
                if (expectedRevision is not null)
                    throw new ConflictCampaignConcurrencyException("Missing campaign.");

                _record = new ConflictCampaignStoreRecord(1, checkpoint);
                return Task.FromResult(_record);
            }

            if (expectedRevision != _record.Revision)
                throw new ConflictCampaignConcurrencyException("Stale revision.");

            _record = new ConflictCampaignStoreRecord(
                checked(_record.Revision + 1),
                checkpoint);
            return Task.FromResult(_record);
        }
    }
}
