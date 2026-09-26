using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;
using OpenCareer.Infrastructure.Aircraft;
using OpenCareer.Infrastructure.Flights;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteFlightAirframeConsequenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_root, "consequences.db");
    private OpenCareerDatabaseOptions Options => new(DatabasePath);
    private SqliteAirframeStore Store() => new(Options, NullLogger<SqliteAirframeStore>.Instance);
    private SqliteFlightSessionCheckpointStore Checkpoints() => new(DatabasePath);
    private static DateTimeOffset Now => ConsequenceFixture.Epoch.AddHours(4);
    private FlightAirframeConsequenceCoordinator Coordinator()
    {
        var store = Store();
        return new(store, store, new Clock());
    }
    private Task<AirframeStoreRecord> CreateAsync() => Store().CreateAsync(
        new(new AirframeId(Guid.NewGuid()), ConsequenceFixture.Model, ConsequenceFixture.Epoch.AddDays(-1)),
        new(0.2, AirframeDamageState.None), ConsequenceFixture.Epoch);
    private async Task<AirframeServiceState> StateAsync(AirframeId id) =>
        Assert.IsType<AirframeServiceState>(await Store().ReadServiceStateAsync(id));

    [Fact]
    public async Task FirstApplyAndRestartReplayAreExactOnceAndIsolateSameModelAirframes()
    {
        var a = await CreateAsync();
        var b = await CreateAsync();
        var session = ConsequenceFixture.Session(a.Airframe.AirframeId, verticalSpeeds: [-500, -1600, -800]);
        var first = (await Coordinator().ApplyAsync(session))!;
        Assert.True(first.WasNewlyApplied);
        Assert.Equal(a, first.Application.Before);
        Assert.Equal(2, first.Application.After.Revision);
        Assert.Equal(0.2016, first.Application.After.Condition.WearFraction, 12);
        Assert.Equal(AirframeDamageState.Recorded, first.Application.After.Condition.Damage);
        Assert.Equal(first.Application.After, await Store().FindAsync(a.Airframe.AirframeId));
        Assert.Equal(b, await Store().FindAsync(b.Airframe.AirframeId));
        Assert.Equal(1, (await StateAsync(a.Airframe.AirframeId)).TotalTrackedLandingCycles);
        Assert.Equal(0, (await StateAsync(b.Airframe.AirframeId)).TotalTrackedLandingCycles);
        await Checkpoints().SaveAsync(session);
        var recovered = (await Checkpoints().LoadAsync())!;
        var replay = (await Coordinator().ApplyAsync(recovered))!;
        Assert.False(replay.WasNewlyApplied);
        Assert.Equal(Json(first.Application), Json(replay.Application));
        Assert.Equal(Json(first.Application), Json(await Store().FindBySessionAsync(session.SessionId)));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
        Assert.Equal(first.Application.After, await Store().FindAsync(a.Airframe.AirframeId));
        Assert.Equal(1, (await StateAsync(a.Airframe.AirframeId)).TotalTrackedLandingCycles);
    }

    [Fact]
    public async Task ConcurrentSameSessionWritersCommitOnlyOnce()
    {
        var before = await CreateAsync();
        var decision = FlightAirframeConsequenceCalculator.Calculate(
            ConsequenceFixture.Session(before.Airframe.AirframeId, verticalSpeeds: [-500]));
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => Store().ApplyAsync(decision, before, Now))));
        Assert.Single(results, r => r.WasNewlyApplied);
        Assert.Equal(2, (await Store().FindAsync(before.Airframe.AirframeId))!.Revision);
        Assert.Equal(1, (await StateAsync(before.Airframe.AirframeId)).TotalTrackedLandingCycles);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
    }

    [Fact]
    public async Task SameSessionDifferentPhysicalAirframeOrWeakerContactEvidenceCannotRewriteHistory()
    {
        var a = await CreateAsync();
        var b = await CreateAsync();
        var session = ConsequenceFixture.Session(a.Airframe.AirframeId, verticalSpeeds: [-1800, -500]);
        await Coordinator().ApplyAsync(session);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator().ApplyAsync(session with
        {
            AircraftIdentity = new(ConsequenceFixture.Model, b.Airframe.AirframeId)
        }));
        var episode = session.EffectiveLandingEpisodes[0];
        var weaker = episode.EffectiveContacts[1] with { VerticalSpeedFeetPerMinute = -600 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator().ApplyAsync(session with
        {
            LandingEpisodes = [episode with { Contacts = episode.EffectiveContacts.SetItem(1, weaker) }]
        }));
        Assert.Equal(b, await Store().FindAsync(b.Airframe.AirframeId));
        Assert.Equal(2, (await Store().FindAsync(a.Airframe.AirframeId))!.Revision);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
    }

    [Fact]
    public async Task NewSessionWithStaleConditionFailsButRetainedReplayNeverOverwritesLaterCondition()
    {
        var before = await CreateAsync();
        var session = ConsequenceFixture.Session(before.Airframe.AirframeId, verticalSpeeds: [-1000]);
        var first = (await Coordinator().ApplyAsync(session))!;
        var newer = await Store().UpdateConditionAsync(before.Airframe, new(0.8, AirframeDamageState.Grounding), 2, Now);
        var other = FlightAirframeConsequenceCalculator.Calculate(session with { SessionId = Guid.NewGuid() });
        await Assert.ThrowsAsync<AirframeConcurrencyException>(() => Store().ApplyAsync(other, before, Now));
        Assert.False((await Coordinator().ApplyAsync(session))!.WasNewlyApplied);
        Assert.Equal(newer, await Store().FindAsync(before.Airframe.AirframeId));
        Assert.Equal(Json(first.Application), Json(await Store().FindBySessionAsync(session.SessionId)));
    }

    [Theory]
    [InlineData("BEFORE INSERT ON flight_airframe_consequences")]
    [InlineData("AFTER INSERT ON flight_airframe_consequences")]
    [InlineData("AFTER UPDATE ON airframes")]
    public async Task TransactionFailureRollsBackConditionAndHistoryAndRetrySucceedsOnce(string failurePoint)
    {
        var before = await CreateAsync();
        var session = ConsequenceFixture.Session(before.Airframe.AirframeId, verticalSpeeds: [-2000]);
        await ExecuteAsync($"CREATE TRIGGER injected_failure {failurePoint} BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Coordinator().ApplyAsync(session));
        Assert.Equal(before, await Store().FindAsync(before.Airframe.AirframeId));
        Assert.Null(await Store().FindBySessionAsync(session.SessionId));
        await ExecuteAsync("DROP TRIGGER injected_failure;");
        Assert.True((await Coordinator().ApplyAsync(session))!.WasNewlyApplied);
        Assert.False((await Coordinator().ApplyAsync(session))!.WasNewlyApplied);
        Assert.Equal(2, (await Store().FindAsync(before.Airframe.AirframeId))!.Revision);
    }

    [Theory]
    [InlineData("PRAGMA foreign_keys=OFF; UPDATE flight_airframe_consequences SET airframe_id='wrong';")]
    [InlineData("UPDATE flight_airframe_consequences SET applied_at_utc_ticks=1;")]
    [InlineData("UPDATE flight_airframe_consequences SET payload_json='{}';")]
    [InlineData("UPDATE flight_airframe_consequences SET payload_json=json_remove(payload_json,'$.consequence.summary.crashReported');")]
    [InlineData("UPDATE flight_airframe_consequences SET payload_json=json_set(payload_json,'$.after.revision',99);")]
    [InlineData("UPDATE flight_airframe_consequences SET payload_json=json_set(payload_json,'$.consequence.severity',4);")]
    public async Task CorruptOrMismatchedHistoryFailsClosed(string corruption)
    {
        var before = await CreateAsync();
        var session = ConsequenceFixture.Session(before.Airframe.AirframeId, verticalSpeeds: [-400]);
        var applied = (await Coordinator().ApplyAsync(session))!;
        await ExecuteAsync(corruption);
        var error = await Record.ExceptionAsync(() => Coordinator().ApplyAsync(session));
        Assert.True(error is InvalidDataException or JsonException or ArgumentException, error?.ToString());
        Assert.Equal(applied.Application.After, await Store().FindAsync(before.Airframe.AirframeId));
    }

    [Theory]
    [InlineData(FlightSessionStatus.Completed, false, true, AirframeDamageState.Recorded)]
    [InlineData(FlightSessionStatus.Cancelled, false, true, AirframeDamageState.Recorded)]
    [InlineData(FlightSessionStatus.Interrupted, true, true, AirframeDamageState.Grounding)]
    [InlineData(FlightSessionStatus.Interrupted, false, false, AirframeDamageState.None)]
    public async Task TerminalCheckpointIsClearedOnlyAfterDurableDecision(FlightSessionStatus status, bool crash, bool mutation, AirframeDamageState damage)
    {
        var before = await CreateAsync();
        var session = ConsequenceFixture.Session(before.Airframe.AirframeId, status, crash, -1500);
        await Checkpoints().SaveAsync(session);
        var runtime = new FlightSessionCoordinator();
        var checkpoint = new ObservedCheckpoint(Checkpoints());
        checkpoint.BeforeClear = async () =>
        {
            var retained = Assert.IsType<FlightAirframeApplication>(await Store().FindBySessionAsync(session.SessionId));
            Assert.Equal(status, retained.Consequence.Summary.TerminalStatus);
            Assert.Equal(mutation, retained.Consequence.ApplyCondition);
            Assert.Equal(damage, retained.After.Condition.Damage);
            Assert.Equal(mutation ? 2 : 1, retained.After.Revision);
            Assert.Equal(status, (await Checkpoints().LoadAsync())!.Status);
        };
        var persistence = new FlightSessionPersistenceService(runtime, checkpoint, airframeConsequences: Coordinator());
        await persistence.RecoverAsync();
        await persistence.ClearTerminalAsync(session.SessionId, session.ContractId!.Value);
        Assert.Equal(1, checkpoint.ClearCount);
        Assert.Null(await Checkpoints().LoadAsync());
        Assert.Null(runtime.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsequenceOrCheckpointFailureRetainsTerminalEvidenceAndRestartRetryIsSafe(bool failAfterConsequence)
    {
        var before = await CreateAsync();
        var session = ConsequenceFixture.Session(before.Airframe.AirframeId, verticalSpeeds: [-1500]);
        await Checkpoints().SaveAsync(session);
        var checkpoint = new ObservedCheckpoint(Checkpoints()) { FailClear = failAfterConsequence };
        if (!failAfterConsequence)
            await ExecuteAsync("CREATE TRIGGER injected_failure BEFORE INSERT ON flight_airframe_consequences BEGIN SELECT RAISE(ABORT, 'test'); END;");
        var runtime = new FlightSessionCoordinator();
        var persistence = new FlightSessionPersistenceService(runtime, checkpoint, airframeConsequences: Coordinator());
        await persistence.RecoverAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => persistence.ClearTerminalAsync());
        Assert.NotNull(runtime.Current);
        Assert.NotNull(await Checkpoints().LoadAsync());
        Assert.Equal(failAfterConsequence ? 2 : 1, (await Store().FindAsync(before.Airframe.AirframeId))!.Revision);
        if (!failAfterConsequence) await ExecuteAsync("DROP TRIGGER injected_failure;");
        var restarted = new FlightSessionCoordinator();
        var recoveredPersistence = new FlightSessionPersistenceService(restarted, Checkpoints(), airframeConsequences: Coordinator());
        await recoveredPersistence.RecoverAsync();
        await recoveredPersistence.ClearTerminalAsync();
        Assert.Null(restarted.Current);
        Assert.Null(await Checkpoints().LoadAsync());
        Assert.Equal(2, (await Store().FindAsync(before.Airframe.AirframeId))!.Revision);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("model")]
    [InlineData("configuration")]
    public async Task MissingAirframeWrongModelOrMissingCoordinatorCannotLoseCheckpoint(string fault)
    {
        var before = await CreateAsync();
        var session = ConsequenceFixture.Session(before.Airframe.AirframeId);
        if (fault == "missing") session = session with { AircraftIdentity = new(ConsequenceFixture.Model, new AirframeId(Guid.NewGuid())) };
        if (fault == "model") session = session with { AircraftIdentity = new("different-model", before.Airframe.AirframeId) };
        await Checkpoints().SaveAsync(session);
        var runtime = new FlightSessionCoordinator();
        var persistence = new FlightSessionPersistenceService(runtime, Checkpoints(), airframeConsequences: fault == "configuration" ? null : Coordinator());
        await persistence.RecoverAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => persistence.ClearTerminalAsync());
        Assert.NotNull(await Checkpoints().LoadAsync());
        Assert.Equal(before, await Store().FindAsync(before.Airframe.AirframeId));
        Assert.Null(await Store().FindBySessionAsync(session.SessionId));
    }

    [Fact]
    public async Task ModelOnlyCleanupDoesNotReadOrMutatePhysicalFleet()
    {
        var before = await CreateAsync();
        var session = ConsequenceFixture.Session() with { AircraftIdentity = new(ConsequenceFixture.Model) };
        await Checkpoints().SaveAsync(session);
        var persistence = new FlightSessionPersistenceService(new(), Checkpoints(), airframeConsequences: Coordinator());
        await persistence.RecoverAsync();
        await persistence.ClearTerminalAsync();
        Assert.Equal(before, await Store().FindAsync(before.Airframe.AirframeId));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
    }

    [Fact]
    public async Task Schema14MigrationPreservesPhysicalAndPlayableDataAndFreshSchemaIsCurrent()
    {
        var before = await CreateAsync();
        var session = ConsequenceFixture.Session(before.Airframe.AirframeId);
        await Checkpoints().SaveAsync(session);
        await ExecuteAsync("""
            INSERT INTO job_contracts VALUES ('contract-sentinel', 1, 1, 3, 100, '{"preserve":"contract"}');
            INSERT INTO economy_ledger_transactions VALUES ('ledger-sentinel', 'idempotency-sentinel', 100, 'preserve', 'contract', 'contract-sentinel');
            DROP TABLE flight_airframe_consequences;
            PRAGMA user_version = 14;
            """);
        string rows = await AllRowsAsync();
        Assert.Null(await Store().FindBySessionAsync(session.SessionId));
        Assert.Equal(18L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(rows, await AllRowsAsync());
        Assert.Equal(before, await Store().FindAsync(before.Airframe.AirframeId));
        Assert.Equal(session.SessionId, (await Checkpoints().LoadAsync())!.SessionId);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
    }

    [Fact]
    public async Task SuccessfulProductionFinalizerReleasesReservationThenAppliesBeforeClearAndReplays()
    {
        var before = await CreateAsync();
        var session = ConsequenceFixture.Session(before.Airframe.AirframeId, verticalSpeeds: [-1500]);
        await Checkpoints().SaveAsync(session);
        var fleet = new SqliteAircraftAvailabilityStore(DatabasePath);
        var reservation = JobAcceptanceFleetBridge.GetReservationId(session.ContractId!.Value);
        await fleet.SetAsync(new(ConsequenceFixture.Model, AircraftAvailabilityStatus.Unavailable, reservation));
        var runtime = new FlightSessionCoordinator();
        var checkpoints = new ObservedCheckpoint(Checkpoints());
        checkpoints.BeforeClear = async () =>
        {
            Assert.NotNull(await Store().FindBySessionAsync(session.SessionId));
            Assert.Null(await fleet.FindByReservationIdAsync(reservation));
        };
        var persistence = new FlightSessionPersistenceService(runtime, checkpoints, airframeConsequences: Coordinator());
        await persistence.RecoverAsync();
        var finalizer = new CareerFlightFinalizationCoordinator(new(fleet, fleet), persistence, runtime);
        var entry = CareerEntry(session);
        var profile = PlayerCareerProfile.Start(Guid.NewGuid(), "KJFK", ConsequenceFixture.Epoch) with
        { AppliedExperienceDebriefIds = ImmutableHashSet<Guid>.Empty.Add(entry.Debrief.DebriefId) };
        var appliedProfile = new PlayerCareerProfileStoreRecord(1, profile, Now);
        var result = await finalizer.FinalizeAsync(new(LogbookAppendDisposition.Appended, entry), appliedProfile);
        Assert.Equal(CareerFlightFinalizationStatus.Finalized, result.Status);
        Assert.Equal(CareerFlightReservationReleaseStatus.Released, result.ReservationRelease.Status);
        Assert.Null(runtime.Current);
        var replay = await finalizer.FinalizeAsync(new(LogbookAppendDisposition.AlreadyExists, entry), appliedProfile);
        Assert.Equal(CareerFlightFinalizationStatus.AlreadyFinalized, replay.Status);
        Assert.Equal(1, checkpoints.ClearCount);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
    }

    private static LogbookEntry CareerEntry(FlightSession session)
    {
        var route = new FlightRouteDebrief("KJFK", "KJFK", "KJFK", "KJFK", null, 5);
        var debrief = FlightDebriefFactory.Create(new FlightDebriefDraft(
            session.SessionId, session.SessionId, session.ContractId, LogbookEntryKind.CareerJob, session.CreatedAt, session.UpdatedAt,
            route, new AircraftDebrief("C172"), session.TimeLedger, session.Tracking,
            [new FlightLegDebrief(session.SessionId, 1, session.CreatedAt, session.UpdatedAt, route, session.TimeLedger, [], [1])],
            new FlightFuelDebrief(null, null, null, EvidenceQuality.Unavailable),
            new PayloadDebrief(null, 0, "Development", "Completed", EvidenceQuality.MissionDeclared),
            new FlightAssistanceDebrief(false, false, false, false, false),
            FlightSafetyOutcome.CompletedNormally, MissionOutcome.Succeeded,
            [new LandingDebrief(1, session.EffectiveLandingEpisodes[0].TouchdownAt, LandingOperationType.FullStop,
                0, null, null, null, null, null, null, EvidenceQuality.Unavailable)], [],
            new FlightSettlementRecord(SettlementRecordStatus.Settled, $"contract:{session.ContractId:D}:settlement-v1",
                session.ContractId!.Value.ToString("D"), Now, 0m, 0d)));
        return LogbookEntry.Commit(session.SessionId, debrief, Now, LogbookCommitKind.AutomaticCareerSettlement);
    }

    private async Task<string> AllRowsAsync()
    {
        await using var db = await OpenAsync();
        var tables = new List<string>();
        await using (var command = db.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name <> 'flight_airframe_consequences' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var rows = new Dictionary<string, List<object[]>>();
        foreach (string table in tables)
        {
            rows[table] = [];
            await using var command = db.CreateCommand();
            command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\" ORDER BY rowid;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { var values = new object[reader.FieldCount]; reader.GetValues(values); rows[table].Add(values); }
        }
        return Json(rows);
    }
    private async Task<SqliteConnection> OpenAsync()
    {
        var db = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await db.OpenAsync();
        return db;
    }
    private async Task ExecuteAsync(string sql)
    {
        await using var db = await OpenAsync();
        await using var command = db.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
    private async Task<object?> ScalarAsync(string sql)
    {
        await using var db = await OpenAsync();
        await using var command = db.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync();
    }
    private static string Json(object? value) => JsonSerializer.Serialize(value);
    public void Dispose()
    {
        using var pool = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString());
        SqliteConnection.ClearPool(pool);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class ObservedCheckpoint(IFlightSessionCheckpointStore inner) : IFlightSessionCheckpointStore
    {
        public Func<Task>? BeforeClear { get; set; }
        public bool FailClear { get; set; }
        public int ClearCount { get; private set; }
        public Task SaveAsync(FlightSession session, CancellationToken cancellationToken = default) => inner.SaveAsync(session, cancellationToken);
        public Task<FlightSession?> LoadAsync(CancellationToken cancellationToken = default) => inner.LoadAsync(cancellationToken);
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            if (BeforeClear is not null) await BeforeClear();
            if (FailClear) throw new IOException("Injected checkpoint failure after durable consequence.");
            await inner.ClearAsync(cancellationToken);
            ClearCount++;
        }
    }
}
