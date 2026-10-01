using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Dashboard;
using OpenCareer.Application.Economy;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class PlayerCareerDashboardSnapshotSourceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 30, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MissingProfileReturnsFinanceOnlyWithoutWrites()
    {
        var store = new FakeProfileStore(record: null);
        var ledger = new FakeLedgerStore();
        var source = new PlayerCareerDashboardSnapshotSource(
            new PlayerCareerRuntimeState(store),
            ledger,
            new FakeLogbookSource(),
            new FixedTimeProvider(Epoch));

        DashboardSnapshot snapshot = await source.GetAsync();

        Assert.Null(snapshot.Career);
        Assert.Null(snapshot.World);
        DashboardFinanceSummary finances =
            Assert.IsType<DashboardFinanceSummary>(snapshot.Finances);
        Assert.Equal(0m, finances.Cash);
        Assert.Equal(0m, finances.TodayNet);
        Assert.Null(finances.UpcomingObligations);
        Assert.Empty(snapshot.Opportunities);
        Assert.Empty(snapshot.RecentActivity);
        Assert.Empty(snapshot.SocialFeed);
        Assert.Empty(snapshot.Guidance);
        Assert.Equal(1, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(1, ledger.CashReadCount);
        Assert.Equal(1, ledger.RecentReadCount);
        Assert.Equal(0, ledger.PostCount);
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
        var ledger = new FakeLedgerStore(
            cash: 1_234.56m,
            transactions:
            [
                Transaction("today-positive", Epoch.AddMinutes(-1), 150m),
                Transaction("today-negative", Epoch.AddHours(-12), -40.25m),
                Transaction("prior-day", Epoch.AddDays(-1), 999m)
            ]);
        var source = new PlayerCareerDashboardSnapshotSource(
            runtime,
            ledger,
            new FakeLogbookSource(),
            new FixedTimeProvider(Epoch));

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
        DashboardFinanceSummary finances =
            Assert.IsType<DashboardFinanceSummary>(snapshot.Finances);
        Assert.Equal(1_234.56m, finances.Cash);
        Assert.Equal(109.75m, finances.TodayNet);
        Assert.Null(finances.UpcomingObligations);
        Assert.Null(snapshot.Aircraft);
        Assert.Null(snapshot.ActiveOperation);
        Assert.Empty(snapshot.Opportunities);
        Assert.Empty(snapshot.RecentActivity);
        Assert.Empty(snapshot.SocialFeed);
        Assert.Empty(snapshot.Guidance);
        Assert.Equal(1, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(1, ledger.CashReadCount);
        Assert.Equal(1, ledger.RecentReadCount);
        Assert.Equal(0, ledger.PostCount);

        DashboardSnapshot replay = await source.GetAsync();
        Assert.Equal(snapshot, replay);
        Assert.Equal(1, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(2, ledger.CashReadCount);
        Assert.Equal(2, ledger.RecentReadCount);
        Assert.Equal(0, ledger.PostCount);
    }

    [Fact]
    public async Task TodayNetUsesConfiguredLocalCalendarDayBoundaries()
    {
        var ledger = new FakeLedgerStore(
            cash: 25m,
            transactions:
            [
                Transaction("local-today", new DateTimeOffset(2026, 10, 1, 4, 59, 0, TimeSpan.Zero), 25m),
                Transaction("local-prior", new DateTimeOffset(2026, 9, 30, 4, 59, 0, TimeSpan.Zero), 75m)
            ]);
        var source = new PlayerCareerDashboardSnapshotSource(
            new PlayerCareerRuntimeState(new FakeProfileStore(record: null)),
            ledger,
            new FakeLogbookSource(),
            new FixedTimeProvider(
                new DateTimeOffset(2026, 10, 1, 0, 30, 0, TimeSpan.Zero),
                TimeZoneInfo.CreateCustomTimeZone(
                    "Test Central",
                    TimeSpan.FromHours(-5),
                    "Test Central",
                    "Test Central")));

        DashboardSnapshot snapshot = await source.GetAsync();

        DashboardFinanceSummary finances =
            Assert.IsType<DashboardFinanceSummary>(snapshot.Finances);
        Assert.Equal(25m, finances.TodayNet);
        Assert.Equal(0, ledger.PostCount);
    }

    [Fact]
    public async Task RecentCommittedFlightsProjectNewestFirstWithBoundedRead()
    {
        LogbookEntry free = Entry(
            "10000000-0000-0000-0000-000000000001",
            LogbookEntryKind.FreeFlight,
            Epoch.AddHours(-3),
            "Cessna 172",
            plannedOrigin: null,
            plannedDestination: null,
            actualDeparture: null,
            actualArrival: null);
        LogbookEntry career = Entry(
            "10000000-0000-0000-0000-000000000002",
            LogbookEntryKind.CareerJob,
            Epoch.AddHours(-1),
            "Beechcraft King Air",
            plannedOrigin: "KDFW",
            plannedDestination: "KAUS",
            actualDeparture: "KDFW",
            actualArrival: "KAUS");
        LogbookEntry training = Entry(
            "10000000-0000-0000-0000-000000000003",
            LogbookEntryKind.Training,
            Epoch.AddHours(-2),
            "Cessna 152",
            plannedOrigin: "KDAL",
            plannedDestination: "KADS",
            actualDeparture: null,
            actualArrival: null);
        var logbook = new FakeLogbookSource([free, career, training]);
        var source = new PlayerCareerDashboardSnapshotSource(
            new PlayerCareerRuntimeState(new FakeProfileStore(record: null)),
            new FakeLedgerStore(cash: 50m),
            logbook,
            new FixedTimeProvider(Epoch));

        DashboardSnapshot snapshot = await source.GetAsync();

        Assert.Equal(6, logbook.LastQuery?.Limit);
        Assert.Equal(1, logbook.QueryCount);
        Assert.Equal(0, logbook.GetCount);
        Assert.Equal(0, logbook.AppendCount);
        Assert.Collection(
            snapshot.RecentActivity,
            activity =>
            {
                Assert.Equal(career.Debrief.EndedAt, activity.Timestamp);
                Assert.Equal("Career flight", activity.Category);
                Assert.Equal(
                    "Beechcraft King Air • Actual KDFW → KAUS • Mission succeeded; completed normally",
                    activity.Text);
                Assert.Equal(DashboardActionTarget.Logbook, activity.Target);
            },
            activity =>
            {
                Assert.Equal(training.Debrief.EndedAt, activity.Timestamp);
                Assert.Equal("Training flight", activity.Category);
                Assert.Equal(
                    "Cessna 152 • Planned KDAL → KADS • Completed normally",
                    activity.Text);
                Assert.Equal(DashboardActionTarget.Logbook, activity.Target);
            },
            activity =>
            {
                Assert.Equal(free.Debrief.EndedAt, activity.Timestamp);
                Assert.Equal("Free flight", activity.Category);
                Assert.Equal(
                    "Cessna 172 • Route unknown • Completed normally",
                    activity.Text);
                Assert.Equal(DashboardActionTarget.Logbook, activity.Target);
            });

        Assert.Null(snapshot.Career);
        Assert.Null(snapshot.World);
        DashboardFinanceSummary finances =
            Assert.IsType<DashboardFinanceSummary>(snapshot.Finances);
        Assert.Equal(50m, finances.Cash);
        Assert.Equal(0m, finances.TodayNet);
    }

    private static LogbookEntry Entry(
        string entryId,
        LogbookEntryKind kind,
        DateTimeOffset endedAt,
        string aircraft,
        string? plannedOrigin,
        string? plannedDestination,
        string? actualDeparture,
        string? actualArrival)
    {
        DateTimeOffset startedAt = endedAt.AddHours(-1);
        var route = new FlightRouteDebrief(
            plannedOrigin,
            plannedDestination,
            actualDeparture,
            actualArrival,
            null,
            null);
        Guid? contractId = kind == LogbookEntryKind.CareerJob
            ? Guid.Parse("20000000-0000-0000-0000-000000000001")
            : null;
        FlightSettlementRecord settlement = kind == LogbookEntryKind.CareerJob
            ? new FlightSettlementRecord(
                SettlementRecordStatus.Settled,
                "dashboard-career-settlement",
                "dashboard-transaction",
                endedAt.AddMinutes(1),
                100m,
                1d)
            : FlightSettlementRecord.NotApplicable;
        MissionOutcome missionOutcome = kind == LogbookEntryKind.CareerJob
            ? MissionOutcome.Succeeded
            : MissionOutcome.NotApplicable;
        FlightDebrief debrief = FlightDebriefFactory.Create(
            new FlightDebriefDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                contractId,
                kind,
                startedAt,
                endedAt,
                route,
                new AircraftDebrief(aircraft),
                FlightTimeLedger.Empty,
                new FlightTrackingSnapshot(
                    FlightTrackingState.Complete,
                    null,
                    endedAt,
                    0,
                    0,
                    0,
                    0,
                    0,
                    false),
                [
                    new FlightLegDebrief(
                        Guid.NewGuid(),
                        1,
                        startedAt,
                        endedAt,
                        route,
                        FlightTimeLedger.Empty,
                        [],
                        [])
                ],
                new FlightFuelDebrief(null, null, null, EvidenceQuality.Unavailable),
                new PayloadDebrief(null, null, null, null, EvidenceQuality.Unavailable),
                new FlightAssistanceDebrief(false, false, false, false, false),
                FlightSafetyOutcome.CompletedNormally,
                missionOutcome,
                [],
                [],
                settlement));

        return LogbookEntry.Commit(
            Guid.Parse(entryId),
            debrief,
            endedAt.AddMinutes(1),
            kind == LogbookEntryKind.CareerJob
                ? LogbookCommitKind.AutomaticCareerSettlement
                : LogbookCommitKind.ManualPilotLog);
    }

    private static EconomyLedgerTransaction Transaction(
        string key,
        DateTimeOffset occurredAt,
        decimal cashChange)
    {
        decimal amount = Math.Abs(cashChange);
        IReadOnlyList<LedgerPosting> postings = cashChange >= 0m
            ?
            [
                LedgerPosting.DebitTo(LedgerAccountCode.Cash, amount, key),
                LedgerPosting.CreditTo(LedgerAccountCode.WageIncome, amount, key)
            ]
            :
            [
                LedgerPosting.DebitTo(LedgerAccountCode.OtherOperatingExpense, amount, key),
                LedgerPosting.CreditTo(LedgerAccountCode.Cash, amount, key)
            ];

        return new EconomyLedgerTransaction(
            Guid.NewGuid(),
            key,
            occurredAt,
            key,
            "dashboard-test",
            key,
            postings);
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

    private sealed class FakeLedgerStore(
        decimal cash = 0m,
        IReadOnlyList<EconomyLedgerTransaction>? transactions = null)
        : IEconomyLedgerStore
    {
        public int CashReadCount { get; private set; }
        public int RecentReadCount { get; private set; }
        public int PostCount { get; private set; }

        public Task<LedgerPostResult> PostAsync(
            EconomyLedgerTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            PostCount++;
            throw new InvalidOperationException("Dashboard reads must not post ledger transactions.");
        }

        public Task<EconomyLedgerTransaction?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<EconomyLedgerTransaction?>(null);

        public Task<decimal> ReadCashBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            CashReadCount++;
            return Task.FromResult(cash);
        }

        public Task<IReadOnlyList<LedgerAccountBalance>> ReadAccountBalancesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LedgerAccountBalance>>([]);

        public Task<IReadOnlyList<EconomyLedgerTransaction>> ReadRecentAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            RecentReadCount++;
            Assert.Equal(500, limit);
            return Task.FromResult(transactions ?? []);
        }
    }

    private sealed class FakeLogbookSource(
        IReadOnlyList<LogbookEntry>? entries = null)
        : ILogbookSource, ILogbookWriter
    {
        public int QueryCount { get; private set; }
        public int GetCount { get; private set; }
        public int AppendCount { get; private set; }
        public LogbookQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<LogbookEntry>> QueryAsync(
            LogbookQuery query,
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            LastQuery = query;
            return Task.FromResult(
                LogbookQueryMatcher.Apply(entries ?? [], query));
        }

        public Task<LogbookEntry?> GetAsync(
            Guid entryId,
            CancellationToken cancellationToken = default)
        {
            GetCount++;
            return Task.FromResult(
                (entries ?? []).SingleOrDefault(entry => entry.EntryId == entryId));
        }

        public Task<LogbookAppendResult> TryAppendAsync(
            LogbookEntry entry,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            AppendCount++;
            throw new InvalidOperationException("Dashboard reads must not append Logbook entries.");
        }
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset utcNow,
        TimeZoneInfo? localTimeZone = null)
        : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone { get; } =
            localTimeZone ?? TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
