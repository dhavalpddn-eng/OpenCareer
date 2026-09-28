using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class PlayableLoopDatabaseMigrationTests
{
    [Fact]
    public async Task FreshDatabaseCreatesUnifiedSchemaThroughEconomyStore()
    {
        string directory = CreateTempDirectory();

        try
        {
            string path =
                Path.Combine(
                    directory,
                    "opencareer.db");

            var store =
                CreateLedgerStore(path);

            Assert.Equal(
                0m,
                await store.ReadCashBalanceAsync());

            await AssertSchemaVersionAsync(
                path,
                13);

            await AssertTablesExistAsync(
                path,
                "player_career_profile",
                "job_board_states",
                "job_contracts",
                "economy_ledger_transactions",
                "economy_ledger_postings",
                "commodity_market_snapshots",
                "installed_aircraft_observations",
                "aircraft_availability");
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task EconomyV10DatabasePreservesCurrentRowsAndAddsCareerSchema()
    {
        string directory = CreateTempDirectory();

        try
        {
            string path =
                Path.Combine(
                    directory,
                    "opencareer.db");

            await CreateEconomyV10DatabaseAsync(path);

            var store =
                CreateLedgerStore(path);

            Assert.Equal(
                123.45m,
                await store.ReadCashBalanceAsync());

            await AssertSchemaVersionAsync(
                path,
                13);

            await AssertTablesExistAsync(
                path,
                "player_career_profile",
                "conflict_campaigns",
                "military_career_profile",
                "military_operation_consequences");

            await using SqliteConnection connection =
                await OpenReadOnlyAsync(path);

            await using SqliteCommand ledger =
                connection.CreateCommand();

            ledger.CommandText =
                """
                SELECT description
                FROM economy_ledger_transactions
                WHERE transaction_id = '10000000-0000-0000-0000-000000000001';
                """;

            Assert.Equal(
                "economy-v10-sentinel",
                Convert.ToString(
                    await ledger.ExecuteScalarAsync(),
                    System.Globalization.CultureInfo.InvariantCulture));

            await using SqliteCommand contract =
                connection.CreateCommand();

            contract.CommandText =
                """
                SELECT payload_json
                FROM job_contracts
                WHERE contract_id = '20000000-0000-0000-0000-000000000001';
                """;

            Assert.Equal(
                "{\"currentEconomy\":true}",
                Convert.ToString(
                    await contract.ExecuteScalarAsync(),
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task CareerV6DatabasePreservesProfileAndAddsCurrentEconomySchema()
    {
        string directory = CreateTempDirectory();

        try
        {
            string path =
                Path.Combine(
                    directory,
                    "opencareer.db");

            await CreateCareerV6DatabaseAsync(path);

            var store =
                CreateLedgerStore(path);

            Assert.Equal(
                0m,
                await store.ReadCashBalanceAsync());

            await AssertSchemaVersionAsync(
                path,
                13);

            await AssertTablesExistAsync(
                path,
                "job_board_states",
                "job_contracts",
                "economy_ledger_transactions",
                "economy_ledger_postings",
                "commodity_market_snapshots");

            await using SqliteConnection connection =
                await OpenReadOnlyAsync(path);

            await using SqliteCommand profile =
                connection.CreateCommand();

            profile.CommandText =
                """
                SELECT career_id, revision, payload_json
                FROM player_career_profile
                WHERE slot_id = 1;
                """;

            await using SqliteDataReader reader =
                await profile.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());
            Assert.Equal(
                "30000000-0000-0000-0000-000000000001",
                reader.GetString(0));
            Assert.Equal(
                3L,
                reader.GetInt64(1));
            Assert.Equal(
                "{\"careerV6\":true}",
                reader.GetString(2));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task IntegrationV7LegacyEconomyRowsRemainVisibleToCurrentStores()
    {
        string directory = CreateTempDirectory();

        try
        {
            string path =
                Path.Combine(
                    directory,
                    "opencareer.db");

            await CreateIntegrationV7DatabaseAsync(path);

            var store =
                CreateLedgerStore(path);

            Assert.Equal(
                50m,
                await store.ReadCashBalanceAsync());

            await AssertSchemaVersionAsync(
                path,
                13);

            await using SqliteConnection connection =
                await OpenReadOnlyAsync(path);

            await using SqliteCommand ledger =
                connection.CreateCommand();

            ledger.CommandText =
                """
                SELECT description
                FROM economy_ledger_transactions
                WHERE transaction_id = '40000000-0000-0000-0000-000000000001';
                """;

            Assert.Equal(
                "integration-v7-sentinel",
                Convert.ToString(
                    await ledger.ExecuteScalarAsync(),
                    System.Globalization.CultureInfo.InvariantCulture));

            await using SqliteCommand contract =
                connection.CreateCommand();

            contract.CommandText =
                """
                SELECT
                    payload_schema_version,
                    status,
                    version,
                    updated_at_ms,
                    payload_json
                FROM job_contracts
                WHERE contract_id = '50000000-0000-0000-0000-000000000001';
                """;

            await using SqliteDataReader reader =
                await contract.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.Equal(2L, reader.GetInt64(2));
            Assert.Equal(12345L, reader.GetInt64(3));
            Assert.Equal(
                "{\"legacyContract\":true}",
                reader.GetString(4));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task LegacyFleetAvailabilityPreservesStateAndGainsReservationColumn()
    {
        string directory = CreateTempDirectory();

        try
        {
            string path =
                Path.Combine(
                    directory,
                    "opencareer.db");

            await using (SqliteConnection connection =
                await OpenReadWriteAsync(path))
            {
                await using SqliteCommand command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    CREATE TABLE aircraft_availability (
                        canonical_aircraft_id TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                        status INTEGER NOT NULL CHECK (status IN (0, 1))
                    );

                    INSERT INTO aircraft_availability (
                        canonical_aircraft_id,
                        status
                    )
                    VALUES (
                        'msfs-title:legacy-fixture',
                        1
                    );

                    PRAGMA user_version = 1;
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var store =
                new OpenCareer.Infrastructure.Aircraft.SqliteAircraftAvailabilityStore(
                    path);

            OpenCareer.Domain.Aircraft.AircraftAvailabilityState? state =
                await store.FindAsync(
                    "MSFS-TITLE:LEGACY-FIXTURE");

            Assert.NotNull(state);
            Assert.Equal(
                OpenCareer.Domain.Aircraft.AircraftAvailabilityStatus.Unavailable,
                state.Status);
            Assert.Null(state.ReservationId);

            await AssertSchemaVersionAsync(
                path,
                13);

            await using SqliteConnection verification =
                await OpenReadOnlyAsync(path);

            await using SqliteCommand column =
                verification.CreateCommand();

            column.CommandText =
                """
                SELECT COUNT(*)
                FROM pragma_table_info('aircraft_availability')
                WHERE name = 'reservation_id';
                """;

            Assert.Equal(
                1L,
                Convert.ToInt64(
                    await column.ExecuteScalarAsync(),
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static SqliteEconomyLedgerStore CreateLedgerStore(
        string path) =>
        new(
            new OpenCareerDatabaseOptions(path),
            NullLogger<SqliteEconomyLedgerStore>.Instance);

    private static async Task CreateEconomyV10DatabaseAsync(
        string path)
    {
        await using SqliteConnection connection =
            await OpenReadWriteAsync(path);

        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE job_contracts (
                contract_id TEXT NOT NULL PRIMARY KEY,
                payload_schema_version INTEGER NOT NULL,
                status INTEGER NOT NULL,
                version INTEGER NOT NULL CHECK (version >= 0),
                updated_at_ms INTEGER NOT NULL,
                payload_json TEXT NOT NULL
            );

            CREATE TABLE economy_ledger_transactions (
                transaction_id TEXT NOT NULL PRIMARY KEY,
                idempotency_key TEXT NOT NULL UNIQUE,
                occurred_at_utc_ticks INTEGER NOT NULL,
                description TEXT NOT NULL,
                reference_type TEXT NOT NULL,
                reference_id TEXT NOT NULL
            );

            CREATE TABLE economy_ledger_postings (
                transaction_id TEXT NOT NULL,
                posting_index INTEGER NOT NULL,
                account_code INTEGER NOT NULL,
                debit_cents INTEGER NOT NULL,
                credit_cents INTEGER NOT NULL,
                memo TEXT NOT NULL,
                PRIMARY KEY (transaction_id, posting_index)
            );

            INSERT INTO job_contracts (
                contract_id,
                payload_schema_version,
                status,
                version,
                updated_at_ms,
                payload_json
            )
            VALUES (
                '20000000-0000-0000-0000-000000000001',
                1,
                1,
                3,
                1789952400000,
                '{"currentEconomy":true}'
            );

            INSERT INTO economy_ledger_transactions (
                transaction_id,
                idempotency_key,
                occurred_at_utc_ticks,
                description,
                reference_type,
                reference_id
            )
            VALUES (
                '10000000-0000-0000-0000-000000000001',
                'economy-v10-idempotency',
                638941572000000000,
                'economy-v10-sentinel',
                'MigrationTest',
                'economy-v10'
            );

            INSERT INTO economy_ledger_postings (
                transaction_id,
                posting_index,
                account_code,
                debit_cents,
                credit_cents,
                memo
            )
            VALUES
                (
                    '10000000-0000-0000-0000-000000000001',
                    0,
                    0,
                    12345,
                    0,
                    'cash'
                ),
                (
                    '10000000-0000-0000-0000-000000000001',
                    1,
                    1,
                    0,
                    12345,
                    'revenue'
                );

            PRAGMA user_version = 10;
            """;

        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateCareerV6DatabaseAsync(
        string path)
    {
        await using SqliteConnection connection =
            await OpenReadWriteAsync(path);

        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE player_career_profile (
                slot_id INTEGER NOT NULL PRIMARY KEY,
                career_id TEXT NOT NULL UNIQUE,
                revision INTEGER NOT NULL CHECK (revision >= 1),
                payload_schema_version INTEGER NOT NULL,
                saved_at_ms INTEGER NOT NULL,
                payload_json TEXT NOT NULL,
                CHECK (slot_id = 1)
            );

            INSERT INTO player_career_profile (
                slot_id,
                career_id,
                revision,
                payload_schema_version,
                saved_at_ms,
                payload_json
            )
            VALUES (
                1,
                '30000000-0000-0000-0000-000000000001',
                3,
                1,
                1789952400000,
                '{"careerV6":true}'
            );

            PRAGMA user_version = 6;
            """;

        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateIntegrationV7DatabaseAsync(
        string path)
    {
        await using SqliteConnection connection =
            await OpenReadWriteAsync(path);

        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE JobContracts (
                ContractId TEXT NOT NULL PRIMARY KEY,
                ContractJson TEXT NOT NULL,
                Status INTEGER NOT NULL,
                Version INTEGER NOT NULL CHECK (Version >= 0),
                UpdatedAtUtcTicks INTEGER NOT NULL
            );

            CREATE TABLE EconomyLedgerTransactions (
                TransactionId TEXT NOT NULL PRIMARY KEY,
                IdempotencyKey TEXT NOT NULL UNIQUE,
                OccurredAtUtcTicks INTEGER NOT NULL,
                Description TEXT NOT NULL,
                ReferenceType TEXT NOT NULL,
                ReferenceId TEXT NOT NULL
            );

            CREATE TABLE EconomyLedgerPostings (
                TransactionId TEXT NOT NULL,
                PostingIndex INTEGER NOT NULL,
                AccountCode INTEGER NOT NULL,
                DebitCents INTEGER NOT NULL,
                CreditCents INTEGER NOT NULL,
                Memo TEXT NOT NULL,
                PRIMARY KEY (TransactionId, PostingIndex)
            );

            INSERT INTO JobContracts (
                ContractId,
                ContractJson,
                Status,
                Version,
                UpdatedAtUtcTicks
            )
            VALUES (
                '50000000-0000-0000-0000-000000000001',
                '{"legacyContract":true}',
                1,
                2,
                621355968123450000
            );

            INSERT INTO EconomyLedgerTransactions (
                TransactionId,
                IdempotencyKey,
                OccurredAtUtcTicks,
                Description,
                ReferenceType,
                ReferenceId
            )
            VALUES (
                '40000000-0000-0000-0000-000000000001',
                'integration-v7-idempotency',
                638941572000000000,
                'integration-v7-sentinel',
                'MigrationTest',
                'integration-v7'
            );

            INSERT INTO EconomyLedgerPostings (
                TransactionId,
                PostingIndex,
                AccountCode,
                DebitCents,
                CreditCents,
                Memo
            )
            VALUES
                (
                    '40000000-0000-0000-0000-000000000001',
                    0,
                    0,
                    5000,
                    0,
                    'cash'
                ),
                (
                    '40000000-0000-0000-0000-000000000001',
                    1,
                    1,
                    0,
                    5000,
                    'revenue'
                );

            PRAGMA user_version = 7;
            """;

        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertSchemaVersionAsync(
        string path,
        long expected)
    {
        await using SqliteConnection connection =
            await OpenReadOnlyAsync(path);

        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            "PRAGMA user_version;";

        Assert.Equal(
            expected,
            Convert.ToInt64(
                await command.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task AssertTablesExistAsync(
        string path,
        params string[] tableNames)
    {
        await using SqliteConnection connection =
            await OpenReadOnlyAsync(path);

        foreach (string tableName in tableNames)
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

            Assert.Equal(
                1L,
                Convert.ToInt64(
                    await command.ExecuteScalarAsync(),
                    System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static async Task<SqliteConnection> OpenReadWriteAsync(
        string path)
    {
        var connection =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false
                }.ToString());

        await connection.OpenAsync();
        return connection;
    }

    private static async Task<SqliteConnection> OpenReadOnlyAsync(
        string path)
    {
        var connection =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString());

        await connection.OpenAsync();
        return connection;
    }

    private static string CreateTempDirectory()
    {
        string directory =
            Path.Combine(
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
            // Cleanup must not make migration tests platform-specific.
        }
    }
}
