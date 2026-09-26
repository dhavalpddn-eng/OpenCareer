using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteLogbookStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

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
    public async Task AppendRoundTripsFrozenDebriefAcrossStoreInstances()
    {
        LogbookEntry expected = ManualEntry(
            Guid.Parse("51000000-0000-0000-0000-000000000001"),
            Guid.Parse("52000000-0000-0000-0000-000000000001"),
            "Cessna 172",
            "KAAA",
            "KBBB",
            "Medical supplies");

        SqliteLogbookStore first = CreateStore();

        LogbookAppendResult result = await first.TryAppendAsync(
            expected,
            "manual:round-trip");

        Assert.Equal(LogbookAppendDisposition.Appended, result.Disposition);
        Assert.True(File.Exists(_databasePath));

        SqliteLogbookStore second = CreateStore();
        LogbookEntry? loaded = await second.GetAsync(expected.EntryId);

        Assert.NotNull(loaded);
        Assert.Equal(expected.EntryId, loaded.EntryId);
        Assert.Equal(expected.Debrief.DebriefId, loaded.Debrief.DebriefId);
        Assert.Equal("Medical supplies", loaded.Debrief.Payload.CargoDescription);
        Assert.Equal(3, loaded.Debrief.Legs[0].RouteTrack.Count);
        Assert.Equal(-205, loaded.Debrief.Landings[0].VerticalSpeedFeetPerMinute);
        Assert.Equal("Crosswind on final", loaded.Debrief.Events[0].Text);
    }

    [Fact]
    public async Task IdempotencyLookupRoundTripsStoredEntry()
    {
        SqliteLogbookStore first =
            CreateStore();

        LogbookEntry expected =
            ManualEntry(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Cessna 172",
                "KAAA",
                "KBBB",
                "Boxes");

        const string idempotencyKey =
            "manual:idempotency-lookup";

        await first.TryAppendAsync(
            expected,
            idempotencyKey);

        SqliteLogbookStore reopened =
            CreateStore();

        LogbookEntry? recovered =
            await reopened.FindByIdempotencyKeyAsync(
                idempotencyKey);

        Assert.NotNull(recovered);
        Assert.Equal(
            expected.EntryId,
            recovered.EntryId);
        Assert.Equal(
            expected.Debrief.DebriefId,
            recovered.Debrief.DebriefId);

        Assert.Null(
            await reopened.FindByIdempotencyKeyAsync(
                "manual:missing"));
    }

    [Fact]
    public async Task RepeatedIdempotencyKeyReturnsOriginalEntry()
    {
        SqliteLogbookStore store = CreateStore();
        LogbookEntry first = ManualEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Cessna 172",
            "KAAA",
            "KBBB",
            "Boxes");

        LogbookAppendResult appended = await store.TryAppendAsync(
            first,
            "manual:same-flight");

        LogbookEntry retry = first with
        {
            EntryId = Guid.NewGuid(),
            CommittedAt = first.CommittedAt.AddMinutes(1)
        };

        LogbookAppendResult duplicate = await store.TryAppendAsync(
            retry,
            "manual:same-flight");

        Assert.Equal(LogbookAppendDisposition.Appended, appended.Disposition);
        Assert.Equal(LogbookAppendDisposition.AlreadyExists, duplicate.Disposition);
        Assert.Equal(first.EntryId, duplicate.Entry.EntryId);

        IReadOnlyList<LogbookEntry> all = await store.QueryAsync(
            new LogbookQuery(Limit: 100));

        Assert.Single(all);
    }

    [Fact]
    public async Task IdempotencyCollisionWithDifferentDebriefIsRejected()
    {
        SqliteLogbookStore store = CreateStore();

        await store.TryAppendAsync(
            ManualEntry(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Cessna 172",
                "KAAA",
                "KBBB",
                "Boxes"),
            "manual:collision");

        LogbookEntry different = ManualEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "DA40",
            "KCCC",
            "KDDD",
            "Parts");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.TryAppendAsync(
                different,
                "manual:collision"));
    }

    [Fact]
    public async Task QueryUsesIndexedFiltersAndSearchText()
    {
        SqliteLogbookStore store = CreateStore();

        LogbookEntry alpha = ManualEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Cessna 172",
            "KAAA",
            "KBBB",
            "Medical supplies");

        LogbookEntry bravo = ManualEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "DA40",
            "KCCC",
            "KDDD",
            "Machine parts",
            safetyOutcome: FlightSafetyOutcome.CompletedWithIncident,
            endedAt: Start.AddHours(5));

        await store.TryAppendAsync(alpha, "manual:alpha");
        await store.TryAppendAsync(bravo, "manual:bravo");

        IReadOnlyList<LogbookEntry> bySearch = await store.QueryAsync(
            new LogbookQuery(SearchText: "medical", Limit: 100));
        Assert.Single(bySearch);
        Assert.Equal(alpha.EntryId, bySearch[0].EntryId);

        IReadOnlyList<LogbookEntry> byAircraft = await store.QueryAsync(
            new LogbookQuery(SearchText: "da40", Limit: 100));
        Assert.Single(byAircraft);
        Assert.Equal(bravo.EntryId, byAircraft[0].EntryId);

        IReadOnlyList<LogbookEntry> byOutcome = await store.QueryAsync(
            new LogbookQuery(
                SafetyOutcome: FlightSafetyOutcome.CompletedWithIncident,
                Limit: 100));
        Assert.Single(byOutcome);
        Assert.Equal(bravo.EntryId, byOutcome[0].EntryId);

        IReadOnlyList<LogbookEntry> byDate = await store.QueryAsync(
            new LogbookQuery(
                From: Start.AddHours(4),
                Limit: 100));
        Assert.Single(byDate);
        Assert.Equal(bravo.EntryId, byDate[0].EntryId);
    }

    [Fact]
    public async Task ConcurrentRetriesCreateOneEntry()
    {
        SqliteLogbookStore store = CreateStore();
        LogbookEntry entry = ManualEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Cessna 172",
            "KAAA",
            "KBBB",
            "Boxes");

        Task<LogbookAppendResult>[] attempts = Enumerable.Range(0, 12)
            .Select(_ => store.TryAppendAsync(entry, "manual:concurrent"))
            .ToArray();

        LogbookAppendResult[] results = await Task.WhenAll(attempts);

        Assert.Single(
            results,
            static result => result.Disposition == LogbookAppendDisposition.Appended);
        Assert.Equal(
            11,
            results.Count(
                static result => result.Disposition == LogbookAppendDisposition.AlreadyExists));

        IReadOnlyList<LogbookEntry> entries = await store.QueryAsync(
            new LogbookQuery(Limit: 100));
        Assert.Single(entries);
    }

    [Fact]
    public async Task UnknownEntryReturnsNull()
    {
        SqliteLogbookStore store = CreateStore();

        LogbookEntry? entry = await store.GetAsync(Guid.NewGuid());

        Assert.Null(entry);
        Assert.True(File.Exists(_databasePath));
    }

    private SqliteLogbookStore CreateStore() =>
        new(
            new OpenCareerDatabaseOptions(_databasePath),
            NullLogger<SqliteLogbookStore>.Instance);

    private static LogbookEntry ManualEntry(
        Guid entryId,
        Guid debriefId,
        string aircraft,
        string origin,
        string destination,
        string cargo,
        FlightSafetyOutcome safetyOutcome = FlightSafetyOutcome.CompletedNormally,
        DateTimeOffset? endedAt = null)
    {
        DateTimeOffset end = endedAt ?? Start.AddMinutes(90);
        DateTimeOffset start = end.AddMinutes(-90);

        var time = new FlightTimeLedger(
            TimeSpan.FromMinutes(90),
            TimeSpan.FromMinutes(90),
            TimeSpan.FromMinutes(80),
            TimeSpan.FromMinutes(70),
            TimeSpan.FromMinutes(55),
            TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(7),
            TimeSpan.FromMinutes(60),
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(8));

        var landing = new LandingDebrief(
            1,
            end.AddMinutes(-10),
            LandingOperationType.FullStop,
            0,
            -205,
            1.10,
            61,
            4,
            1,
            false,
            EvidenceQuality.DerivedHighConfidence);

        var leg = new FlightLegDebrief(
            Guid.NewGuid(),
            1,
            start,
            end,
            new(origin, destination, origin, destination, null, 150),
            time,
            [
                new(start.AddMinutes(5), 32.8, -97.3, 900),
                new(start.AddMinutes(45), 33.2, -96.8, 6500),
                new(end.AddMinutes(-5), 33.6, -96.2, 1200)
            ],
            [1]);

        FlightDebrief debrief = FlightDebriefFactory.Create(
            new(
                debriefId,
                Guid.NewGuid(),
                null,
                LogbookEntryKind.FreeFlight,
                start,
                end,
                new(origin, destination, origin, destination, null, 150),
                new(aircraft, aircraft, "N123OC"),
                time,
                new(
                    FlightTrackingState.Complete,
                    null,
                    end,
                    1,
                    1,
                    0,
                    0,
                    0,
                    false),
                [leg],
                new(220, 145, 75, EvidenceQuality.Observed),
                new(1, 120, cargo, "Delivered", EvidenceQuality.Observed),
                new(false, false, false, false, false),
                safetyOutcome,
                MissionOutcome.NotApplicable,
                [landing],
                [
                    new(
                        Guid.NewGuid(),
                        end.AddMinutes(-20),
                        "Weather",
                        DebriefEventSeverity.Advisory,
                        "Crosswind on final",
                        EvidenceQuality.Observed)
                ],
                FlightSettlementRecord.NotApplicable));

        return LogbookEntry.Commit(
            entryId,
            debrief,
            end.AddMinutes(1),
            LogbookCommitKind.ManualPilotLog);
    }
}
