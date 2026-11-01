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
        var consequences = new MemoryConsequenceStore();
        var current = await store.SaveAsync(Checkpoint(), null);
        var service = CreateService(store, consequences);

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
        Assert.Equal(
            trustBefore - 0.03,
            failed.Checkpoint.MilitaryCareer.Trust,
            precision: 10);
        Assert.Equal(
            accepted.Record.Checkpoint.MilitaryCareer.FailedOperations + 1,
            failed.Checkpoint.MilitaryCareer.FailedOperations);
        OperationConsequenceStoreRecord consequence =
            Assert.Single(consequences.Records);
        Assert.Equal(
            OperationOutcomeStatus.Failure,
            consequence.Result.Outcome.Status);
        Assert.Equal(
            failed.Checkpoint.MilitaryCareer,
            consequence.Result.MilitaryCareer);
    }

    [Fact]
    public async Task CompletedMissionPersistsConsequencesAndUpdatesCareerOnce()
    {
        var store = new MemoryStore();
        var consequences = new MemoryConsequenceStore();
        var current = await store.SaveAsync(Checkpoint(), null);
        var service = CreateService(store, consequences);

        Guid missionId =
            Guid.Parse("91500000-0000-0000-0000-000000000001");

        MilitaryMissionAcceptanceResult accepted =
            await service.AcceptAsync(
                current,
                current.Checkpoint.World.SupportRequests.Single().RequestId,
                missionId,
                Epoch.AddMinutes(1),
                Fighter(),
                aircraftAssignedForOperation: true);

        AirSupportMission mission =
            accepted.Record.Checkpoint.CombatSupportMissions.Single()
            with
            {
                Stage = AirSupportMissionStage.ObjectiveComplete,
                LastUpdatedAt = Epoch.AddMinutes(2)
            };

        ConflictCampaignStoreRecord ready =
            accepted.Record with
            {
                Checkpoint = accepted.Record.Checkpoint with
                {
                    CombatSupportMissions = new[] { mission }
                }
            };

        ConflictCampaignStoreRecord completed =
            await service.CompleteAsync(
                ready,
                missionId,
                Epoch.AddMinutes(3));

        Assert.Empty(completed.Checkpoint.CombatSupportMissions);
        Assert.Equal(
            SupportRequestStatus.Completed,
            completed.Checkpoint.World.SupportRequests.Single().Status);
        Assert.Equal(
            0.62,
            completed.Checkpoint.MilitaryCareer.Trust,
            precision: 10);
        Assert.Equal(
            3,
            completed.Checkpoint.MilitaryCareer.SuccessfulOperations);

        OperationConsequenceStoreRecord consequence =
            Assert.Single(consequences.Records);
        Assert.Equal(
            OperationOutcomeStatus.Success,
            consequence.Result.Outcome.Status);
        Assert.Equal(
            completed.Checkpoint.MilitaryCareer,
            consequence.Result.MilitaryCareer);
    }

    [Fact]
    public async Task TerminalCampaignCannotAcceptNewMilitaryMission()
    {
        var store = new MemoryStore();
        var consequences = new MemoryConsequenceStore();
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

        var service = CreateService(store, consequences);

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
        var consequences = new MemoryConsequenceStore();
        var current = await store.SaveAsync(Checkpoint(), null);
        var service = CreateService(store, consequences);

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

    private static MilitaryCampaignMissionService CreateService(
        MemoryStore campaignStore,
        MemoryConsequenceStore consequenceStore)
    {
        var operations = new ConflictOperationsService();
        var campaigns =
            new ConflictCampaignCoordinator(campaignStore);

        var resolver =
            new IdempotentOperationResolver(
                new OperationResolver(),
                new InMemoryOperationResolutionRegistry());

        var reputation =
            new MilitaryReputationConsequence(
                new InMemoryMilitaryReputationConsequenceRegistry());

        var consequenceCoordinator =
            new PersistedOperationConsequenceCoordinator(
                new OperationConsequenceOrchestrator(
                    resolver,
                    reputation),
                consequenceStore);

        return new MilitaryCampaignMissionService(
            new MilitaryDispatchService(operations),
            operations,
            campaigns,
            consequenceCoordinator,
            consequenceStore);
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

    private sealed class MemoryConsequenceStore :
        IOperationConsequenceStore,
        IOperationConsequenceHistorySource
    {
        private readonly List<OperationConsequenceStoreRecord> _records = [];

        public IReadOnlyList<OperationConsequenceStoreRecord> Records =>
            _records;

        public Task<OperationConsequenceStoreRecord?> LoadAsync(
            OperationResolutionKey key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _records.SingleOrDefault(
                    item => item.ResolutionKey == key));
        }

        public Task<OperationConsequenceStoreRecord?> LoadLatestForCampaignAsync(
            string campaignId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _records
                    .Where(item =>
                        string.Equals(
                            item.Result.CampaignProgress.CampaignId,
                            campaignId,
                            StringComparison.Ordinal))
                    .OrderByDescending(item => item.Result.Outcome.CompletedAt)
                    .ThenByDescending(item => item.SavedAt)
                    .FirstOrDefault());
        }

        public Task<OperationConsequenceStoreRecord?> LoadLatestForSectorAsync(
            string campaignId,
            string sectorId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _records
                    .Where(item =>
                        string.Equals(
                            item.Result.CampaignProgress.CampaignId,
                            campaignId,
                            StringComparison.Ordinal)
                        && string.Equals(
                            item.Result.TerritoryPressure.State.SectorId,
                            sectorId,
                            StringComparison.Ordinal))
                    .OrderByDescending(item => item.Result.Outcome.CompletedAt)
                    .ThenByDescending(item => item.SavedAt)
                    .FirstOrDefault());
        }

        public Task<OperationConsequenceStoreRecord> SaveAsync(
            OperationConsequenceResult result,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationResolutionKey key =
                OperationResolutionKey.Create(
                    result.Outcome.OperationId,
                    result.Outcome.MissionId);

            if (_records.Any(item => item.ResolutionKey == key))
            {
                throw new OperationConsequenceAlreadyExistsException(
                    key);
            }

            var record = new OperationConsequenceStoreRecord(
                key,
                result,
                savedAt);

            record.Validate();
            _records.Add(record);
            return Task.FromResult(record);
        }
    }

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
