using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Infrastructure.Flights;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class AirframeRepairTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_root, "repair.db");
    private static DateTimeOffset Epoch => ConsequenceFixture.Epoch;
    private SqliteAirframeStore Store() => new(new(DatabasePath), NullLogger<SqliteAirframeStore>.Instance);
    private AirframeMaintenanceService Service() { var store = Store(); return new(store, store); }
    private AirframeMaintenanceHistorySource History() { var store = Store(); return new(store, store, store); }
    private Task<AirframeStoreRecord> CreateAsync(AirframeDamageState damage = AirframeDamageState.Recorded, double wear = 0.1234567890123456) =>
        Store().CreateAsync(new(new AirframeId(Guid.NewGuid()), ConsequenceFixture.Model, Epoch.AddDays(-1)), new(wear, damage), Epoch);
    private static AirframeRepairRequest Request(AirframeStoreRecord record) =>
        new(Guid.NewGuid(), record.Airframe.AirframeId, record.Revision, record.SavedAt.AddMinutes(1));

    [Theory]
    [InlineData(AirframeDamageState.Recorded, 0.1234567890123456)]
    [InlineData(AirframeDamageState.Grounding, 0.1234567890123456)]
    [InlineData(AirframeDamageState.Grounding, 1)]
    [InlineData(AirframeDamageState.Recorded, 0)]
    public async Task RepairClearsOnlyDiscreteDamageAndPersistsExactEvent(AirframeDamageState damage, double wear)
    {
        var before = await CreateAsync(damage, wear);
        var other = await CreateAsync(damage, wear);
        var request = Request(before);
        var result = await Service().RepairDiscreteDamageAsync(request);
        Assert.Equal(AirframeRepairStatus.Repaired, result.Status);
        Assert.True(result.WasNewlyApplied);
        var retained = Assert.IsType<AirframeMaintenanceEvent>(result.Event);
        var after = Assert.IsType<AirframeStoreRecord>(result.Current);
        Assert.Equal(before.Airframe, after.Airframe);
        Assert.Equal(BitConverter.DoubleToInt64Bits(before.Condition.WearFraction), BitConverter.DoubleToInt64Bits(after.Condition.WearFraction));
        Assert.Equal(AirframeDamageState.None, after.Condition.Damage);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(request.PerformedAt, after.SavedAt);
        Assert.Equal(request, retained.Request);
        Assert.Equal(before, retained.Before);
        Assert.Equal(after, retained.After);
        Assert.Equal(request.MaintenanceActionId, retained.MaintenanceActionId);
        Assert.Equal(request.AirframeId, retained.AirframeId);
        Assert.Equal(ConsequenceFixture.Model, retained.CanonicalAircraftId);
        Assert.Equal(AirframeMaintenanceEventKind.DiscreteDamageRepair, retained.Kind);
        Assert.Equal(AirframeMaintenanceEvent.DiscreteRepairRationale, retained.Rationale);
        Assert.Equal(after, await Store().FindAsync(request.AirframeId));
        Assert.Equal(other, await Store().FindAsync(other.Airframe.AirframeId));
        var snapshot = (await History().ReadAsync(new(request.AirframeId))).Snapshot!;
        Assert.Equal(AirframeServiceability.AvailableForDispatch, snapshot.Serviceability);
        Assert.True((await new PhysicalAirframeEligibilityService(Store()).EvaluateAsync(ConsequenceFixture.Model, request.AirframeId)).IsEligible);
        Assert.Empty(snapshot.History);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Fact]
    public async Task NoDamageReturnsNoRepairRequiredWithoutConditionOrHistoryWrites()
    {
        var before = await CreateAsync(AirframeDamageState.None, 1);
        await ExecuteAsync("""
            CREATE TRIGGER no_condition_update BEFORE UPDATE ON airframes BEGIN SELECT RAISE(ABORT, 'unexpected update'); END;
            CREATE TRIGGER no_service_insert BEFORE INSERT ON airframe_maintenance_events BEGIN SELECT RAISE(ABORT, 'unexpected insert'); END;
            """);
        var request = Request(before);
        foreach (var result in new[] { await Service().RepairDiscreteDamageAsync(request), await Store().RepairDiscreteDamageAsync(request, before) })
        {
            Assert.Equal(AirframeRepairStatus.NoRepairRequired, result.Status);
            Assert.Equal(before, result.Current);
            Assert.Null(result.Event);
            Assert.False(result.WasNewlyApplied);
        }
        Assert.Equal(before, await Store().FindAsync(request.AirframeId));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Theory]
    [InlineData("action")]
    [InlineData("airframe")]
    [InlineData("revision")]
    [InlineData("timestamp")]
    public async Task InvalidRequestFailsBeforeCreatingDatabase(string fault)
    {
        var valid = new AirframeRepairRequest(Guid.NewGuid(), new AirframeId(Guid.NewGuid()), 1, Epoch);
        var request = fault switch
        {
            "action" => valid with { MaintenanceActionId = Guid.Empty },
            "airframe" => valid with { AirframeId = default },
            "revision" => valid with { ExpectedRevision = 0 },
            _ => valid with { PerformedAt = default }
        };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Service().RepairDiscreteDamageAsync(request));
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task MissingAirframeReturnsNotFoundWithoutCreatingOne()
    {
        var result = await Service().RepairDiscreteDamageAsync(new(Guid.NewGuid(), new AirframeId(Guid.NewGuid()), 1, Epoch));
        Assert.Equal(AirframeRepairStatus.NotFound, result.Status);
        Assert.Null(result.Current);
        Assert.Null(result.Event);
        Assert.False(result.WasNewlyApplied);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframes;"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task BackdatedOrNonAdvancingTimeFailsWithoutWrites(int seconds)
    {
        var before = await CreateAsync();
        var request = Request(before) with { PerformedAt = before.SavedAt.AddSeconds(seconds) };
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service().RepairDiscreteDamageAsync(request));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Store().RepairDiscreteDamageAsync(request, before));
        Assert.Equal(before, await Store().FindAsync(request.AirframeId));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Fact]
    public async Task StaleRevisionAndMismatchedExpectedConditionFailWithoutMutation()
    {
        var before = await CreateAsync();
        var request = Request(before);
        var current = await Store().UpdateConditionAsync(before.Airframe, new(0.75, AirframeDamageState.Grounding), before.Revision, Epoch.AddSeconds(1));
        await Assert.ThrowsAsync<AirframeConcurrencyException>(() => Service().RepairDiscreteDamageAsync(request));
        await Assert.ThrowsAsync<AirframeConcurrencyException>(() => Store().RepairDiscreteDamageAsync(Request(current),
            current with { Condition = new(0.25, AirframeDamageState.Recorded) }));
        Assert.Equal(current, await Store().FindAsync(request.AirframeId));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Fact]
    public async Task IdenticalReplayAfterRestartReturnsRetainedResultAndNeverClearsLaterDamage()
    {
        var before = await CreateAsync(AirframeDamageState.Grounding);
        var request = Request(before);
        var first = await Service().RepairDiscreteDamageAsync(request);
        ClearPool();
        var replay = await Service().RepairDiscreteDamageAsync(request);
        Assert.Equal(AirframeRepairStatus.Repaired, replay.Status);
        Assert.False(replay.WasNewlyApplied);
        Assert.Equal(Json(first.Event!), Json(replay.Event!));
        Assert.Equal(first.Current, replay.Current);
        var newer = await Store().UpdateConditionAsync(before.Airframe, new(0.9, AirframeDamageState.Grounding), first.Current!.Revision, request.PerformedAt.AddMinutes(1));
        Assert.Equal(first.Current, (await Service().RepairDiscreteDamageAsync(request)).Current);
        Assert.False((await Store().RepairDiscreteDamageAsync(request, before)).WasNewlyApplied);
        Assert.Equal(newer, await Store().FindAsync(request.AirframeId));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Theory]
    [InlineData("airframe")]
    [InlineData("revision")]
    [InlineData("timestamp")]
    public async Task ConflictingActionReplayFailsClosed(string fault)
    {
        var before = await CreateAsync();
        var other = await CreateAsync();
        var request = Request(before);
        var applied = await Service().RepairDiscreteDamageAsync(request);
        var conflict = fault switch
        {
            "airframe" => request with { AirframeId = other.Airframe.AirframeId },
            "revision" => request with { ExpectedRevision = before.Revision + 1 },
            _ => request with { PerformedAt = request.PerformedAt.AddMinutes(1) }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().RepairDiscreteDamageAsync(conflict));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().RepairDiscreteDamageAsync(request,
            before with { Condition = new(0.99, AirframeDamageState.Recorded) }));
        Assert.Equal(applied.Current, await Store().FindAsync(request.AirframeId));
        Assert.Equal(other, await Store().FindAsync(other.Airframe.AirframeId));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Fact]
    public async Task ConcurrentIdenticalRequestsCommitOnlyOneRepair()
    {
        var before = await CreateAsync();
        var request = Request(before);
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => Service().RepairDiscreteDamageAsync(request))));
        Assert.Single(results, r => r.WasNewlyApplied);
        Assert.All(results, r => Assert.Equal(AirframeRepairStatus.Repaired, r.Status));
        Assert.Equal(2, (await Store().FindAsync(request.AirframeId))!.Revision);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Theory]
    [InlineData("AFTER UPDATE ON airframes")]
    [InlineData("BEFORE INSERT ON airframe_maintenance_events")]
    [InlineData("AFTER INSERT ON airframe_maintenance_events")]
    public async Task PersistenceFailureRollsBackBothWritesAndRetryAppliesOnce(string point)
    {
        var before = await CreateAsync();
        var request = Request(before);
        await ExecuteAsync($"CREATE TRIGGER fail_repair {point} BEGIN SELECT RAISE(ABORT, 'injected failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Service().RepairDiscreteDamageAsync(request));
        Assert.Equal(before, await Store().FindAsync(request.AirframeId));
        Assert.Null(await Store().FindMaintenanceActionAsync(request.MaintenanceActionId));
        await ExecuteAsync("DROP TRIGGER fail_repair;");
        Assert.True((await Service().RepairDiscreteDamageAsync(request)).WasNewlyApplied);
        Assert.False((await Service().RepairDiscreteDamageAsync(request)).WasNewlyApplied);
        Assert.Equal(2, (await Store().FindAsync(request.AirframeId))!.Revision);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
    }

    [Fact]
    public async Task ServiceHistoryIsSeparateBoundedExactAndSurvivesRestart()
    {
        var a = await CreateAsync();
        var b = await CreateAsync();
        var first = await Service().RepairDiscreteDamageAsync(Request(a));
        var damagedAgain = await Store().UpdateConditionAsync(a.Airframe, new(0.3, AirframeDamageState.Grounding), first.Current!.Revision, Epoch.AddMinutes(2));
        var second = await Service().RepairDiscreteDamageAsync(Request(damagedAgain));
        await Service().RepairDiscreteDamageAsync(Request(b));
        ClearPool();
        var page1 = (await History().ReadServiceHistoryAsync(new(a.Airframe.AirframeId, 1))).Snapshot!;
        Assert.Equal(second.Current, page1.Current);
        Assert.Equal(Json(second.Event!), Json(Assert.Single(page1.History.Events)));
        Assert.NotNull(page1.History.Next);
        var page2 = (await History().ReadServiceHistoryAsync(new(a.Airframe.AirframeId, 1, page1.History.Next))).Snapshot!;
        Assert.Equal(Json(first.Event!), Json(Assert.Single(page2.History.Events)));
        Assert.Null(page2.History.Next);
        Assert.Empty((await History().ReadAsync(new(a.Airframe.AirframeId))).Snapshot!.History);
        Assert.Single((await History().ReadServiceHistoryAsync(new(b.Airframe.AirframeId))).Snapshot!.History.Events);
        Assert.Equal(second.Current, await Store().FindAsync(a.Airframe.AirframeId));
    }

    [Fact]
    public async Task ServiceHistoryNotFoundAndEmptyAreDistinct()
    {
        var missing = await History().ReadServiceHistoryAsync(new(new AirframeId(Guid.NewGuid())));
        Assert.Equal(AirframeMaintenanceReadStatus.NotFound, missing.Status);
        Assert.Null(missing.Snapshot);
        var airframe = await CreateAsync();
        var existing = await History().ReadServiceHistoryAsync(new(airframe.Airframe.AirframeId));
        Assert.Equal(AirframeMaintenanceReadStatus.Available, existing.Status);
        Assert.Empty(existing.Snapshot!.History.Events);
    }

    [Theory]
    [InlineData("action")]
    [InlineData("airframe")]
    [InlineData("kind")]
    [InlineData("time")]
    [InlineData("malformed")]
    [InlineData("missing")]
    [InlineData("schema")]
    [InlineData("wear")]
    [InlineData("revision")]
    [InlineData("semantics")]
    [InlineData("rationale")]
    public async Task CorruptOrMismatchedServiceEventFailsClosed(string fault)
    {
        var a = await CreateAsync();
        var b = await CreateAsync();
        var request = Request(a);
        var applied = await Service().RepairDiscreteDamageAsync(request);
        string sql = fault switch
        {
            "action" => "UPDATE airframe_maintenance_events SET maintenance_action_id=$other;",
            "airframe" => "UPDATE airframe_maintenance_events SET airframe_id=$other;",
            "kind" => "PRAGMA ignore_check_constraints=ON; UPDATE airframe_maintenance_events SET event_kind=2;",
            "time" => "UPDATE airframe_maintenance_events SET performed_at_utc_ticks=1;",
            "malformed" => "UPDATE airframe_maintenance_events SET payload_json='not json';",
            "missing" => "UPDATE airframe_maintenance_events SET payload_json='{}';",
            "schema" => "PRAGMA ignore_check_constraints=ON; UPDATE airframe_maintenance_events SET payload_schema_version=99;",
            "wear" => "UPDATE airframe_maintenance_events SET payload_json=json_set(payload_json,'$.after.condition.wearFraction',0);",
            "revision" => "UPDATE airframe_maintenance_events SET payload_json=json_set(payload_json,'$.after.revision',99);",
            "semantics" => "UPDATE airframe_maintenance_events SET payload_json=json_set(payload_json,'$.kind',2);",
            _ => "UPDATE airframe_maintenance_events SET payload_json=json_set(payload_json,'$.rationale','component inspected');"
        };
        await ExecuteAsync(sql, b.Airframe.AirframeId.ToString());
        var error = await Record.ExceptionAsync(() => History().ReadServiceHistoryAsync(new(fault == "airframe" ? b.Airframe.AirframeId : a.Airframe.AirframeId)));
        Assert.True(error is InvalidDataException or JsonException or ArgumentException or NotSupportedException, error?.ToString());
        var replay = await Record.ExceptionAsync(() => Service().RepairDiscreteDamageAsync(request));
        Assert.NotNull(replay);
        Assert.Equal(applied.Current, await Store().FindAsync(a.Airframe.AirframeId));
        Assert.Equal(b, await Store().FindAsync(b.Airframe.AirframeId));
    }

    [Fact]
    public async Task ServiceHistoryRejectsCurrentCanonicalModelMismatch()
    {
        var a = await CreateAsync();
        await Service().RepairDiscreteDamageAsync(Request(a));
        await ExecuteAsync("UPDATE airframes SET canonical_aircraft_id='different-model';");
        await Assert.ThrowsAsync<InvalidDataException>(() => History().ReadServiceHistoryAsync(new(a.Airframe.AirframeId)));
    }

    [Fact]
    public async Task InvalidServiceHistoryQueryFailsBeforeDatabaseAccess()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store().ReadServiceHistoryAsync(new(default)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Store().ReadServiceHistoryAsync(new(new AirframeId(Guid.NewGuid()), 0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => History().ReadServiceHistoryAsync(new(new AirframeId(Guid.NewGuid()), 1001)));
        await Assert.ThrowsAsync<ArgumentException>(() => Store().ReadServiceHistoryAsync(new(new AirframeId(Guid.NewGuid()), Before: new(Epoch, Guid.Empty))));
        await Assert.ThrowsAsync<ArgumentException>(() => Store().FindMaintenanceActionAsync(Guid.Empty));
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task Schema15MigrationPreservesEveryExistingRowIncludingConsequenceEvidence()
    {
        var a = await CreateAsync();
        var flight = ConsequenceFixture.Session(a.Airframe.AirframeId, crash: true);
        var decision = OpenCareer.Domain.Flights.FlightAirframeConsequenceCalculator.Calculate(flight);
        var applied = await Store().ApplyAsync(decision, a, Epoch.AddHours(4));
        await new SqliteFlightSessionCheckpointStore(DatabasePath).SaveAsync(flight);
        await ExecuteAsync("""
            INSERT INTO job_contracts VALUES ('repair-contract-sentinel', 1, 1, 3, 100, '{"preserve":"contract"}');
            INSERT INTO economy_ledger_transactions VALUES ('repair-ledger-sentinel', 'repair-key', 100, 'preserve', 'contract', 'repair-contract-sentinel');
            INSERT INTO economy_ledger_postings VALUES ('repair-ledger-sentinel', 0, 0, 500, 0, 'debit');
            INSERT INTO economy_ledger_postings VALUES ('repair-ledger-sentinel', 1, 1, 0, 500, 'credit');
            INSERT INTO logbook_entries VALUES ('repair-log-sentinel', 'repair-log-key', 1, 100, 100, 0, 0, 0, 'C172', 'KJFK', 'KJFK', 'repair-contract-sentinel', 'preserve', '{"preserve":"logbook"}');
            DROP TABLE airframe_maintenance_events;
            PRAGMA user_version=15;
            """);
        var beforeRows = await ExistingRowsAsync();
        Assert.Equal(15L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Null(await Store().FindMaintenanceActionAsync(Guid.NewGuid()));
        Assert.Equal(17L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(beforeRows, await ExistingRowsAsync());
        Assert.Equal(Json(applied.Application), Json((await Store().FindBySessionAsync(flight.SessionId))!));
        Assert.Equal(applied.Application.After, await Store().FindAsync(a.Airframe.AirframeId));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM sqlite_master WHERE type='index' AND name='ix_airframe_maintenance_events_airframe';"));
    }

    [Fact]
    public async Task FreshSchemaHasEmptyServiceHistoryAndProductionUsesSameStore()
    {
        Assert.Null(await Store().FindMaintenanceActionAsync(Guid.NewGuid()));
        Assert.Equal(17L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframe_maintenance_events;"));
        string registration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "UiContracts", "App.xaml.cs"));
        Assert.Contains("AddSingleton<IAirframeMaintenanceStore>(provider =>", registration);
        Assert.Contains("(SqliteAirframeStore)provider.GetRequiredService<IAirframeStore>()", registration);
        Assert.Contains("AddSingleton<AirframeMaintenanceService>()", registration);
    }

    private async Task<string> ExistingRowsAsync()
    {
        await using var connection = await OpenAsync();
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name <> 'airframe_maintenance_events' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var rows = new Dictionary<string, List<object[]>>();
        foreach (string table in tables)
        {
            rows[table] = [];
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\" ORDER BY rowid;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { var values = new object[reader.FieldCount]; reader.GetValues(values); rows[table].Add(values); }
        }
        return Json(rows);
    }
    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync(); return connection;
    }
    private async Task ExecuteAsync(string sql, string? other = null)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        if (other is not null) command.Parameters.AddWithValue("$other", other);
        await command.ExecuteNonQueryAsync();
    }
    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
    private static string Json(object value) => JsonSerializer.Serialize(value);
    private void ClearPool()
    {
        using var pool = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString());
        SqliteConnection.ClearPool(pool);
    }
    public void Dispose()
    {
        ClearPool();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
