using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Dashboard;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class PlayerCareerDashboardSnapshotSourceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 30, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MissingProfileReturnsSafeEmptySnapshotWithoutWrites()
    {
        var store = new FakeProfileStore(record: null);
        var source = new PlayerCareerDashboardSnapshotSource(
            new PlayerCareerRuntimeState(store));

        DashboardSnapshot snapshot = await source.GetAsync();

        Assert.Same(DashboardSnapshot.Empty, snapshot);
        Assert.Null(snapshot.Career);
        Assert.Null(snapshot.World);
        Assert.Empty(snapshot.Opportunities);
        Assert.Empty(snapshot.RecentActivity);
        Assert.Empty(snapshot.SocialFeed);
        Assert.Empty(snapshot.Guidance);
        Assert.Equal(1, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task InitializedProfileProjectsOnlyCareerAndLocationAuthority()
    {
        PlayerCareerProfile profile = PlayerCareerProfile.Start(
            Guid.Parse("26cf77f6-6395-46a5-9785-fac2cd43fd65"),
            "KDFW",
            Epoch) with
        {
            Location = new CareerLocation(
                "KDFW",
                "KDAL",
                Epoch.AddHours(1),
                ImmutableHashSet.Create("KDFW", "KDAL"),
                ImmutableHashSet<Guid>.Empty),
            Qualifications = new PilotQualificationState(
                PilotLicenseLevel.Commercial,
                ImmutableHashSet.Create(
                    PilotRating.AirplaneSingleEngineLand,
                    PilotRating.InstrumentAirplane)),
            Experience = new PilotExperienceTotals(
                FlightCount: 7,
                CareerCreditTime: TimeSpan.FromMinutes(765),
                NightCareerCreditTime: TimeSpan.FromHours(2),
                ActualInstrumentCareerCreditTime: TimeSpan.FromMinutes(45),
                TakeoffCount: 9,
                LandingEpisodeCount: 10)
        };
        profile.Validate();

        var store = new FakeProfileStore(
            new PlayerCareerProfileStoreRecord(4, profile, Epoch.AddHours(1)));
        var runtime = new PlayerCareerRuntimeState(store);
        var source = new PlayerCareerDashboardSnapshotSource(runtime);

        DashboardSnapshot snapshot = await source.GetAsync();

        DashboardCareerSummary career = Assert.IsType<DashboardCareerSummary>(snapshot.Career);
        Assert.Equal(
            "Commercial pilot • Airplane single-engine land, Instrument airplane",
            career.LicenseSummary);
        Assert.Equal(12.75, career.TotalFlightHours);
        Assert.Null(career.Level);
        Assert.Null(career.CurrentXp);
        Assert.Null(career.XpForNextLevel);
        Assert.Null(career.AircraftOwned);
        Assert.Null(career.NextMilestone);
        Assert.Null(career.RecentAchievement);

        DashboardWorldSummary world = Assert.IsType<DashboardWorldSummary>(snapshot.World);
        Assert.Equal("KDAL", world.PlayerLocation);
        Assert.Equal("KDFW", world.HomeBase);
        Assert.Null(world.NearbyOpportunityCount);
        Assert.Null(world.ActiveWorldEventCount);
        Assert.Null(world.ActiveMarketSignalCount);
        Assert.Null(world.ActiveGovernmentSignalCount);

        Assert.Null(snapshot.Employment);
        Assert.Null(snapshot.Finances);
        Assert.Null(snapshot.Aircraft);
        Assert.Null(snapshot.ActiveOperation);
        Assert.Empty(snapshot.Opportunities);
        Assert.Empty(snapshot.RecentActivity);
        Assert.Empty(snapshot.SocialFeed);
        Assert.Empty(snapshot.Guidance);
        Assert.Equal(1, store.LoadCount);
        Assert.Equal(0, store.SaveCount);

        DashboardSnapshot replay = await source.GetAsync();
        Assert.Equal(snapshot, replay);
        Assert.Equal(1, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
    }

    private sealed class FakeProfileStore(PlayerCareerProfileStoreRecord? record)
        : IPlayerCareerProfileStore
    {
        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            throw new InvalidOperationException("Dashboard reads must not save career state.");
        }
    }
}
