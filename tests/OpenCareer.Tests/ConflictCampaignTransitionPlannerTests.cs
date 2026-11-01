using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class ConflictCampaignTransitionPlannerTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SameCompletedCampaignProducesSameSuccessorOffer()
    {
        ConflictCampaignStoreRecord completed =
            CompletedCampaign();

        ConflictTheaterTemplate[] candidates =
        {
            Template("FICTIONAL-TRANSITION-A", 35.0, -97.0),
            Template("FICTIONAL-TRANSITION-B", 38.0, -105.0),
            Template("FICTIONAL-TRANSITION-C", 32.0, -112.0)
        };

        ConflictCampaignSuccessorOffer first =
            ConflictCampaignTransitionPlanner.Plan(
                completed,
                candidates,
                Epoch.AddHours(1));

        ConflictCampaignSuccessorOffer second =
            ConflictCampaignTransitionPlanner.Plan(
                completed,
                candidates,
                Epoch.AddHours(1));

        Assert.Equal(first, second);
        Assert.NotEqual(
            completed.Checkpoint.World.TheaterId,
            first.Theater.TheaterId);
        Assert.Equal(
            "transition-root-op-002",
            first.CampaignId);
        Assert.StartsWith(
            "Operation ",
            first.Identity.OperationName);
    }

    [Fact]
    public async Task PlannedOfferCreatesSuccessorWithArchivedHistory()
    {
        var store = new MemoryStore();
        ConflictCampaignStoreRecord completed =
            CompletedCampaign();

        await store.SaveAsync(
            completed.Checkpoint,
            expectedRevision: null);

        var coordinator =
            new ConflictCampaignCoordinator(store);

        ConflictCampaignSuccessorOffer offer =
            ConflictCampaignTransitionPlanner.Plan(
                completed,
                new[]
                {
                    Template(
                        "FICTIONAL-TRANSITION-A",
                        35.0,
                        -97.0),
                    Template(
                        "FICTIONAL-TRANSITION-B",
                        40.0,
                        -104.0)
                },
                Epoch.AddHours(1));

        ConflictCampaignStoreRecord successor =
            await coordinator.CreateSuccessorAsync(
                completed,
                offer);

        Assert.Equal(
            offer.CampaignId,
            successor.Checkpoint.CampaignId);
        Assert.Equal(
            offer.Identity,
            successor.Checkpoint.CampaignState.Identity);
        Assert.Equal(
            offer.Theater.TheaterId,
            successor.Checkpoint.World.TheaterId);
        Assert.Equal(
            ConflictCampaignOutcome.Ongoing,
            successor.Checkpoint.CampaignState.Outcome);
        Assert.Equal(
            0,
            successor.Checkpoint.CampaignState.EvaluationSequence);
        Assert.Equal(
            0.22,
            successor.Checkpoint.CampaignState.FriendlyReplacementReserve,
            precision: 10);
        Assert.Equal(
            0.22,
            successor.Checkpoint.CampaignState.HostileReplacementReserve,
            precision: 10);
        Assert.Equal(
            completed.Checkpoint.PlayerCombatState,
            successor.Checkpoint.PlayerCombatState);
        Assert.Single(successor.Checkpoint.History);
        Assert.Equal(
            completed.Checkpoint.CampaignId,
            successor.Checkpoint.History[0].CampaignId);
        Assert.Equal(
            ConflictCampaignOutcome.Victory,
            successor.Checkpoint.History[0].Outcome);
    }

    [Fact]
    public void OngoingCampaignCannotProduceSuccessorOffer()
    {
        ConflictCampaignStoreRecord completed =
            CompletedCampaign();

        var ongoing = completed with
        {
            Checkpoint = completed.Checkpoint with
            {
                CampaignState =
                    ConflictCampaignDirector.Create(
                        completed.Checkpoint.CampaignId,
                        completed.Checkpoint.World)
            }
        };

        Assert.Throws<InvalidOperationException>(
            () => ConflictCampaignTransitionPlanner.Plan(
                ongoing,
                new[]
                {
                    Template(
                        "FICTIONAL-TRANSITION-B",
                        40.0,
                        -104.0)
                },
                Epoch.AddHours(1)));
    }

    private static ConflictCampaignStoreRecord CompletedCampaign()
    {
        ConflictTheaterTemplate template =
            Template(
                "FICTIONAL-TRANSITION-A",
                35.0,
                -97.0);

        ConflictWorldState world =
            ConflictTheaterGenerator.Generate(
                template,
                theaterSeed: 0xAA55UL,
                Epoch);

        ConflictCampaignState state =
            ConflictCampaignDirector.Create(
                "transition-root",
                world) with
            {
                Outcome = ConflictCampaignOutcome.Victory,
                Phase = ConflictCampaignPhase.FriendlySecured,
                EvaluationSequence = 6,
                FriendlyControlAverage = 0.84,
                Objectives =
                    Array.Empty<ConflictStrategicObjective>()
            };

        ConflictCampaignCheckpoint checkpoint =
            ConflictCampaignCheckpoint.Create(
                "transition-root",
                world,
                new MilitaryCareerState(
                    MilitaryAffiliation.Reserve,
                    MilitaryQualification.MilitaryFlight
                        | MilitaryQualification.Patrol,
                    Trust: 0.68,
                    SuccessfulOperations: 5,
                    FailedOperations: 1),
                new PlayerCombatState(
                    AirframeDamage: 0.08,
                    PropulsionDamage: 0.03,
                    SystemsDamage: 0.04),
                combatSupportMissions: null,
                areaSupportMissions: null,
                airOperationMissions: null,
                savedAt: Epoch,
                state);

        return new ConflictCampaignStoreRecord(
            1,
            checkpoint);
    }

    private static ConflictTheaterTemplate Template(
        string theaterId,
        double latitude,
        double longitude) =>
        new(
            theaterId,
            new GeoPoint(latitude, longitude),
            RadiusNauticalMiles: 100,
            FriendlyGroundUnits: 6,
            HostileGroundUnits: 6,
            FriendlyAirUnits: 3,
            HostileAirUnits: 3);

    private sealed class MemoryStore : IConflictCampaignStore
    {
        private readonly Dictionary<string, ConflictCampaignStoreRecord> _records =
            new(StringComparer.Ordinal);

        public Task<ConflictCampaignStoreRecord?> LoadAsync(
            string campaignId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _records.TryGetValue(
                campaignId,
                out ConflictCampaignStoreRecord? record);
            return Task.FromResult(record);
        }

        public Task<ConflictCampaignStoreRecord> SaveAsync(
            ConflictCampaignCheckpoint checkpoint,
            long? expectedRevision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            checkpoint.Validate();

            if (!_records.TryGetValue(
                checkpoint.CampaignId,
                out ConflictCampaignStoreRecord? current))
            {
                if (expectedRevision is not null)
                {
                    throw new ConflictCampaignConcurrencyException(
                        "Missing campaign.");
                }

                var created =
                    new ConflictCampaignStoreRecord(
                        1,
                        checkpoint);

                _records.Add(
                    checkpoint.CampaignId,
                    created);

                return Task.FromResult(created);
            }

            if (expectedRevision != current.Revision)
            {
                throw new ConflictCampaignConcurrencyException(
                    "Stale revision.");
            }

            var updated =
                new ConflictCampaignStoreRecord(
                    checked(current.Revision + 1),
                    checkpoint);

            _records[checkpoint.CampaignId] = updated;
            return Task.FromResult(updated);
        }
    }
}
