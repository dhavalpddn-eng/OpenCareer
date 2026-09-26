using Microsoft.Data.Sqlite;

namespace OpenCareer.Infrastructure.Persistence;

internal static class OpenCareerDatabaseMigrator
{
    // Career and Economy branches independently reused schema versions 1-10.
    // Version 11 was the first shared convergence point; pre-v11 user_version
    // alone cannot be used to infer which subsystem tables already exist.
    public const int CurrentSchemaVersion = 15;

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

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        await EnsureUnifiedSchemaAsync(
                connection,
                transaction,
                cancellationToken)
            .ConfigureAwait(false);

        await MigrateLegacyIntegrationEconomyAsync(
                connection,
                transaction,
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureAircraftAvailabilityReservationColumnAsync(
                connection,
                transaction,
                cancellationToken)
            .ConfigureAwait(false);

        await EnsurePlayerProfileSavePrecisionAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                connection,
                transaction,
                $"PRAGMA user_version = {CurrentSchemaVersion};",
                cancellationToken)
            .ConfigureAwait(false);

        transaction.Commit();
    }

    private static async Task EnsureUnifiedSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS airframes (
                    airframe_id TEXT NOT NULL PRIMARY KEY,
                    canonical_aircraft_id TEXT NOT NULL CHECK (length(trim(canonical_aircraft_id)) > 0),
                    created_at_utc_ticks INTEGER NOT NULL,
                    wear_fraction REAL NOT NULL CHECK (wear_fraction >= 0 AND wear_fraction <= 1),
                    damage_state INTEGER NOT NULL CHECK (damage_state IN (0, 1, 2)),
                    revision INTEGER NOT NULL CHECK (revision >= 1),
                    saved_at_utc_ticks INTEGER NOT NULL CHECK (saved_at_utc_ticks >= created_at_utc_ticks)
                );

                CREATE TABLE IF NOT EXISTS flight_airframe_consequences (
                    session_id TEXT NOT NULL PRIMARY KEY,
                    airframe_id TEXT NOT NULL REFERENCES airframes(airframe_id),
                    payload_schema_version INTEGER NOT NULL CHECK (payload_schema_version = 1),
                    applied_at_utc_ticks INTEGER NOT NULL,
                    payload_json TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_flight_airframe_consequences_airframe
                    ON flight_airframe_consequences (airframe_id, applied_at_utc_ticks, session_id);

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

                CREATE INDEX IF NOT EXISTS ix_logbook_entries_ended_at
                    ON logbook_entries (ended_at_ms DESC, entry_id ASC);

                CREATE INDEX IF NOT EXISTS ix_logbook_entries_entry_kind
                    ON logbook_entries (entry_kind, ended_at_ms DESC);

                CREATE INDEX IF NOT EXISTS ix_logbook_entries_safety
                    ON logbook_entries (safety_outcome, ended_at_ms DESC);

                CREATE INDEX IF NOT EXISTS ix_logbook_entries_contract
                    ON logbook_entries (contract_id);

                CREATE TABLE IF NOT EXISTS flight_session_checkpoint (
                    slot_id INTEGER NOT NULL PRIMARY KEY,
                    session_id TEXT NOT NULL,
                    payload_schema_version INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    updated_at_utc_ticks INTEGER NOT NULL,
                    payload_json TEXT NOT NULL,
                    CHECK (slot_id = 1)
                );

                CREATE TABLE IF NOT EXISTS conflict_campaigns (
                    campaign_id TEXT NOT NULL PRIMARY KEY,
                    revision INTEGER NOT NULL CHECK (revision >= 1),
                    checkpoint_schema_version INTEGER NOT NULL,
                    saved_at_ms INTEGER NOT NULL,
                    world_updated_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_conflict_campaigns_world_updated
                    ON conflict_campaigns (
                        world_updated_at_ms DESC,
                        campaign_id ASC
                    );

                CREATE TABLE IF NOT EXISTS military_career_profile (
                    slot_id INTEGER NOT NULL PRIMARY KEY,
                    revision INTEGER NOT NULL CHECK (revision >= 1),
                    payload_schema_version INTEGER NOT NULL,
                    saved_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL,
                    CHECK (slot_id = 1)
                );

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

                CREATE INDEX IF NOT EXISTS ix_military_operation_consequences_campaign
                    ON military_operation_consequences (
                        campaign_id,
                        completed_at_ms DESC,
                        resolution_key ASC
                    );

                CREATE TABLE IF NOT EXISTS player_career_profile (
                    slot_id INTEGER NOT NULL PRIMARY KEY,
                    career_id TEXT NOT NULL UNIQUE,
                    revision INTEGER NOT NULL CHECK (revision >= 1),
                    payload_schema_version INTEGER NOT NULL,
                    saved_at_ms INTEGER NOT NULL,
                    saved_at_utc_ticks INTEGER NULL,
                    payload_json TEXT NOT NULL,
                    CHECK (slot_id = 1)
                );

                CREATE TABLE IF NOT EXISTS market_states (
                    market_id TEXT NOT NULL PRIMARY KEY,
                    payload_schema_version INTEGER NOT NULL,
                    segment INTEGER NOT NULL,
                    updated_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_market_states_segment
                    ON market_states (segment, market_id);

                CREATE TABLE IF NOT EXISTS economic_cycle_states (
                    region_id TEXT NOT NULL PRIMARY KEY,
                    payload_schema_version INTEGER NOT NULL,
                    updated_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS world_event_states (
                    snapshot_id TEXT NOT NULL PRIMARY KEY,
                    payload_schema_version INTEGER NOT NULL,
                    updated_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS world_simulation_checkpoints (
                    market_id TEXT NOT NULL PRIMARY KEY,
                    payload_schema_version INTEGER NOT NULL,
                    requested_through_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS job_board_states (
                    airport_icao TEXT NOT NULL PRIMARY KEY,
                    payload_schema_version INTEGER NOT NULL,
                    updated_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS job_contracts (
                    contract_id TEXT NOT NULL PRIMARY KEY,
                    payload_schema_version INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    version INTEGER NOT NULL CHECK (version >= 0),
                    updated_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_job_contracts_status_updated
                    ON job_contracts (
                        status,
                        updated_at_ms DESC,
                        contract_id ASC
                    );

                CREATE TABLE IF NOT EXISTS economy_ledger_transactions (
                    transaction_id TEXT NOT NULL PRIMARY KEY,
                    idempotency_key TEXT NOT NULL UNIQUE,
                    occurred_at_utc_ticks INTEGER NOT NULL,
                    description TEXT NOT NULL,
                    reference_type TEXT NOT NULL,
                    reference_id TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS economy_ledger_postings (
                    transaction_id TEXT NOT NULL,
                    posting_index INTEGER NOT NULL,
                    account_code INTEGER NOT NULL,
                    debit_cents INTEGER NOT NULL,
                    credit_cents INTEGER NOT NULL,
                    memo TEXT NOT NULL,
                    PRIMARY KEY (transaction_id, posting_index),
                    FOREIGN KEY (transaction_id)
                        REFERENCES economy_ledger_transactions(transaction_id)
                        ON DELETE CASCADE,
                    CHECK (debit_cents >= 0),
                    CHECK (credit_cents >= 0),
                    CHECK (
                        (debit_cents > 0 AND credit_cents = 0)
                        OR (credit_cents > 0 AND debit_cents = 0)
                    )
                );

                CREATE INDEX IF NOT EXISTS ix_economy_ledger_transactions_occurred
                    ON economy_ledger_transactions (
                        occurred_at_utc_ticks DESC,
                        transaction_id ASC
                    );

                CREATE INDEX IF NOT EXISTS ix_economy_ledger_postings_account
                    ON economy_ledger_postings (
                        account_code,
                        transaction_id
                    );

                CREATE TABLE IF NOT EXISTS commodity_market_snapshots (
                    scope INTEGER NOT NULL,
                    location_id TEXT NOT NULL,
                    payload_schema_version INTEGER NOT NULL,
                    captured_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL,
                    PRIMARY KEY (scope, location_id)
                );

                CREATE INDEX IF NOT EXISTS ix_commodity_market_snapshots_captured
                    ON commodity_market_snapshots (
                        captured_at_ms DESC,
                        scope ASC,
                        location_id ASC
                    );

                CREATE TABLE IF NOT EXISTS installed_aircraft_observations (
                    canonical_aircraft_id TEXT NOT NULL,
                    provider_id TEXT NOT NULL,
                    provider_record_id TEXT NOT NULL,
                    payload_schema_version INTEGER NOT NULL,
                    payload_json TEXT NOT NULL,
                    PRIMARY KEY (provider_id, provider_record_id)
                );

                CREATE INDEX IF NOT EXISTS ix_installed_aircraft_observations_canonical
                    ON installed_aircraft_observations (
                        canonical_aircraft_id COLLATE NOCASE,
                        provider_id,
                        provider_record_id
                    );

                CREATE TABLE IF NOT EXISTS aircraft_availability (
                    canonical_aircraft_id TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                    status INTEGER NOT NULL CHECK (status IN (0, 1)),
                    reservation_id TEXT NULL
                );
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsurePlayerProfileSavePrecisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "PRAGMA table_info(player_career_profile);";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(1), "saved_at_utc_ticks", StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        // Legacy milliseconds cannot reconstruct lost ticks. Preserve them without inventing precision;
        // new profile commits persist exact UTC ticks in the same transaction as revision and payload.
        await ExecuteAsync(connection, transaction,
            "ALTER TABLE player_career_profile ADD COLUMN saved_at_utc_ticks INTEGER NULL;",
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureAircraftAvailabilityReservationColumnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        bool hasReservationId = false;

        await using (SqliteCommand command =
            connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                "PRAGMA table_info(aircraft_availability);";

            await using SqliteDataReader reader =
                await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);

            while (await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (string.Equals(
                        reader.GetString(1),
                        "reservation_id",
                        StringComparison.OrdinalIgnoreCase))
                {
                    hasReservationId = true;
                    break;
                }
            }
        }

        if (hasReservationId)
            return;

        await ExecuteAsync(
                connection,
                transaction,
                "ALTER TABLE aircraft_availability ADD COLUMN reservation_id TEXT NULL;",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task MigrateLegacyIntegrationEconomyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(
                connection,
                transaction,
                "JobContracts",
                cancellationToken)
            .ConfigureAwait(false))
        {
            await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO job_contracts (
                        contract_id,
                        payload_schema_version,
                        status,
                        version,
                        updated_at_ms,
                        payload_json
                    )
                    SELECT
                        legacy.ContractId,
                        1,
                        legacy.Status,
                        legacy.Version,
                        (legacy.UpdatedAtUtcTicks - 621355968000000000) / 10000,
                        legacy.ContractJson
                    FROM JobContracts AS legacy
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM job_contracts AS current
                        WHERE current.contract_id = legacy.ContractId
                    );
                    """,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        bool hasLegacyLedgerTransactions =
            await TableExistsAsync(
                    connection,
                    transaction,
                    "EconomyLedgerTransactions",
                    cancellationToken)
                .ConfigureAwait(false);

        bool hasLegacyLedgerPostings =
            await TableExistsAsync(
                    connection,
                    transaction,
                    "EconomyLedgerPostings",
                    cancellationToken)
                .ConfigureAwait(false);

        if (!hasLegacyLedgerTransactions
            || !hasLegacyLedgerPostings)
        {
            return;
        }

        await ExecuteAsync(
                connection,
                transaction,
                """
                DROP TABLE IF EXISTS temp.legacy_economy_transactions_to_migrate;

                CREATE TEMP TABLE legacy_economy_transactions_to_migrate (
                    transaction_id TEXT NOT NULL PRIMARY KEY
                );

                INSERT INTO legacy_economy_transactions_to_migrate (
                    transaction_id
                )
                SELECT legacy.TransactionId
                FROM EconomyLedgerTransactions AS legacy
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM economy_ledger_transactions AS current
                    WHERE current.transaction_id = legacy.TransactionId
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM economy_ledger_transactions AS current
                    WHERE current.idempotency_key = legacy.IdempotencyKey
                );

                INSERT INTO economy_ledger_transactions (
                    transaction_id,
                    idempotency_key,
                    occurred_at_utc_ticks,
                    description,
                    reference_type,
                    reference_id
                )
                SELECT
                    legacy.TransactionId,
                    legacy.IdempotencyKey,
                    legacy.OccurredAtUtcTicks,
                    legacy.Description,
                    legacy.ReferenceType,
                    legacy.ReferenceId
                FROM EconomyLedgerTransactions AS legacy
                INNER JOIN legacy_economy_transactions_to_migrate AS migrate
                    ON migrate.transaction_id = legacy.TransactionId;

                INSERT INTO economy_ledger_postings (
                    transaction_id,
                    posting_index,
                    account_code,
                    debit_cents,
                    credit_cents,
                    memo
                )
                SELECT
                    legacy.TransactionId,
                    legacy.PostingIndex,
                    legacy.AccountCode,
                    legacy.DebitCents,
                    legacy.CreditCents,
                    legacy.Memo
                FROM EconomyLedgerPostings AS legacy
                INNER JOIN legacy_economy_transactions_to_migrate AS migrate
                    ON migrate.transaction_id = legacy.TransactionId;

                DROP TABLE temp.legacy_economy_transactions_to_migrate;
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction = transaction;
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

        object? result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return Convert.ToInt64(
            result,
            System.Globalization.CultureInfo.InvariantCulture) == 1;
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
