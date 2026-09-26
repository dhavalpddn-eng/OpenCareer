using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class AirframeMaintenanceHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_root, "maintenance.db");
    private static DateTimeOffset Now => ConsequenceFixture.Epoch.AddHours(4);
    private SqliteAirframeStore Store() => new(new(DatabasePath), NullLogger<SqliteAirframeStore>.Instance);
    private AirframeMaintenanceHistorySource Source() { var store = Store(); return new(store, store); }
    private Task<AirframeStoreRecord> CreateAsync(double wear = 0.2, AirframeDamageState damage = AirframeDamageState.None) =>
        Store().CreateAsync(new(new AirframeId(Guid.NewGuid()), ConsequenceFixture.Model, ConsequenceFixture.Epoch.AddDays(-1)),
            new(wear, damage), ConsequenceFixture.Epoch);
    private Task<FlightAirframeApplyResult> ApplyAsync(AirframeStoreRecord before, FlightSession session,
        DateTimeOffset? appliedAt = null) => Store().ApplyAsync(FlightAirframeConsequenceCalculator.Calculate(session), before, appliedAt ?? Now);
    private async Task<AirframeMaintenanceSnapshot> ReadAsync(AirframeId id, int limit = 100, FlightAirframeHistoryCursor? before = null)
    {
        var result = await Source().ReadAsync(new(id, limit, before));
        Assert.Equal(id, result.AirframeId);
        Assert.Equal(AirframeMaintenanceReadStatus.Available, result.Status);
        return Assert.IsType<AirframeMaintenanceSnapshot>(result.Snapshot);
    }

    [Theory]
    [InlineData(0, AirframeDamageState.None, AirframeServiceability.AvailableForDispatch)]
    [InlineData(1, AirframeDamageState.None, AirframeServiceability.AvailableForDispatch)]
    [InlineData(0, AirframeDamageState.Recorded, AirframeServiceability.AvailableForDispatch)]
    [InlineData(1, AirframeDamageState.Recorded, AirframeServiceability.AvailableForDispatch)]
    [InlineData(0, AirframeDamageState.Grounding, AirframeServiceability.Grounded)]
    [InlineData(1, AirframeDamageState.Grounding, AirframeServiceability.Grounded)]
    public async Task CurrentConditionAloneDescribesServiceabilityWithoutInventingHistory(
        double wear, AirframeDamageState damage, AirframeServiceability serviceability)
    {
        var current = await CreateAsync(wear, damage);
        var snapshot = await ReadAsync(current.Airframe.AirframeId);
        Assert.Equal(current, snapshot.Current);
        Assert.Equal(current.Airframe, snapshot.Airframe);
        Assert.Equal(current.Condition, snapshot.Condition);
        Assert.Equal(current.Revision, snapshot.Revision);
        Assert.Equal(current.SavedAt, snapshot.SavedAt);
        Assert.Equal(serviceability, snapshot.Serviceability);
        Assert.Empty(snapshot.History);
        Assert.Null(snapshot.Next);
        Assert.Equal(current, await Store().FindAsync(current.Airframe.AirframeId));
        Assert.Equal(16L, await ScalarAsync("PRAGMA user_version;"));
    }

    [Fact]
    public async Task MissingAirframeReturnsExplicitNotFoundWithoutReadingHistoryOrCreatingAircraft()
    {
        var store = Store();
        var id = new AirframeId(Guid.NewGuid());
        var result = await new AirframeMaintenanceHistorySource(store, new HistoryStub()).ReadAsync(new(id));
        Assert.Equal(AirframeMaintenanceReadStatus.NotFound, result.Status);
        Assert.Equal(id, result.AirframeId);
        Assert.Null(result.Snapshot);
        Assert.Null(await store.FindAsync(id));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframes;"));
    }

    [Theory]
    [InlineData(-500, false, FlightDamageSeverity.Normal)]
    [InlineData(-1000, false, FlightDamageSeverity.ElevatedWear)]
    [InlineData(-1500, false, FlightDamageSeverity.MinorDamage)]
    [InlineData(-2000, false, FlightDamageSeverity.MajorDamage)]
    [InlineData(-500, true, FlightDamageSeverity.Severe)]
    public async Task RetainedDecisionSurvivesRestartAndAgreesWithCurrentConditionAndGroundingGate(
        double descent, bool crash, FlightDamageSeverity severity)
    {
        var before = await CreateAsync();
        var session = ConsequenceFixture.Session(before.Airframe.AirframeId,
            crash ? FlightSessionStatus.Interrupted : FlightSessionStatus.Completed, crash, descent);
        var applied = (await ApplyAsync(before, session)).Application;
        ClearPool();
        var snapshot = await ReadAsync(before.Airframe.AirframeId);
        var entry = Assert.Single(snapshot.History);
        Assert.Equal(Json(applied), Json(entry.Application));
        Assert.Equal(session.SessionId, entry.SessionId);
        Assert.Equal(session.ContractId, entry.ContractId);
        Assert.Equal(before.Airframe.AirframeId, entry.AirframeId);
        Assert.Equal(session.UpdatedAt, entry.OccurredAt);
        Assert.Equal(Now, entry.AppliedAt);
        Assert.Equal(session.Status, entry.TerminalStatus);
        Assert.Equal(severity, entry.Severity);
        Assert.Equal(applied.Consequence.Rationale, entry.Rationale);
        Assert.Equal(session.TimeLedger.AirborneTime, entry.AirborneTime);
        Assert.Equal(before.Condition, entry.ConditionBefore);
        Assert.Equal(applied.After.Condition, entry.ConditionAfter);
        Assert.Equal(1, entry.BeforeRevision);
        Assert.Equal(2, entry.AfterRevision);
        Assert.True(entry.ConditionChanged);
        Assert.Equal(applied.After, snapshot.Current);
        Assert.Equal(crash ? AirframeServiceability.Grounded : AirframeServiceability.AvailableForDispatch, snapshot.Serviceability);
        var eligibility = await new PhysicalAirframeEligibilityService(Store()).EvaluateAsync(ConsequenceFixture.Model, entry.AirframeId);
        Assert.Equal(!crash, eligibility.IsEligible);
        Assert.Equal(applied.After, await Store().FindAsync(entry.AirframeId));
    }

    [Fact]
    public async Task BounceContactsAndStrongestTelemetryAreRetainedExactly()
    {
        var current = await CreateAsync();
        var session = ConsequenceFixture.Session(current.Airframe.AirframeId, verticalSpeeds: [-950, -1900, -1400]);
        var applied = (await ApplyAsync(current, session)).Application;
        var entry = Assert.Single((await ReadAsync(current.Airframe.AirframeId)).History);
        Assert.Equal(2, entry.BounceCount);
        Assert.Equal(session.EffectiveLandingEpisodes[0].EffectiveContacts[1], entry.StrongestContact);
        Assert.Single(entry.Application.Consequence.Summary.LandingEpisodes);
        Assert.Equal(3, entry.Application.Consequence.Summary.LandingEpisodes[0].EffectiveContacts.Count);
        Assert.Equal(Json(applied), Json(entry.Application));
    }

    [Theory]
    [InlineData(FlightSessionStatus.Completed)]
    [InlineData(FlightSessionStatus.Cancelled)]
    [InlineData(FlightSessionStatus.Interrupted)]
    public async Task MissingContactAndUnassessedSeverityStayUnknown(FlightSessionStatus status)
    {
        var current = await CreateAsync();
        var applied = (await ApplyAsync(current, ConsequenceFixture.Session(current.Airframe.AirframeId, status))).Application;
        var snapshot = await ReadAsync(current.Airframe.AirframeId);
        var entry = Assert.Single(snapshot.History);
        Assert.Null(entry.StrongestContact);
        Assert.Null(entry.Severity);
        Assert.Empty(entry.Application.Consequence.Summary.LandingEpisodes);
        Assert.Equal(0, entry.BounceCount);
        Assert.Equal(status != FlightSessionStatus.Interrupted, entry.ConditionChanged);
        Assert.Equal(applied.After, snapshot.Current);
        if (status == FlightSessionStatus.Interrupted)
        {
            Assert.Equal(entry.BeforeRevision, entry.AfterRevision);
            Assert.Equal(current.SavedAt, snapshot.SavedAt);
            Assert.Equal(Now, entry.AppliedAt);
        }
    }

    [Fact]
    public async Task AppliedRevisionDoesNotImplyConditionActuallyChanged()
    {
        var current = await CreateAsync();
        var session = ConsequenceFixture.Session(current.Airframe.AirframeId, FlightSessionStatus.Cancelled)
            with { TimeLedger = FlightTimeLedger.Empty };
        await ApplyAsync(current, session);
        var entry = Assert.Single((await ReadAsync(current.Airframe.AirframeId)).History);
        Assert.True(entry.Application.Consequence.ApplyCondition);
        Assert.Equal(2, entry.AfterRevision);
        Assert.False(entry.ConditionChanged);
    }

    [Fact]
    public async Task HistoryUsesRetainedCalibrationAndDecisionRatherThanCurrentDefaults()
    {
        var current = await CreateAsync();
        var original = FlightAirframeConsequenceCalculator.Calculate(ConsequenceFixture.Session(current.Airframe.AirframeId, verticalSpeeds: [-2000]));
        Assert.Equal(FlightDamageSeverity.MajorDamage, original.Severity);
        var retainedCalibration = new FlightAirframeCalibration("test-retained-other-calibration", 10000, 20000, 30000, 0.01);
        var retained = FlightAirframeConsequenceCalculator.Calculate(original.Summary, retainedCalibration);
        var application = (await Store().ApplyAsync(retained, current, Now)).Application;
        var entry = Assert.Single((await ReadAsync(current.Airframe.AirframeId)).History);
        Assert.Equal(FlightDamageSeverity.Normal, entry.Severity);
        Assert.Equal(retainedCalibration, entry.Application.Consequence.Calibration);
        Assert.Equal(0.02, entry.Application.Consequence.RoutineStructuralWearFraction, 12);
        Assert.Equal(Json(application), Json(entry.Application));
    }

    [Fact]
    public async Task PagesAreNewestFirstWithOrdinalSessionTieBreakAndDoNotShiftWhenNewerRowsArrive()
    {
        var a = await CreateAsync();
        var b = await CreateAsync();
        var ids = new[] { Guid.Parse("10000000-0000-0000-0000-000000000000"), Guid.Parse("f0000000-0000-0000-0000-000000000000") };
        var first = (await ApplyAsync(a, ConsequenceFixture.Session(a.Airframe.AirframeId) with { SessionId = ids[0] })).Application;
        var second = (await ApplyAsync(first.After, ConsequenceFixture.Session(a.Airframe.AirframeId) with { SessionId = ids[1] })).Application;
        await ApplyAsync(b, ConsequenceFixture.Session(b.Airframe.AirframeId, crash: true), Now.AddHours(1));
        var page1 = await ReadAsync(a.Airframe.AirframeId, 1);
        Assert.Equal(ids[1], Assert.Single(page1.History).SessionId);
        Assert.NotNull(page1.Next);
        var third = (await ApplyAsync(second.After, ConsequenceFixture.Session(a.Airframe.AirframeId), Now.AddMinutes(1))).Application;
        var page2 = await ReadAsync(a.Airframe.AirframeId, 1, page1.Next);
        Assert.Equal(ids[0], Assert.Single(page2.History).SessionId);
        Assert.Null(page2.Next);
        var all = await ReadAsync(a.Airframe.AirframeId);
        Assert.Equal(new[] { third.Consequence.Summary.SessionId, ids[1], ids[0] }, all.History.Select(e => e.SessionId));
        Assert.All(all.History, e => Assert.Equal(a.Airframe.AirframeId, e.AirframeId));
        Assert.Single((await ReadAsync(b.Airframe.AirframeId)).History);
        Assert.Equal(third.After, all.Current);
    }

    [Fact]
    public async Task ReplayKeepsOneRowConflictingReplayFailsAndReadsNeverWrite()
    {
        var current = await CreateAsync();
        var session = ConsequenceFixture.Session(current.Airframe.AirframeId, verticalSpeeds: [-1500]);
        var first = await ApplyAsync(current, session);
        Assert.False((await ApplyAsync(current, session)).WasNewlyApplied);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ApplyAsync(current, session with { ContractId = Guid.NewGuid() }));
        await ExecuteAsync("""
            CREATE TRIGGER forbid_condition_write BEFORE UPDATE ON airframes BEGIN SELECT RAISE(ABORT, 'read must not write'); END;
            CREATE TRIGGER forbid_history_write BEFORE INSERT ON flight_airframe_consequences BEGIN SELECT RAISE(ABORT, 'read must not write'); END;
            """);
        Assert.Single((await ReadAsync(current.Airframe.AirframeId)).History);
        Assert.Single((await ReadAsync(current.Airframe.AirframeId)).History);
        Assert.Equal(first.Application.After, await Store().FindAsync(current.Airframe.AirframeId));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM flight_airframe_consequences;"));
    }

    [Theory]
    [InlineData("session")]
    [InlineData("airframe")]
    [InlineData("applied")]
    [InlineData("malformed")]
    [InlineData("missing-field")]
    [InlineData("schema")]
    [InlineData("revision")]
    [InlineData("decision")]
    public async Task CorruptMetadataOrPayloadFailsClosed(string fault)
    {
        var a = await CreateAsync();
        var b = await CreateAsync();
        var applied = (await ApplyAsync(a, ConsequenceFixture.Session(a.Airframe.AirframeId, verticalSpeeds: [-500]))).Application;
        string corruption = fault switch
        {
            "session" => "UPDATE flight_airframe_consequences SET session_id=$other;",
            "airframe" => "UPDATE flight_airframe_consequences SET airframe_id=$other;",
            "applied" => "UPDATE flight_airframe_consequences SET applied_at_utc_ticks=1;",
            "malformed" => "UPDATE flight_airframe_consequences SET payload_json='not json';",
            "missing-field" => "UPDATE flight_airframe_consequences SET payload_json='{}';",
            "schema" => "PRAGMA ignore_check_constraints=ON; UPDATE flight_airframe_consequences SET payload_schema_version=99;",
            "revision" => "UPDATE flight_airframe_consequences SET payload_json=json_set(payload_json,'$.after.revision',99);",
            "decision" => "UPDATE flight_airframe_consequences SET payload_json=json_set(payload_json,'$.consequence.severity',4);",
            _ => throw new ArgumentException(fault)
        };
        await ExecuteAsync(corruption, b.Airframe.AirframeId.ToString());
        var error = await Record.ExceptionAsync(() => ReadAsync(fault == "airframe" ? b.Airframe.AirframeId : a.Airframe.AirframeId));
        Assert.True(error is InvalidDataException or JsonException or ArgumentException or NotSupportedException, error?.ToString());
        Assert.Equal(applied.After, await Store().FindAsync(a.Airframe.AirframeId));
        Assert.Equal(b, await Store().FindAsync(b.Airframe.AirframeId));
    }

    [Fact]
    public async Task CurrentCanonicalModelMustMatchRetainedHistory()
    {
        var current = await CreateAsync();
        await ApplyAsync(current, ConsequenceFixture.Session(current.Airframe.AirframeId));
        await ExecuteAsync("UPDATE airframes SET canonical_aircraft_id='different-model';");
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(current.Airframe.AirframeId));
    }

    [Fact]
    public async Task SourceRejectsHistoryFromAnotherSameModelAirframeAndWrongCurrentIdentity()
    {
        var a = await CreateAsync();
        var b = await CreateAsync();
        var other = (await ApplyAsync(b, ConsequenceFixture.Session(b.Airframe.AirframeId))).Application;
        await Assert.ThrowsAsync<InvalidDataException>(() => new AirframeMaintenanceHistorySource(Store(),
            new HistoryStub(new([other], null))).ReadAsync(new(a.Airframe.AirframeId)));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AirframeMaintenanceHistorySource(
            new AirframeReadStub(a), new HistoryStub()).ReadAsync(new(b.Airframe.AirframeId)));
    }

    [Fact]
    public async Task ConcurrentConditionWriteRejectsMixedSnapshotAndFreshReadSucceeds()
    {
        var current = await CreateAsync();
        var store = Store();
        var history = new HistoryStub(new([], null), async () =>
            await store.UpdateConditionAsync(current.Airframe, new(0.5, AirframeDamageState.Grounding), current.Revision, Now));
        await Assert.ThrowsAsync<AirframeConcurrencyException>(() =>
            new AirframeMaintenanceHistorySource(store, history).ReadAsync(new(current.Airframe.AirframeId)));
        Assert.Equal(AirframeServiceability.Grounded, (await ReadAsync(current.Airframe.AirframeId)).Serviceability);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task InvalidQueryBoundsFailBeforeDatabaseAccess(int limit)
    {
        var query = new FlightAirframeHistoryQuery(new AirframeId(Guid.NewGuid()), limit);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Store().ReadHistoryAsync(query));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Source().ReadAsync(query));
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task InvalidIdentityAndCursorFailBeforeDatabaseAccess()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store().ReadHistoryAsync(new(default)));
        await Assert.ThrowsAsync<ArgumentException>(() => Source().ReadAsync(new(default)));
        await Assert.ThrowsAsync<ArgumentException>(() => Store().ReadHistoryAsync(new(new AirframeId(Guid.NewGuid()), Before: new(Now, Guid.Empty))));
        await Assert.ThrowsAsync<ArgumentException>(() => Source().ReadAsync(new(new AirframeId(Guid.NewGuid()), Before: new(default, Guid.NewGuid()))));
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public void ProductionRegistersReadSourceOverExistingAuthoritativeStores()
    {
        string composition = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "UiContracts", "App.xaml.cs"));
        Assert.Contains("AddSingleton<AirframeMaintenanceHistorySource>()", composition);
        Assert.Contains("(SqliteAirframeStore)provider.GetRequiredService<IAirframeStore>()", composition);
    }

    private async Task ExecuteAsync(string sql, string? other = null)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (other is not null) command.Parameters.AddWithValue("$other", other);
        await command.ExecuteNonQueryAsync();
    }
    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
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
    private sealed class HistoryStub(FlightAirframeHistoryPage? page = null, Func<Task>? onRead = null) : IFlightAirframeConsequenceStore
    {
        public async Task<FlightAirframeHistoryPage> ReadHistoryAsync(FlightAirframeHistoryQuery query, CancellationToken cancellationToken = default)
        {
            if (onRead is not null) await onRead();
            return page ?? throw new InvalidOperationException("Unexpected history read.");
        }
        public Task<FlightAirframeApplication?> FindBySessionAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FlightAirframeApplyResult> ApplyAsync(FlightAirframeConsequence consequence, AirframeStoreRecord expected, DateTimeOffset appliedAt, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class AirframeReadStub(AirframeStoreRecord record) : IAirframeStore
    {
        public Task<AirframeStoreRecord?> FindAsync(AirframeId id, CancellationToken ct = default) => Task.FromResult<AirframeStoreRecord?>(record);
        public Task<AirframeStoreRecord> CreateAsync(Airframe a, AirframeCondition c, DateTimeOffset t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AirframeStoreRecord> UpdateConditionAsync(Airframe a, AirframeCondition c, long r, DateTimeOffset t, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
