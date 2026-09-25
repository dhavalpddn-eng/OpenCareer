using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Aircraft;
using OpenCareer.Infrastructure.Flights;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteAirframeStoreTests : IDisposable
{
    private static readonly DateTimeOffset Epoch = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero).AddTicks(1234);
    private static readonly AirframeCondition Baseline = new(0.15, AirframeDamageState.None);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
    private OpenCareerDatabaseOptions Options => new(Path.Combine(_root, "career.db"));
    private SqliteAirframeStore Store() => new(Options, NullLogger<SqliteAirframeStore>.Instance);
    private static Airframe Aircraft() => new(new AirframeId(Guid.NewGuid()), AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId, Epoch);

    [Fact]
    public async Task CreateUpdateAndRestartRetainExactIdentityConditionRevisionAndTimestamp()
    {
        Airframe airframe = Aircraft();
        IAirframeStore store = Store();
        Assert.Null(await store.FindAsync(airframe.AirframeId));
        AirframeStoreRecord created = await store.CreateAsync(airframe, Baseline, Epoch);
        Assert.Equal(1, created.Revision);
        Assert.Equal(created, await Store().FindAsync(airframe.AirframeId));

        var changed = new AirframeCondition(0.2, AirframeDamageState.Grounding);
        AirframeStoreRecord updated = await Store().UpdateConditionAsync(airframe, changed, created.Revision, Epoch.AddTicks(1));
        Assert.Equal(2, updated.Revision);
        Assert.Equal(changed, updated.Condition);
        Assert.Equal(airframe, updated.Airframe);
        Assert.Equal(Epoch.AddTicks(1), updated.SavedAt);
        Assert.Equal(updated, await Store().FindAsync(airframe.AirframeId));
    }

    [Fact]
    public async Task DuplicateCreateCannotOverwriteThePhysicalIdentityOrCondition()
    {
        Airframe airframe = Aircraft();
        AirframeStoreRecord first = await Store().CreateAsync(airframe, Baseline, Epoch);
        await Assert.ThrowsAsync<AirframeConcurrencyException>(() => Store().CreateAsync(airframe, new(0, AirframeDamageState.None), Epoch));
        var wrongModel = new Airframe(airframe.AirframeId, "different-canonical-model", Epoch);
        await Assert.ThrowsAsync<AirframeConcurrencyException>(() => Store().CreateAsync(wrongModel, Baseline, Epoch));
        Assert.Equal(first, await Store().FindAsync(airframe.AirframeId));
    }

    [Fact]
    public async Task SameModelAirframesRemainIndependentAcrossStoresAndUpdates()
    {
        Airframe a = Aircraft();
        Airframe b = Aircraft();
        Assert.Equal(a.CanonicalAircraftId, b.CanonicalAircraftId);
        await Store().CreateAsync(a, Baseline, Epoch);
        AirframeStoreRecord untouched = await Store().CreateAsync(b, Baseline, Epoch);
        var damageOnly = new AirframeCondition(Baseline.WearFraction, AirframeDamageState.Grounding);
        AirframeStoreRecord damaged = await Store().UpdateConditionAsync(a, damageOnly, 1, Epoch.AddMinutes(1));
        Assert.True((await Store().FindAsync(a.AirframeId))!.Condition.RequiresGrounding);
        Assert.Equal(untouched, await Store().FindAsync(b.AirframeId));
        var wearOnly = new AirframeCondition(0.4, AirframeDamageState.Grounding);
        AirframeStoreRecord worn = await Store().UpdateConditionAsync(a, wearOnly, damaged.Revision, Epoch.AddMinutes(2));
        Assert.Equal(damaged.Condition.Damage, worn.Condition.Damage);
        Assert.Equal(untouched, await Store().FindAsync(b.AirframeId));
    }

    [Fact]
    public async Task StaleAndReplayedWritesCannotOverwriteNewerCondition()
    {
        Airframe a = Aircraft();
        await Store().CreateAsync(a, Baseline, Epoch);
        var firstWriter = Store();
        var secondWriter = Store();
        AirframeStoreRecord retained = (await secondWriter.FindAsync(a.AirframeId))!;
        AirframeStoreRecord winner = await firstWriter.UpdateConditionAsync(a, new(0.3, AirframeDamageState.Recorded), 1, Epoch.AddHours(1));
        await Assert.ThrowsAsync<AirframeConcurrencyException>(() => secondWriter.UpdateConditionAsync(a, Baseline, retained.Revision, Epoch.AddHours(2)));
        Assert.Equal(winner, await secondWriter.FindAsync(a.AirframeId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetainedIdentityMismatchCannotChangeAnExistingAirframe(bool wrongModel)
    {
        Airframe a = Aircraft();
        AirframeStoreRecord original = await Store().CreateAsync(a, Baseline, Epoch);
        var mismatch = new Airframe(a.AirframeId, wrongModel ? "other-model" : a.CanonicalAircraftId,
            wrongModel ? a.CreatedAt : a.CreatedAt.AddTicks(1));
        await Assert.ThrowsAsync<AirframeConcurrencyException>(() => Store().UpdateConditionAsync(mismatch, new(1, AirframeDamageState.Grounding), 1, Epoch.AddHours(1)));
        Assert.Equal(original, await Store().FindAsync(a.AirframeId));
    }

    [Fact]
    public async Task InvalidOrMissingTargetAndBackdatedUpdatesFailClosed()
    {
        Airframe a = Aircraft();
        await Assert.ThrowsAsync<ArgumentException>(() => Store().FindAsync(default));
        await Assert.ThrowsAsync<AirframeConcurrencyException>(() => Store().UpdateConditionAsync(a, Baseline, 1, Epoch));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Store().CreateAsync(a, Baseline, Epoch.AddTicks(-1)));
        AirframeStoreRecord original = await Store().CreateAsync(a, Baseline, Epoch.AddSeconds(1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Store().UpdateConditionAsync(a, Baseline, 1, Epoch));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Store().UpdateConditionAsync(a, Baseline, 0, Epoch));
        Assert.Equal(original, await Store().FindAsync(a.AirframeId));
    }

    [Theory]
    [InlineData("canonical_aircraft_id", " ")]
    [InlineData("wear_fraction", "2")]
    [InlineData("wear_fraction", "unknown")]
    [InlineData("damage_state", "99")]
    [InlineData("damage_state", "4294967296")]
    [InlineData("damage_state", "unknown")]
    [InlineData("revision", "0")]
    [InlineData("created_at_utc_ticks", "-1")]
    [InlineData("saved_at_utc_ticks", "0")]
    public async Task CorruptionIsRejectedOnReadAndCannotBeSilentlyOverwritten(string column, string value)
    {
        Airframe a = Aircraft();
        await Store().CreateAsync(a, Baseline, Epoch);
        await using (SqliteConnection connection = await OpenAsync())
        {
            await using SqliteCommand command = connection.CreateCommand();
            // Column names are fixed test cases; only values are external data.
            command.CommandText = $"PRAGMA ignore_check_constraints = ON; UPDATE airframes SET {column} = $value WHERE airframe_id = $id;";
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$id", a.AirframeId.ToString());
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => Store().FindAsync(a.AirframeId));
        await Assert.ThrowsAsync<InvalidDataException>(() => Store().UpdateConditionAsync(a, Baseline, 1, Epoch));
    }

    [Fact]
    public async Task FailedUpdateRollsBackWholeConditionAndSameRevisionCanRetry()
    {
        Airframe a = Aircraft();
        AirframeStoreRecord original = await Store().CreateAsync(a, Baseline, Epoch);
        await ExecuteAsync("""
            CREATE TRIGGER fail_condition_save AFTER UPDATE ON airframes
            BEGIN SELECT RAISE(ABORT, 'injected condition save failure'); END;
            """);
        await Assert.ThrowsAsync<SqliteException>(() => Store().UpdateConditionAsync(a, new(0.5, AirframeDamageState.Grounding), 1, Epoch.AddMinutes(1)));
        Assert.Equal(original, await Store().FindAsync(a.AirframeId));
        await ExecuteAsync("DROP TRIGGER fail_condition_save;");
        AirframeStoreRecord retry = await Store().UpdateConditionAsync(a, new(0.5, AirframeDamageState.Grounding), 1, Epoch.AddMinutes(1));
        Assert.Equal(2, retry.Revision);
        Assert.Equal(retry, await Store().FindAsync(a.AirframeId));
    }

    [Fact]
    public async Task FreshMigrationCreatesAnEmptyPhysicalFleetAndRegistersProductionStore()
    {
        Assert.Null(await Store().FindAsync(Aircraft().AirframeId));
        Assert.Equal(14L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframes;"));
        string registration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "UiContracts", "App.xaml.cs"));
        Assert.Contains("AddSingleton<IAirframeStore, SqliteAirframeStore>()", registration);
    }

    [Fact]
    public async Task Schema13MigrationPreservesPlayableLoopRowsAndDoesNotInventAirframes()
    {
        var profiles = new SqlitePlayerCareerProfileStore(Options, NullLogger<SqlitePlayerCareerProfileStore>.Instance);
        var profile = PlayerCareerProfile.Start(Guid.NewGuid(), "KJFK", Epoch);
        await profiles.SaveAsync(profile, null, Epoch);
        var sessions = new SqliteFlightSessionCheckpointStore(Options.DatabasePath);
        var flight = FlightSession.Start(Epoch, contractId: Guid.NewGuid());
        await sessions.SaveAsync(flight);
        var availability = new SqliteAircraftAvailabilityStore(Options.DatabasePath);
        await availability.SetAsync(new AircraftAvailabilityState(AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId,
            AircraftAvailabilityStatus.Unavailable, flight.ContractId!.Value.ToString("D")));
        await ExecuteAsync("""
            INSERT INTO job_contracts VALUES ('contract-sentinel', 1, 1, 3, 100, '{"preserve":"contract"}');
            INSERT INTO economy_ledger_transactions VALUES ('ledger-sentinel', 'idempotency-sentinel', 100, 'preserve', 'contract', 'contract-sentinel');
            INSERT INTO economy_ledger_postings VALUES ('ledger-sentinel', 0, 0, 500, 0, 'debit');
            INSERT INTO economy_ledger_postings VALUES ('ledger-sentinel', 1, 1, 0, 500, 'credit');
            INSERT INTO logbook_entries VALUES ('log-sentinel', 'log-key', 1, 100, 100, 0, 0, 0, 'C172', 'KJFK', 'KJFK', 'contract-sentinel', 'preserve', '{"preserve":"logbook"}');
            DROP TABLE airframes;
            PRAGMA user_version = 13;
            """);
        // The previous schema has exactly these existing tables, with no physical-airframe table.
        string before = await PlayableRowsAsync();
        Assert.Equal(13L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Null(await Store().FindAsync(Aircraft().AirframeId));
        Assert.Equal(14L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(before, await PlayableRowsAsync());
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM airframes;"));
        Assert.Equal(profile.CareerId, (await profiles.LoadAsync())!.Profile.CareerId);
        Assert.Equal(flight.SessionId, (await new SqliteFlightSessionCheckpointStore(Options.DatabasePath).LoadAsync())!.SessionId);
        Assert.Equal(flight.ContractId.Value.ToString("D"), (await availability.FindAsync(AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId))!.ReservationId);
        // Migration is also safe on another store's restart after physical data exists.
        Airframe a = Aircraft();
        AirframeStoreRecord persisted = await Store().CreateAsync(a, Baseline, Epoch);
        Assert.Equal(persisted, await Store().FindAsync(a.AirframeId));
        Assert.Equal(before, await PlayableRowsAsync());
    }

    [Fact]
    public async Task FutureDatabaseSchemaIsNotDowngraded()
    {
        await Store().FindAsync(Aircraft().AirframeId);
        await ExecuteAsync("PRAGMA user_version = 999;");
        await Assert.ThrowsAsync<NotSupportedException>(() => Store().FindAsync(Aircraft().AirframeId));
        Assert.Equal(999L, await ScalarAsync("PRAGMA user_version;"));
    }

    private async Task<string> PlayableRowsAsync()
    {
        var rows = new List<object[]>();
        await using SqliteConnection connection = await OpenAsync();
        foreach (string table in new[] { "player_career_profile", "flight_session_checkpoint", "aircraft_availability", "job_contracts", "economy_ledger_transactions", "economy_ledger_postings", "logbook_entries" })
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} ORDER BY rowid;";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(values);
            }
        }
        return JsonSerializer.Serialize(rows);
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Options.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using SqliteConnection connection = await OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using SqliteConnection connection = await OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
