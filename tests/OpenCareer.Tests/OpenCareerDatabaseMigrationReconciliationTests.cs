using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Domain.Military;
using OpenCareer.Infrastructure.Flights;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class OpenCareerDatabaseMigrationReconciliationTests
{
    [Fact]
    public async Task FlightOnlyLegacyV2AddsMissingSchemasAndAdvancesToV14()
    {
        string directory = CreateTempDirectory();

        try
        {
            string path = Path.Combine(directory, "opencareer.db");

            await CreateLegacyV2Async(
                path,
                """
                CREATE TABLE flight_session_checkpoint (
                    slot_id INTEGER NOT NULL PRIMARY KEY,
                    session_id TEXT NOT NULL,
                    payload_schema_version INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    updated_at_utc_ticks INTEGER NOT NULL,
                    payload_json TEXT NOT NULL,
                    CHECK (slot_id = 1)
                );
                """);

            var conflictStore = new SqliteConflictCampaignStore(
                new OpenCareerDatabaseOptions(path),
                NullLogger<SqliteConflictCampaignStore>.Instance);

            Assert.Null(
                await conflictStore.LoadMostRecentlySavedAsync());

            await AssertUnifiedSchemaAsync(path);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task ConflictOnlyLegacyV2AddsMissingSchemasAndAdvancesToV14()
    {
        string directory = CreateTempDirectory();

        try
        {
            string path = Path.Combine(directory, "opencareer.db");

            await CreateLegacyV2Async(
                path,
                """
                CREATE TABLE conflict_campaigns (
                    campaign_id TEXT NOT NULL PRIMARY KEY,
                    revision INTEGER NOT NULL CHECK (revision >= 1),
                    checkpoint_schema_version INTEGER NOT NULL,
                    saved_at_ms INTEGER NOT NULL,
                    world_updated_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL
                );
                """);

            var flightStore =
                new SqliteFlightSessionCheckpointStore(path);

            Assert.Null(await flightStore.LoadAsync());

            await AssertUnifiedSchemaAsync(path);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task UnifiedLegacyV3AddsMilitaryPersistenceSchemasAndAdvancesToV14()
    {
        string directory = CreateTempDirectory();

        try
        {
            string path = Path.Combine(directory, "opencareer.db");

            await using (var connection =
                new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource = path,
                        Mode = SqliteOpenMode.ReadWriteCreate,
                        Pooling = false
                    }.ToString()))
            {
                await connection.OpenAsync();

                await using SqliteCommand command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    CREATE TABLE logbook_entries (
                        entry_id TEXT NOT NULL PRIMARY KEY,
                        idempotency_key TEXT NOT NULL UNIQUE,
                        payload_schema_version INTEGER NOT NULL,
                        committed_at_ms INTEGER NOT NULL,
                        ended_at_ms INTEGER NOT NULL,
                        entry_kind INTEGER NOT NULL,
                        safety_outcome INTEGER NOT NULL,
                        mission_outcome INTEGER NOT NULL,
                        aircraft_name TEXT NOT NULL,
                        departure TEXT NULL,
                        arrival TEXT NULL,
                        contract_id TEXT NULL,
                        search_text TEXT NOT NULL,
                        payload_json TEXT NOT NULL
                    );
                    CREATE TABLE flight_session_checkpoint (
                        slot_id INTEGER NOT NULL PRIMARY KEY,
                        session_id TEXT NOT NULL,
                        payload_schema_version INTEGER NOT NULL,
                        status INTEGER NOT NULL,
                        updated_at_utc_ticks INTEGER NOT NULL,
                        payload_json TEXT NOT NULL,
                        CHECK (slot_id = 1)
                    );
                    CREATE TABLE conflict_campaigns (
                        campaign_id TEXT NOT NULL PRIMARY KEY,
                        revision INTEGER NOT NULL CHECK (revision >= 1),
                        checkpoint_schema_version INTEGER NOT NULL,
                        saved_at_ms INTEGER NOT NULL,
                        world_updated_at_ms INTEGER NOT NULL,
                        payload_json TEXT NOT NULL
                    );
                    PRAGMA user_version = 3;
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var profileStore =
                new SqliteMilitaryCareerProfileStore(
                    new OpenCareerDatabaseOptions(path),
                    NullLogger<SqliteMilitaryCareerProfileStore>.Instance);

            Assert.Null(await profileStore.LoadAsync());

            await AssertUnifiedSchemaAsync(path);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task UnifiedLegacyV4AddsOperationConsequencesAndAdvancesToV14()
    {
        string directory = CreateTempDirectory();

        try
        {
            string path = Path.Combine(directory, "opencareer.db");

            await using (var connection =
                new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource = path,
                        Mode = SqliteOpenMode.ReadWriteCreate,
                        Pooling = false
                    }.ToString()))
            {
                await connection.OpenAsync();

                await using SqliteCommand command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    CREATE TABLE logbook_entries (
                        entry_id TEXT NOT NULL PRIMARY KEY,
                        idempotency_key TEXT NOT NULL UNIQUE,
                        payload_schema_version INTEGER NOT NULL,
                        committed_at_ms INTEGER NOT NULL,
                        ended_at_ms INTEGER NOT NULL,
                        entry_kind INTEGER NOT NULL,
                        safety_outcome INTEGER NOT NULL,
                        mission_outcome INTEGER NOT NULL,
                        aircraft_name TEXT NOT NULL,
                        departure TEXT NULL,
                        arrival TEXT NULL,
                        contract_id TEXT NULL,
                        search_text TEXT NOT NULL,
                        payload_json TEXT NOT NULL
                    );
                    CREATE TABLE flight_session_checkpoint (
                        slot_id INTEGER NOT NULL PRIMARY KEY,
                        session_id TEXT NOT NULL,
                        payload_schema_version INTEGER NOT NULL,
                        status INTEGER NOT NULL,
                        updated_at_utc_ticks INTEGER NOT NULL,
                        payload_json TEXT NOT NULL,
                        CHECK (slot_id = 1)
                    );
                    CREATE TABLE conflict_campaigns (
                        campaign_id TEXT NOT NULL PRIMARY KEY,
                        revision INTEGER NOT NULL CHECK (revision >= 1),
                        checkpoint_schema_version INTEGER NOT NULL,
                        saved_at_ms INTEGER NOT NULL,
                        world_updated_at_ms INTEGER NOT NULL,
                        payload_json TEXT NOT NULL
                    );
                    CREATE TABLE military_career_profile (
                        slot_id INTEGER NOT NULL PRIMARY KEY,
                        revision INTEGER NOT NULL CHECK (revision >= 1),
                        payload_schema_version INTEGER NOT NULL,
                        saved_at_ms INTEGER NOT NULL,
                        payload_json TEXT NOT NULL,
                        CHECK (slot_id = 1)
                    );
                    PRAGMA user_version = 4;
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var consequenceStore =
                new SqliteOperationConsequenceStore(
                    new OpenCareerDatabaseOptions(path),
                    NullLogger<SqliteOperationConsequenceStore>.Instance);

            OperationResolutionKey key =
                OperationResolutionKey.Create(
                    "migration-test",
                    Guid.Parse("114eb892-cdb9-4db2-9aed-72a5d8ebf241"));

            Assert.Null(
                await consequenceStore.LoadAsync(key));

            await AssertUnifiedSchemaAsync(path);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static async Task CreateLegacyV2Async(
        string path,
        string featureTableSql)
    {
        await using var connection =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false
                }.ToString());

        await connection.OpenAsync();

        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            $"""
            CREATE TABLE logbook_entries (
                entry_id TEXT NOT NULL PRIMARY KEY,
                idempotency_key TEXT NOT NULL UNIQUE,
                payload_schema_version INTEGER NOT NULL,
                committed_at_ms INTEGER NOT NULL,
                ended_at_ms INTEGER NOT NULL,
                entry_kind INTEGER NOT NULL,
                safety_outcome INTEGER NOT NULL,
                mission_outcome INTEGER NOT NULL,
                aircraft_name TEXT NOT NULL,
                departure TEXT NULL,
                arrival TEXT NULL,
                contract_id TEXT NULL,
                search_text TEXT NOT NULL,
                payload_json TEXT NOT NULL
            );
            {featureTableSql}
            PRAGMA user_version = 2;
            """;

        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertUnifiedSchemaAsync(
        string path)
    {
        await using var connection =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString());

        await connection.OpenAsync();

        Assert.True(
            await TableExistsAsync(
                connection,
                "flight_session_checkpoint"));

        Assert.True(
            await TableExistsAsync(
                connection,
                "conflict_campaigns"));

        Assert.True(
            await TableExistsAsync(
                connection,
                "military_career_profile"));

        Assert.True(
            await TableExistsAsync(
                connection,
                "military_operation_consequences"));

        Assert.True(
            await TableExistsAsync(
                connection,
                "player_career_profile"));

        foreach (string tableName in new[]
        {
            "market_states",
            "economic_cycle_states",
            "world_event_states",
            "world_simulation_checkpoints",
            "job_board_states",
            "job_contracts",
            "economy_ledger_transactions",
            "economy_ledger_postings",
            "commodity_market_snapshots",
            "installed_aircraft_observations",
            "aircraft_availability"
        })
        {
            Assert.True(
                await TableExistsAsync(
                    connection,
                    tableName),
                $"Expected unified schema table '{tableName}'.");
        }

        await using SqliteCommand version =
            connection.CreateCommand();

        version.CommandText = "PRAGMA user_version;";

        Assert.Equal(
            14L,
            Convert.ToInt64(
                await version.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName)
    {
        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $name;
            """;

        command.Parameters.AddWithValue(
            "$name",
            tableName);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDirectory(
        string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(
                    directory,
                    recursive: true);
            }
        }
        catch
        {
            // Cleanup must not make a passing migration test platform-specific.
        }
    }
}
