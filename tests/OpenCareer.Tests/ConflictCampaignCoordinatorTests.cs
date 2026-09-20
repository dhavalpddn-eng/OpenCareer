using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class ConflictCampaignCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAndAdvancePersistWorldAndStrategicStateTogether()
    {
        var store = new MemoryStore();
        var coordinator = new ConflictCampaignCoordinator(store);

        ConflictCampaignStoreRecord created =
            await coordinator.CreateAsync(
                "campaign-coordinator",
                Template(),
                theaterSeed: 0xACEDUL,
                Epoch,
                MilitaryCareerState.Civilian);

        Assert.Equal(1, created.Revision);
        Assert.Equal(0, created.Checkpoint.CampaignState.EvaluationSequence);
        Assert.Equal(
            created.Checkpoint.World.UpdatedAt,
            created.Checkpoint.CampaignState.UpdatedAt);

        ConflictCampaignStoreRecord advanced =
            await coordinator.AdvanceAsync(
                created,
                Epoch.AddHours(1),
                Epoch.AddHours(1).AddSeconds(1));

        Assert.Equal(2, advanced.Revision);
        Assert.Equal(1, advanced.Checkpoint.CampaignState.EvaluationSequence);
        Assert.Equal(Epoch.AddHours(1), advanced.Checkpoint.World.UpdatedAt);
        Assert.Equal(
            advanced.Checkpoint.World.UpdatedAt,
            advanced.Checkpoint.CampaignState.UpdatedAt);

        ConflictCampaignStoreRecord? loaded =
            await coordinator.LoadAsync("campaign-coordinator");

        Assert.NotNull(loaded);
        Assert.Equal(advanced.Revision, loaded!.Revision);
        Assert.Equal(
            advanced.Checkpoint.CampaignState.Phase,
            loaded.Checkpoint.CampaignState.Phase);
    }

    [Fact]
    public async Task CreateSuccessorCarriesCareerIntoFreshCampaign()
    {
        var store = new MemoryStore();
        var coordinator = new ConflictCampaignCoordinator(store);
        ConflictCampaignStoreRecord created = await coordinator.CreateAsync(
            "campaign-finished",
            Template(),
            theaterSeed: 17,
            Epoch,
            MilitaryCareerState.Civilian);

        var terminalCheckpoint = created.Checkpoint with
        {
            CampaignState = created.Checkpoint.CampaignState with
            {
                Outcome = ConflictCampaignOutcome.Ceasefire,
                Objectives = Array.Empty<ConflictStrategicObjective>()
            },
            SavedAt = Epoch.AddHours(4)
        };

        ConflictCampaignStoreRecord completed =
            await coordinator.SaveMutationAsync(created, terminalCheckpoint);

        ConflictCampaignStoreRecord successor =
            await coordinator.CreateSuccessorAsync(
                completed,
                "campaign-successor",
                Template(),
                theaterSeed: 18,
                Epoch.AddHours(5));

        Assert.Equal(1, successor.Revision);
        Assert.Equal("campaign-successor", successor.Checkpoint.CampaignId);
        Assert.Equal(ConflictCampaignOutcome.Ongoing, successor.Checkpoint.CampaignState.Outcome);
        Assert.Equal(0, successor.Checkpoint.CampaignState.EvaluationSequence);
        Assert.Equal(completed.Checkpoint.MilitaryCareer, successor.Checkpoint.MilitaryCareer);
        Assert.Equal(PlayerCombatState.Undamaged, successor.Checkpoint.PlayerCombatState);
        Assert.Empty(successor.Checkpoint.CombatSupportMissions);
        Assert.Empty(successor.Checkpoint.AreaSupportMissions);
        Assert.Empty(successor.Checkpoint.AirOperationMissions);
        Assert.NotEqual(
            completed.Checkpoint.CampaignState.Identity?.OperationName,
            successor.Checkpoint.CampaignState.Identity?.OperationName);
    }

    [Fact]
    public async Task CreateSuccessorRejectsOngoingCampaign()
    {
        var coordinator = new ConflictCampaignCoordinator(new MemoryStore());
        ConflictCampaignStoreRecord current = await coordinator.CreateAsync(
            "campaign-ongoing",
            Template(),
            theaterSeed: 23,
            Epoch,
            MilitaryCareerState.Civilian);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.CreateSuccessorAsync(
                current,
                "campaign-too-early",
                Template(),
                theaterSeed: 24,
                Epoch.AddHours(1)));
    }

    [Fact]
    public async Task CreateSuccessorRejectsReusedIdentityAndBackwardTime()
    {
        var store = new MemoryStore();
        var coordinator = new ConflictCampaignCoordinator(store);
        ConflictCampaignStoreRecord created = await coordinator.CreateAsync(
            "campaign-terminal",
            Template(),
            theaterSeed: 31,
            Epoch,
            MilitaryCareerState.Civilian);

        var terminalCheckpoint = created.Checkpoint with
        {
            CampaignState = created.Checkpoint.CampaignState with
            {
                Outcome = ConflictCampaignOutcome.Stalemate,
                Objectives = Array.Empty<ConflictStrategicObjective>()
            },
            SavedAt = Epoch.AddHours(3)
        };

        ConflictCampaignStoreRecord completed =
            await coordinator.SaveMutationAsync(created, terminalCheckpoint);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.CreateSuccessorAsync(
                completed,
                completed.Checkpoint.CampaignId,
                Template(),
                theaterSeed: 32,
                Epoch.AddHours(4)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => coordinator.CreateSuccessorAsync(
                completed,
                "campaign-backward",
                Template(),
                theaterSeed: 33,
                Epoch.AddHours(2)));
    }

    [Fact]
    public async Task AdvanceRejectsSaveTimeBeforeWorldTime()
    {
        var coordinator = new ConflictCampaignCoordinator(new MemoryStore());
        var created = await coordinator.CreateAsync(
            "campaign-time",
            Template(),
            theaterSeed: 42,
            Epoch,
            MilitaryCareerState.Civilian);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => coordinator.AdvanceAsync(
                created,
                Epoch.AddHours(2),
                Epoch.AddHours(1)));
    }

    [Fact]
    public async Task SaveMutationCannotSwapCampaignIdentity()
    {
        var coordinator = new ConflictCampaignCoordinator(new MemoryStore());
        var created = await coordinator.CreateAsync(
            "campaign-original",
            Template(),
            theaterSeed: 77,
            Epoch,
            MilitaryCareerState.Civilian);

        var invalid = created.Checkpoint with
        {
            CampaignId = "campaign-other",
            CampaignState = created.Checkpoint.CampaignState with
            {
                CampaignId = "campaign-other"
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SaveMutationAsync(created, invalid));
    }

    private static ConflictTheaterTemplate Template() =>
        new(
            TheaterId: "FICTIONAL-COORDINATOR",
            Center: new GeoPoint(35.25, -97.15),
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
            _records.TryGetValue(campaignId, out ConflictCampaignStoreRecord? record);
            return Task.FromResult(record);
        }

        public Task<ConflictCampaignStoreRecord> SaveAsync(
            ConflictCampaignCheckpoint checkpoint,
            long? expectedRevision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            checkpoint.Validate();

            if (!_records.TryGetValue(checkpoint.CampaignId, out ConflictCampaignStoreRecord? current))
            {
                if (expectedRevision is not null)
                    throw new ConflictCampaignConcurrencyException("Missing record.");

                var created = new ConflictCampaignStoreRecord(1, checkpoint);
                _records.Add(checkpoint.CampaignId, created);
                return Task.FromResult(created);
            }

            if (expectedRevision != current.Revision)
                throw new ConflictCampaignConcurrencyException("Stale revision.");

            var updated = new ConflictCampaignStoreRecord(
                checked(current.Revision + 1),
                checkpoint);

            _records[checkpoint.CampaignId] = updated;
            return Task.FromResult(updated);
        }
    }
}
