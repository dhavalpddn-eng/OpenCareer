using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Events;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteWorldEventStateStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private string _root = string.Empty;
    private string _databasePath = string.Empty;

    public Task InitializeAsync()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_root, "opencareer.db");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Test cleanup should not mask the actual assertion result.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task SaveRoundTripsAcrossStoreInstances()
    {
        WorldEventStateSnapshot expected = Snapshot(
            "world:dfw",
            Start,
            RegionalEvent(
                "event:regional-cargo-surge:1",
                Start.AddHours(-1),
                Start.AddHours(5)));

        SqliteWorldEventStateStore first = CreateStore();
        await first.SaveAsync(expected);

        SqliteWorldEventStateStore second = CreateStore();
        WorldEventStateSnapshot? loaded = await second.GetAsync(expected.SnapshotId);

        Assert.NotNull(loaded);
        Assert.Equal(expected.SnapshotId, loaded.SnapshotId);
        Assert.Equal(expected.UpdatedAt, loaded.UpdatedAt);
        Assert.Equal(expected.Location, loaded.Location);
        Assert.Single(loaded.Events);
        Assert.Equal(expected.Events[0].InstanceId, loaded.Events[0].InstanceId);
        Assert.Equal(expected.Events[0].DefinitionId, loaded.Events[0].DefinitionId);
        Assert.Equal(expected.Events[0].Effects, loaded.Events[0].Effects);
        Assert.Equal(
            expected.Events[0].AffectedMarketSegments,
            loaded.Events[0].AffectedMarketSegments);
        Assert.True(File.Exists(_databasePath));
    }

    [Fact]
    public async Task NewerSnapshotReplacesExistingState()
    {
        WorldEventStateSnapshot original = Snapshot(
            "world:dfw",
            Start,
            RegionalEvent(
                "event:fuel-supply-crisis:1",
                Start.AddHours(-1),
                Start.AddHours(8)));

        WorldEventStateSnapshot updated = Snapshot(
            "world:dfw",
            Start.AddHours(1),
            original.Events[0],
            AirportEvent(
                "event:ground-services:1",
                Start.AddMinutes(30),
                Start.AddHours(4)));

        SqliteWorldEventStateStore store = CreateStore();

        await store.SaveAsync(original);
        await store.SaveAsync(updated);

        WorldEventStateSnapshot? loaded = await store.GetAsync(original.SnapshotId);

        Assert.NotNull(loaded);
        Assert.Equal(updated.UpdatedAt, loaded.UpdatedAt);
        Assert.Equal(2, loaded.Events.Length);
        Assert.Contains(
            loaded.Events,
            static worldEvent => worldEvent.InstanceId == "event:ground-services:1");
    }

    [Fact]
    public async Task StaleSnapshotCannotRollBackNewerState()
    {
        WorldEventInstance worldEvent = RegionalEvent(
            "event:wildfire:1",
            Start.AddHours(-2),
            Start.AddHours(10));

        WorldEventStateSnapshot stale = Snapshot(
            "world:dfw",
            Start,
            worldEvent);
        WorldEventStateSnapshot newer = Snapshot(
            "world:dfw",
            Start.AddHours(2),
            worldEvent);

        SqliteWorldEventStateStore store = CreateStore();

        await store.SaveAsync(newer);
        await store.SaveAsync(stale);

        WorldEventStateSnapshot? loaded = await store.GetAsync(stale.SnapshotId);

        Assert.NotNull(loaded);
        Assert.Equal(newer.UpdatedAt, loaded.UpdatedAt);
    }

    [Fact]
    public async Task LoadAllUsesStableSnapshotIdOrdering()
    {
        SqliteWorldEventStateStore store = CreateStore();

        await store.SaveAsync(Snapshot(
            "world:tx",
            Start,
            RegionalEvent("event:tx:1", Start.AddHours(-1), Start.AddHours(6))));
        await store.SaveAsync(Snapshot(
            "world:ok",
            Start,
            RegionalEvent("event:ok:1", Start.AddHours(-1), Start.AddHours(6))));
        await store.SaveAsync(Snapshot(
            "world:ar",
            Start,
            RegionalEvent("event:ar:1", Start.AddHours(-1), Start.AddHours(6))));

        IReadOnlyList<WorldEventStateSnapshot> snapshots = await store.LoadAllAsync();

        Assert.Equal(
            [
                "world:ar",
                "world:ok",
                "world:tx"
            ],
            snapshots.Select(static snapshot => snapshot.SnapshotId).ToArray());
    }

    [Fact]
    public async Task SnapshotRejectsEventsThatAreNotActiveAtCheckpointTime()
    {
        SqliteWorldEventStateStore store = CreateStore();
        WorldEventStateSnapshot invalid = Snapshot(
            "world:dfw",
            Start,
            RegionalEvent(
                "event:expired:1",
                Start.AddHours(-3),
                Start.AddHours(-1)));

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAsync(invalid));
    }

    private SqliteWorldEventStateStore CreateStore() =>
        new(
            new OpenCareerDatabaseOptions(_databasePath),
            NullLogger<SqliteWorldEventStateStore>.Instance);

    private static WorldEventStateSnapshot Snapshot(
        string snapshotId,
        DateTimeOffset updatedAt,
        params WorldEventInstance[] events) =>
        new(
            snapshotId,
            updatedAt,
            new(
                RegionId: "US-TX",
                NationId: "US",
                OriginAirport: "KDFW",
                DestinationAirport: "KDAL"),
            events);

    private static WorldEventInstance RegionalEvent(
        string instanceId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt) =>
        new(
            instanceId,
            "regional-cargo-surge",
            "Regional Cargo Surge",
            WorldEventTier.LocalOperational,
            WorldEventScope.Region,
            "US-TX",
            startsAt,
            endsAt,
            new(
                DemandMultiplier: 1.15,
                MissionOpportunities: MissionOpportunity.Cargo),
            [MarketSegment.GeneralCargo, MarketSegment.ExpressCargo]);

    private static WorldEventInstance AirportEvent(
        string instanceId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt) =>
        new(
            instanceId,
            "airport-ground-services-failure",
            "Airport Ground Services Failure",
            WorldEventTier.LocalOperational,
            WorldEventScope.Airport,
            "KDFW",
            startsAt,
            endsAt,
            new(
                CapacityMultiplier: 0.65,
                AirportServiceCapacityMultiplier: 0.35));
}
