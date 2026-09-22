using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class PlayableLoopDatabaseMigrationTests
{
    [Fact]
    public async Task FreshDatabaseCreatesUnifiedEconomyAndCareerSchema()
    {
        string directory = CreateTempDirectory();

        try
        {
            string path = Path.Combine(directory, "opencareer.db");

            await TriggerMigrationAsync(path);

            await AssertSchemaVersionAsync(path, 7);
            await AssertTablesExistAsync(
                path,
                "player_career_profile",
                "JobContracts",
                "EconomyLedgerTransactions",
                "EconomyLedgerPostings",
                "ActivePlayBillingState",
                "OwnedAircraft",
                "AircraftLoans",
                "AircraftLoanState");
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task EconomyLegacyDatabasePreservesRowsAndAddsCareerSchema()
    {
        string directory = CreateTempDirectory();

        try
        {
            string path = Path.Combine(directory, "opencareer.db");
            const string transactionId =
                "9f113d5d-f51c-4be1-bdb0-c3933f75de59";

            await CreateEconomyLegacyDatabaseAsync(
                path,
                transactionId);

            await TriggerMigrationAsync(path);

            await AssertSchemaVersionAsync(path, 7);
            await AssertTablesExistAsync(
                path,
                "player_career_profile",
                "JobContracts",
                "EconomyLedgerTransactions",
                "EconomyLedgerPostings");

            await using SqliteConnection connection =
                await OpenReadOnlyAsync(path);

            await using SqliteCommand transaction =
                connection.CreateCommand();
            transaction.CommandText =
                """
                SELECT Description
                FROM EconomyLedgerTransactions
                WHERE TransactionId = $transaction_id;
                """;
            transaction.Parameters.AddWithValue(
                "$transaction_id",
                transactionId);

            Assert.Equal(
                "legacy-economy-sentinel",
                Convert.ToString(
                    await transaction.ExecuteScalarAsync(),
                    System.Globalization.CultureInfo.InvariantCulture));

            await using SqliteCommand posting =
                connection.CreateCommand();
            posting.CommandText =
                """
                SELECT DebitCents
                FROM EconomyLedgerPostings
                WHERE TransactionId = $transaction_id
                  AND PostingIndex = 0;
                """;
            posting.Parameters.AddWithValue(
                "$transaction_id",
                transactionId);

            Assert.Equal(
                12345L,
                Convert.ToInt64(
                    await posting.ExecuteScalarAsync(),
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task CareerV6DatabasePreservesRowsAndAddsEconomySchema()
    {
        string directory = CreateTempDirectory();

        try
        {
            string path = Path.Combine(directory, "opencareer.db");
            const string careerId =
                "6651324d-7d57-4439-b82c-e3235acc0d10";

            await CreateCareerV6DatabaseAsync(
                path,
                careerId);

            await TriggerMigrationAsync(path);

            await AssertSchemaVersionAsync(path, 7);
            await AssertTablesExistAsync(
                path,
                "player_career_profile",
                "JobContracts",
                "EconomyLedgerTransactions",
                "EconomyLedgerPostings",
                "ActivePlayBillingState",
                "OwnedAircraft",
                "AircraftLoans",
                "AircraftLoanState");

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
            Assert.Equal(careerId, reader.GetString(0));
            Assert.Equal(3L, reader.GetInt64(1));
            Assert.Equal("{\"migrationSentinel\":true}", reader.GetString(2));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static async Task TriggerMigrationAsync(
        string path)
    {
        var store =
            new SqliteConflictCampaignStore(
                new OpenCareerDatabaseOptions(path),
                NullLogger<SqliteConflictCampaignStore>.Instance);

        Assert.Null(
            await store.LoadMostRecentlySavedAsync());
    }

    private static async Task CreateEconomyLegacyDatabaseAsync(
        string path,
        string transactionId)
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

            CREATE INDEX IX_JobContracts_Status_Updated
                ON JobContracts (Status, UpdatedAtUtcTicks DESC);

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
                PRIMARY KEY (TransactionId, PostingIndex),
                FOREIGN KEY (TransactionId)
                    REFERENCES EconomyLedgerTransactions(TransactionId)
                    ON DELETE CASCADE,
                CHECK (DebitCents >= 0),
                CHECK (CreditCents >= 0),
                CHECK (
                    (DebitCents > 0 AND CreditCents = 0)
                    OR (CreditCents > 0 AND DebitCents = 0)
                )
            );

            CREATE TABLE ActivePlayBillingState (
                OwnershipId TEXT NOT NULL PRIMARY KEY,
                CycleIndex INTEGER NOT NULL CHECK (CycleIndex >= 0),
                CycleProgressTicks INTEGER NOT NULL CHECK (CycleProgressTicks >= 0),
                Version INTEGER NOT NULL CHECK (Version >= 0),
                UpdatedAtUtcTicks INTEGER NOT NULL
            );

            CREATE TABLE OwnedAircraft (
                OwnershipId TEXT NOT NULL PRIMARY KEY,
                DealerId TEXT NOT NULL,
                ListingId TEXT NOT NULL,
                AircraftId TEXT NOT NULL,
                AcquisitionMethod INTEGER NOT NULL,
                AcquisitionPriceCents INTEGER NOT NULL CHECK (AcquisitionPriceCents > 0),
                AcquiredAtUtcTicks INTEGER NOT NULL,
                StorageIcao TEXT NOT NULL,
                LoanId TEXT NULL,
                UNIQUE (DealerId, ListingId),
                UNIQUE (LoanId)
            );

            CREATE TABLE AircraftLoans (
                LoanId TEXT NOT NULL PRIMARY KEY,
                OwnershipId TEXT NOT NULL UNIQUE,
                LenderId TEXT NOT NULL,
                OriginalPrincipalCents INTEGER NOT NULL CHECK (OriginalPrincipalCents > 0),
                AnnualRateText TEXT NOT NULL,
                TermMonths INTEGER NOT NULL CHECK (TermMonths > 0),
                ScheduledPaymentCents INTEGER NOT NULL CHECK (ScheduledPaymentCents > 0),
                OriginatedAtUtcTicks INTEGER NOT NULL,
                FOREIGN KEY (OwnershipId)
                    REFERENCES OwnedAircraft(OwnershipId)
                    ON DELETE CASCADE
            );

            CREATE TABLE AircraftLoanState (
                LoanId TEXT NOT NULL PRIMARY KEY,
                OwnershipId TEXT NOT NULL UNIQUE,
                RemainingPrincipalCents INTEGER NOT NULL CHECK (RemainingPrincipalCents >= 0),
                Version INTEGER NOT NULL CHECK (Version >= 0),
                UpdatedAtUtcTicks INTEGER NOT NULL,
                FOREIGN KEY (LoanId)
                    REFERENCES AircraftLoans(LoanId)
                    ON DELETE CASCADE,
                FOREIGN KEY (OwnershipId)
                    REFERENCES OwnedAircraft(OwnershipId)
                    ON DELETE CASCADE
            );

            CREATE INDEX IX_EconomyLedgerTransactions_Occurred
                ON EconomyLedgerTransactions (OccurredAtUtcTicks DESC);

            CREATE INDEX IX_EconomyLedgerPostings_Account
                ON EconomyLedgerPostings (AccountCode, TransactionId);

            INSERT INTO EconomyLedgerTransactions (
                TransactionId,
                IdempotencyKey,
                OccurredAtUtcTicks,
                Description,
                ReferenceType,
                ReferenceId)
            VALUES (
                $transaction_id,
                'legacy-economy-idempotency',
                638938368000000000,
                'legacy-economy-sentinel',
                'MigrationTest',
                'legacy');

            INSERT INTO EconomyLedgerPostings (
                TransactionId,
                PostingIndex,
                AccountCode,
                DebitCents,
                CreditCents,
                Memo)
            VALUES (
                $transaction_id,
                0,
                0,
                12345,
                0,
                'preserve-me');
            """;
        command.Parameters.AddWithValue(
            "$transaction_id",
            transactionId);

        await command.ExecuteNonQueryAsync();

        await using SqliteCommand version =
            connection.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(
            0L,
            Convert.ToInt64(
                await version.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task CreateCareerV6DatabaseAsync(
        string path,
        string careerId)
    {
        await using SqliteConnection connection =
            await OpenReadWriteAsync(path);

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

            CREATE TABLE military_operation_consequences (
                resolution_key TEXT NOT NULL PRIMARY KEY,
                operation_id TEXT NOT NULL,
                mission_id TEXT NOT NULL,
                payload_schema_version INTEGER NOT NULL,
                completed_at_ms INTEGER NOT NULL,
                saved_at_ms INTEGER NOT NULL,
                campaign_id TEXT NOT NULL,
                sector_id TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                UNIQUE (operation_id, mission_id)
            );

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
                payload_json)
            VALUES (
                1,
                $career_id,
                3,
                1,
                1789948800000,
                '{"migrationSentinel":true}');

            PRAGMA user_version = 6;
            """;
        command.Parameters.AddWithValue(
            "$career_id",
            careerId);

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
        command.CommandText = "PRAGMA user_version;";

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
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Cleanup must not make migration tests platform-specific.
        }
    }
}
