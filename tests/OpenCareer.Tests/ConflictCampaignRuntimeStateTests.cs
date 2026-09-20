using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class ConflictCampaignRuntimeStateTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InitializeLoadsRecoveredCampaignOnce()
    {
        var record = Record("runtime-recovery");
        var source = new FakeRecoverySource(record);
        var runtime = new ConflictCampaignRuntimeState(source);

        ConflictCampaignStoreRecord? first = await runtime.InitializeAsync();
        ConflictCampaignStoreRecord? second = await runtime.InitializeAsync();

        Assert.True(runtime.IsInitialized);
        Assert.Same(record, first);
        Assert.Same(record, second);
        Assert.Same(record, runtime.Current);
        Assert.Equal(1, source.LoadCount);
    }

    [Fact]
    public async Task InitializeWithoutCampaignProducesInitializedEmptyState()
    {
        var source = new FakeRecoverySource(null);
        var runtime = new ConflictCampaignRuntimeState(source);

        ConflictCampaignStoreRecord? recovered =
            await runtime.InitializeAsync();

        Assert.True(runtime.IsInitialized);
        Assert.Null(recovered);
        Assert.Null(runtime.Current);
        Assert.Equal(1, source.LoadCount);
    }

    [Fact]
    public async Task FailedInitializationCanBeRetried()
    {
        var record = Record("retry-recovery");
        var source = new FakeRecoverySource(record)
        {
            FailNextLoad = true
        };
        var runtime = new ConflictCampaignRuntimeState(source);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.InitializeAsync());

        Assert.False(runtime.IsInitialized);
        Assert.Null(runtime.Current);

        ConflictCampaignStoreRecord? recovered =
            await runtime.InitializeAsync();

        Assert.Same(record, recovered);
        Assert.Equal(2, source.LoadCount);
    }

    [Fact]
    public void ReplaceValidatesAndPublishesCurrentRecord()
    {
        var runtime = new ConflictCampaignRuntimeState(
            new FakeRecoverySource(null));
        var record = Record("replacement");

        runtime.Replace(record);

        Assert.True(runtime.IsInitialized);
        Assert.Same(record, runtime.Current);
    }

    private static ConflictCampaignStoreRecord Record(string campaignId)
    {
        var world = ConflictWorldState.Create(
            theaterId: "FICTIONAL-RUNTIME",
            theaterSeed: 42,
            updatedAt: Epoch,
            units: new[]
            {
                new GroundUnitState(
                    Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    ConflictSide.Friendly,
                    GroundUnitRole.Command,
                    new GeoPoint(35, -97),
                    Strength: 0.9,
                    Readiness: 0.9,
                    Pressure: 0,
                    IsMobile: false)
            },
            sectors: new[]
            {
                new ConflictSectorState(
                    "RUNTIME-S1",
                    new GeoPoint(35, -97),
                    FriendlyControl: 0.6,
                    IntelligenceConfidence: 0.7)
            });

        var checkpoint = ConflictCampaignCheckpoint.Create(
            campaignId,
            world,
            MilitaryCareerState.Civilian,
            PlayerCombatState.Undamaged,
            combatSupportMissions: null,
            areaSupportMissions: null,
            airOperationMissions: null,
            savedAt: Epoch);

        return new ConflictCampaignStoreRecord(1, checkpoint);
    }

    private sealed class FakeRecoverySource
        : IConflictCampaignRecoverySource
    {
        private readonly ConflictCampaignStoreRecord? _record;

        public FakeRecoverySource(
            ConflictCampaignStoreRecord? record)
        {
            _record = record;
        }

        public int LoadCount { get; private set; }

        public bool FailNextLoad { get; set; }

        public Task<ConflictCampaignStoreRecord?> LoadMostRecentlySavedAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;

            if (FailNextLoad)
            {
                FailNextLoad = false;
                throw new InvalidOperationException("Synthetic recovery failure.");
            }

            return Task.FromResult(_record);
        }
    }
}
