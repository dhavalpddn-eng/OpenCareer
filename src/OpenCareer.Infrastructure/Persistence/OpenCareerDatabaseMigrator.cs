using Microsoft.Data.Sqlite;

namespace OpenCareer.Infrastructure.Persistence;

internal static class OpenCareerDatabaseMigrator
{
    public const int CurrentSchemaVersion = 7;

    public static async Task MigrateAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        int version = await GetSchemaVersionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        if (version > CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"OpenCareer database schema {version} is newer than supported schema {CurrentSchemaVersion}.");
        }

        if (version < 1)
        {
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            await CreateLogbookSchemaAsync(
                    connection,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);

            await ExecuteAsync(
                    connection,
                    transaction,
                    "PRAGMA user_version = 1;",
                    cancellationToken)
                .ConfigureAwait(false);

            transaction.Commit();
            version = 1;
        }

        if (version < 2)
        {
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            await EnsureFlightAndConflictSchemasAsync(
                    connection,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);

            await ExecuteAsync(
                    connection,
                    transaction,
                    "PRAGMA user_version = 2;",
                    cancellationToken)
                .ConfigureAwait(false);

            transaction.Commit();
            version = 2;
        }

        if (version < 3)
        {
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            // Both parallel feature branches independently used schema v2.
            // Re-create both v2 tables idempotently so either legacy v2 shape
            // is repaired before the shared database advances to v3.
            await EnsureFlightAndConflictSchemasAsync(
                    connection,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);

            await ExecuteAsync(
                    connection,
                    transaction,
                    "PRAGMA user_version = 3;",
                    cancellationToken)
                .ConfigureAwait(false);

            transaction.Commit();
            version = 3;
        }

        if (version < 4)
        {
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            await EnsureMilitaryCareerProfileSchemaAsync(
                    connection,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);

            await ExecuteAsync(
                    connection,
                    transaction,
                    "PRAGMA user_version = 4;",
                    cancellationToken)
                .ConfigureAwait(false);

            transaction.Commit();
            version = 4;
        }

        if (version < 5)
        {
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            await EnsureOperationConsequenceSchemaAsync(
                    connection,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);

            await ExecuteAsync(
                    connection,
                    transaction,
                    "PRAGMA user_version = 5;",
                    cancellationToken)
                .ConfigureAwait(false);

            transaction.Commit();
            version = 5;
        }

        if (version < 6)
        {
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            await EnsurePlayerCareerProfileSchemaAsync(
                    connection,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);

            await ExecuteAsync(
                    connection,
                    transaction,
                    "PRAGMA user_version = 6;",
                    cancellationToken)
                .ConfigureAwait(false);

            transaction.Commit();
            version = 6;
        }

        if (version < 7)
        {
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            await EnsureEconomySchemaAsync(
                    connection,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);

            await ExecuteAsync(
                    connection,
                    transaction,
                    "PRAGMA user_version = 7;",
                    cancellationToken)
                .ConfigureAwait(false);

            transaction.Commit();
            version = 7;
        }

        if (version != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"OpenCareer database migration ended at schema {version}; expected {CurrentSchemaVersion}.");
        }
    }

    private static async Task CreateLogbookSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS logbook_entries (
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
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_logbook_entries_ended_at
                    ON logbook_entries (ended_at_ms DESC, entry_id ASC);
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_logbook_entries_entry_kind
                    ON logbook_entries (entry_kind, ended_at_ms DESC);
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_logbook_entries_safety
                    ON logbook_entries (safety_outcome, ended_at_ms DESC);
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_logbook_entries_contract
                    ON logbook_entries (contract_id);
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureFlightAndConflictSchemasAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS flight_session_checkpoint (
                    slot_id INTEGER NOT NULL PRIMARY KEY,
                    session_id TEXT NOT NULL,
                    payload_schema_version INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    updated_at_utc_ticks INTEGER NOT NULL,
                    payload_json TEXT NOT NULL,
                    CHECK (slot_id = 1)
                );
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS conflict_campaigns (
                    campaign_id TEXT NOT NULL PRIMARY KEY,
                    revision INTEGER NOT NULL CHECK (revision >= 1),
                    checkpoint_schema_version INTEGER NOT NULL,
                    saved_at_ms INTEGER NOT NULL,
                    world_updated_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL
                );
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_conflict_campaigns_world_updated
                    ON conflict_campaigns (world_updated_at_ms DESC, campaign_id ASC);
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureMilitaryCareerProfileSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS military_career_profile (
                    slot_id INTEGER NOT NULL PRIMARY KEY,
                    revision INTEGER NOT NULL CHECK (revision >= 1),
                    payload_schema_version INTEGER NOT NULL,
                    saved_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL,
                    CHECK (slot_id = 1)
                );
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsurePlayerCareerProfileSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS player_career_profile (
                    slot_id INTEGER NOT NULL PRIMARY KEY,
                    career_id TEXT NOT NULL UNIQUE,
                    revision INTEGER NOT NULL CHECK (revision >= 1),
                    payload_schema_version INTEGER NOT NULL,
                    saved_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL,
                    CHECK (slot_id = 1)
                );
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureOperationConsequenceSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS military_operation_consequences (
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
                """,
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_military_operation_consequences_campaign
                    ON military_operation_consequences (
                        campaign_id,
                        completed_at_ms DESC,
                        resolution_key ASC
                    );
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }


    private static async Task EnsureEconomySchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS JobContracts (
                    ContractId TEXT NOT NULL PRIMARY KEY,
                    ContractJson TEXT NOT NULL,
                    Status INTEGER NOT NULL,
                    Version INTEGER NOT NULL CHECK (Version >= 0),
                    UpdatedAtUtcTicks INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_JobContracts_Status_Updated
                    ON JobContracts (Status, UpdatedAtUtcTicks DESC);

                CREATE TABLE IF NOT EXISTS EconomyLedgerTransactions (
                    TransactionId TEXT NOT NULL PRIMARY KEY,
                    IdempotencyKey TEXT NOT NULL UNIQUE,
                    OccurredAtUtcTicks INTEGER NOT NULL,
                    Description TEXT NOT NULL,
                    ReferenceType TEXT NOT NULL,
                    ReferenceId TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS EconomyLedgerPostings (
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

                CREATE TABLE IF NOT EXISTS ActivePlayBillingState (
                    OwnershipId TEXT NOT NULL PRIMARY KEY,
                    CycleIndex INTEGER NOT NULL CHECK (CycleIndex >= 0),
                    CycleProgressTicks INTEGER NOT NULL CHECK (CycleProgressTicks >= 0),
                    Version INTEGER NOT NULL CHECK (Version >= 0),
                    UpdatedAtUtcTicks INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS OwnedAircraft (
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

                CREATE TABLE IF NOT EXISTS AircraftLoans (
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

                CREATE TABLE IF NOT EXISTS AircraftLoanState (
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

                CREATE INDEX IF NOT EXISTS IX_EconomyLedgerTransactions_Occurred
                    ON EconomyLedgerTransactions (OccurredAtUtcTicks DESC);

                CREATE INDEX IF NOT EXISTS IX_EconomyLedgerPostings_Account
                    ON EconomyLedgerPostings (AccountCode, TransactionId);
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<int> GetSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText = "PRAGMA user_version;";

        object? result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return Convert.ToInt32(
            result,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
