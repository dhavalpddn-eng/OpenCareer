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
    public async Task LongRunCampaignEvolutionIsDeterministicAndBounded()
    {
        ConflictCampaignStoreRecord first =
            await RunLongCampaignAsync(
                "campaign-stress-a",
                theaterSeed: 0x5EEDUL);

        ConflictCampaignStoreRecord second =
            await RunLongCampaignAsync(
                "campaign-stress-a",
                theaterSeed: 0x5EEDUL);

        first.Checkpoint.Validate();
        second.Checkpoint.Validate();

        Assert.Equal(
            first.Checkpoint.CampaignState.Phase,
            second.Checkpoint.CampaignState.Phase);
        Assert.Equal(
            first.Checkpoint.CampaignState.Outcome,
            second.Checkpoint.CampaignState.Outcome);
        Assert.Equal(
            first.Checkpoint.CampaignState.EvaluationSequence,
            second.Checkpoint.CampaignState.EvaluationSequence);
        Assert.Equal(
            first.Checkpoint.CampaignState.FriendlyMomentum,
            second.Checkpoint.CampaignState.FriendlyMomentum,
            precision: 12);
        Assert.Equal(
            first.Checkpoint.CampaignState.FriendlyReplacementReserve,
            second.Checkpoint.CampaignState.FriendlyReplacementReserve,
            precision: 12);
        Assert.Equal(
            first.Checkpoint.CampaignState.HostileReplacementReserve,
            second.Checkpoint.CampaignState.HostileReplacementReserve,
            precision: 12);
        Assert.Equal(
            first.Checkpoint.CampaignState.Identity,
            second.Checkpoint.CampaignState.Identity);
        Assert.Equal(
            first.Checkpoint.World.Units,
            second.Checkpoint.World.Units);
        Assert.Equal(
            first.Checkpoint.World.AirUnits,
            second.Checkpoint.World.AirUnits);
        Assert.Equal(
            first.Checkpoint.World.Sectors,
            second.Checkpoint.World.Sectors);
        Assert.Equal(
            first.Checkpoint.World.Threats,
            second.Checkpoint.World.Threats);
        Assert.Equal(
            first.Checkpoint.World.SupportRequests,
            second.Checkpoint.World.SupportRequests);

        Assert.InRange(
            first.Checkpoint.CampaignState.FriendlyReplacementReserve,
            0,
            1);
        Assert.InRange(
            first.Checkpoint.CampaignState.HostileReplacementReserve,
            0,
            1);
        Assert.All(
            first.Checkpoint.World.Sectors,
            sector => Assert.InRange(
                sector.FriendlyControl,
                0,
                1));
        Assert.All(
            first.Checkpoint.World.Units,
            unit =>
            {
                Assert.InRange(unit.Strength, 0, 1);
                Assert.InRange(unit.Readiness, 0, 1);
                Assert.InRange(unit.Pressure, 0, 1);
            });
        Assert.All(
            first.Checkpoint.World.AirUnits,
            unit =>
            {
                Assert.InRange(unit.Strength, 0, 1);
                Assert.InRange(unit.Readiness, 0, 1);
            });
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
            PlayerCombatState = new PlayerCombatState(
                AirframeDamage: 0.12,
                PropulsionDamage: 0.04,
                SystemsDamage: 0.07),
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
        Assert.Equal(completed.Checkpoint.PlayerCombatState, successor.Checkpoint.PlayerCombatState);
        Assert.Single(successor.Checkpoint.History);
        Assert.Equal(
            completed.Checkpoint.CampaignId,
            successor.Checkpoint.History[0].CampaignId);
        Assert.Equal(
            ConflictCampaignOutcome.Ceasefire,
            successor.Checkpoint.History[0].Outcome);
        Assert.Equal(
            completed.Checkpoint.CampaignState.Identity,
            successor.Checkpoint.History[0].Identity);
        Assert.Empty(successor.Checkpoint.CombatSupportMissions);
        Assert.Empty(successor.Checkpoint.AreaSupportMissions);
        Assert.Empty(successor.Checkpoint.AirOperationMissions);
        Assert.NotEqual(
            completed.Checkpoint.CampaignState.Identity?.OperationName,
            successor.Checkpoint.CampaignState.Identity?.OperationName);
    }

    [Fact]
    public async Task SuccessorHistoryAccumulatesAcrossOperations()
    {
        var store = new MemoryStore();
        var coordinator = new ConflictCampaignCoordinator(store);

        ConflictCampaignStoreRecord first =
            await coordinator.CreateAsync(
                "campaign-history-1",
                Template(),
                theaterSeed: 101,
                Epoch,
                MilitaryCareerState.Civilian);

        ConflictCampaignStoreRecord firstTerminal =
            await coordinator.SaveMutationAsync(
                first,
                first.Checkpoint with
                {
                    CampaignState = first.Checkpoint.CampaignState with
                    {
                        Outcome = ConflictCampaignOutcome.Victory,
                        Objectives = Array.Empty<ConflictStrategicObjective>()
                    },
                    SavedAt = Epoch.AddHours(2)
                });

        ConflictCampaignStoreRecord second =
            await coordinator.CreateSuccessorAsync(
                firstTerminal,
                "campaign-history-2",
                Template() with
                {
                    TheaterId = "FICTIONAL-COORDINATOR-2"
                },
                theaterSeed: 102,
                Epoch.AddHours(3));

        ConflictCampaignStoreRecord secondTerminal =
            await coordinator.SaveMutationAsync(
                second,
                second.Checkpoint with
                {
                    CampaignState = second.Checkpoint.CampaignState with
                    {
                        Outcome = ConflictCampaignOutcome.Stalemate,
                        Objectives = Array.Empty<ConflictStrategicObjective>()
                    },
                    SavedAt = Epoch.AddHours(5)
                });

        ConflictCampaignStoreRecord third =
            await coordinator.CreateSuccessorAsync(
                secondTerminal,
                "campaign-history-3",
                Template() with
                {
                    TheaterId = "FICTIONAL-COORDINATOR-3"
                },
                theaterSeed: 103,
                Epoch.AddHours(6));

        Assert.Equal(2, third.Checkpoint.History.Length);
        Assert.Equal(
            new[]
            {
                "campaign-history-1",
                "campaign-history-2"
            },
            third.Checkpoint.History.Select(entry => entry.CampaignId));
        Assert.Equal(
            ConflictCampaignOutcome.Victory,
            third.Checkpoint.History[0].Outcome);
        Assert.Equal(
            ConflictCampaignOutcome.Stalemate,
            third.Checkpoint.History[1].Outcome);
        Assert.NotEqual(
            third.Checkpoint.History[0].Identity.OperationId,
            third.Checkpoint.History[1].Identity.OperationId);
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

    private static async Task<ConflictCampaignStoreRecord> RunLongCampaignAsync(
        string campaignId,
        ulong theaterSeed)
    {
        var coordinator =
            new ConflictCampaignCoordinator(
                new MemoryStore());

        ConflictCampaignStoreRecord current =
            await coordinator.CreateAsync(
                campaignId,
                Template(),
                theaterSeed,
                Epoch,
                MilitaryCareerState.Civilian);

        for (int step = 1; step <= 96; step++)
        {
            if (current.Checkpoint.CampaignState.IsTerminal)
                break;

            DateTimeOffset through =
                Epoch.AddHours(step * 6);

            current = await coordinator.AdvanceAsync(
                current,
                through,
                through.AddSeconds(1));

            current.Validate();
        }

        return current;
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
