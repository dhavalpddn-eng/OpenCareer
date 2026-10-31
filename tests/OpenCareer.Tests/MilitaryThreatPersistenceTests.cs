using OpenCareer.Application.Military;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class MilitaryThreatPersistenceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ThreatResolutionPersistsDamageAndDuplicateProtection()
    {
        var store = new MemoryStore();
        var checkpoint = Checkpoint();
        var current = await store.SaveAsync(checkpoint, null);

        var campaigns = new ConflictCampaignCoordinator(store);
        var operations = new ConflictOperationsService();
        var consequences = new NoopConsequenceStore();
        var consequenceCoordinator =
            new PersistedOperationConsequenceCoordinator(
                new OperationConsequenceOrchestrator(
                    new IdempotentOperationResolver(
                        new OperationResolver(),
                        new InMemoryOperationResolutionRegistry()),
                    new MilitaryReputationConsequence(
                        new InMemoryMilitaryReputationConsequenceRegistry())),
                consequences);
        var service = new MilitaryCampaignMissionService(
            new MilitaryDispatchService(operations),
            operations,
            campaigns,
            consequenceCoordinator,
            consequences);

        Guid missionId =
            Guid.Parse("96000000-0000-0000-0000-000000000001");

        var accepted = await service.AcceptAsync(
            current,
            checkpoint.World.SupportRequests.Single().RequestId,
            missionId,
            Epoch.AddMinutes(1),
            Fighter(),
            aircraftAssignedForOperation: true);

        var engagement = new ThreatEngagementRequest(
            "threat-persist-001",
            missionId,
            ThreatId,
            TimeSpan.FromSeconds(45),
            DefensiveResponseQuality: 0);

        var first = await service.ResolveThreatAsync(
            accepted.Record,
            engagement,
            Epoch.AddMinutes(1).AddSeconds(10));

        Assert.Equal(accepted.Record.Revision + 1, first.Record.Revision);
        Assert.Contains(
            engagement.EngagementId,
            first.Record.Checkpoint.World.ProcessedEventIds);

        PlayerCombatState damageAfterFirst =
            first.Record.Checkpoint.PlayerCombatState;

        var duplicate = await service.ResolveThreatAsync(
            first.Record,
            engagement,
            Epoch.AddMinutes(1).AddSeconds(20));

        Assert.Equal(
            ThreatEngagementOutcome.DuplicateIgnored,
            duplicate.Result.Outcome);
        Assert.Equal(first.Record.Revision, duplicate.Record.Revision);
        Assert.Equal(
            damageAfterFirst,
            duplicate.Record.Checkpoint.PlayerCombatState);
    }

    private static ConflictCampaignCheckpoint Checkpoint()
    {
        var friendly = new GroundUnitState(
            Guid.Parse("97000000-0000-0000-0000-000000000001"),
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35, -97),
            0.8,
            0.8,
            0.8,
            true);

        var hostile = new GroundUnitState(
            Guid.Parse("98000000-0000-0000-0000-000000000001"),
            ConflictSide.Hostile,
            GroundUnitRole.AirDefense,
            new GeoPoint(35.03, -96.97),
            0.9,
            0.9,
            0,
            false);

        var threat = new ThreatState(
            ThreatId,
            hostile.UnitId,
            ConflictSide.Hostile,
            AirThreatType.AirDefense,
            hostile.Position,
            RadiusNauticalMiles: 15,
            Severity: 0.8,
            Active: true);

        var world = ConflictWorldState.Create(
            "FICTIONAL-THREAT-PERSISTENCE",
            0x7A7AUL,
            Epoch,
            new[] { friendly, hostile },
            new[]
            {
                new ConflictSectorState(
                    "THREAT-S1",
                    new GeoPoint(35.01, -96.99),
                    0.5,
                    0.6)
            },
            new[] { threat });

        world = world with
        {
            SupportRequests = new[]
            {
                new AirSupportRequest(
                    "threat-cas-001",
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
            "threat-player-campaign",
            world,
            new MilitaryCareerState(
                MilitaryAffiliation.Reserve,
                MilitaryQualification.MilitaryFlight
                    | MilitaryQualification.CloseAirSupport,
                0.6,
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
            "threat-fighter",
            "Threat Test Fighter",
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

    private sealed class NoopConsequenceStore :
        IOperationConsequenceStore,
        IOperationConsequenceHistorySource
    {
        public Task<OperationConsequenceStoreRecord?> LoadAsync(
            OperationResolutionKey key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OperationConsequenceStoreRecord?>(null);

        public Task<OperationConsequenceStoreRecord?> LoadLatestForCampaignAsync(
            string campaignId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OperationConsequenceStoreRecord?>(null);

        public Task<OperationConsequenceStoreRecord?> LoadLatestForSectorAsync(
            string campaignId,
            string sectorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OperationConsequenceStoreRecord?>(null);

        public Task<OperationConsequenceStoreRecord> SaveAsync(
            OperationConsequenceResult result,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Threat persistence test does not settle mission consequences.");
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

    private static readonly Guid ThreatId =
        Guid.Parse("99000000-0000-0000-0000-000000000001");
}
