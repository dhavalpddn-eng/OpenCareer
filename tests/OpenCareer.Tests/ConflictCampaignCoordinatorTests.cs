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
        private ConflictCampaignStoreRecord? _record;

        public Task<ConflictCampaignStoreRecord?> LoadAsync(
            string campaignId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _record is not null
                && string.Equals(
                    _record.Checkpoint.CampaignId,
                    campaignId,
                    StringComparison.Ordinal)
                    ? _record
                    : null);
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
                    throw new ConflictCampaignConcurrencyException("Missing record.");

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
