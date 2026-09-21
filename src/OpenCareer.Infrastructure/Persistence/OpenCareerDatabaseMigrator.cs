using Microsoft.Data.Sqlite;

namespace OpenCareer.Infrastructure.Persistence;

internal static class OpenCareerDatabaseMigrator
{
    public const int CurrentSchemaVersion = 4;

    public static async Task MigrateAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        int version = await GetSchemaVersionAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        if (version > CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"OpenCareer database schema {version} is newer than supported schema {CurrentSchemaVersion}.");
        }

        if (version < 1)
        {
            using SqliteTransaction transaction = connection.BeginTransaction();

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
                cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_logbook_entries_ended_at
                    ON logbook_entries (ended_at_ms DESC, entry_id ASC);
                """,
                cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_logbook_entries_entry_kind
                    ON logbook_entries (entry_kind, ended_at_ms DESC);
                """,
                cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_logbook_entries_safety
                    ON logbook_entries (safety_outcome, ended_at_ms DESC);
                """,
                cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_logbook_entries_contract
                    ON logbook_entries (contract_id);
                """,
                cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                "PRAGMA user_version = 1;",
                cancellationToken).ConfigureAwait(false);

            transaction.Commit();
            version = 1;
        }

        if (version < 2)
        {
            using SqliteTransaction transaction = connection.BeginTransaction();

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
                cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                "PRAGMA user_version = 2;",
                cancellationToken).ConfigureAwait(false);

            transaction.Commit();
            version = 2;
        }

        if (version < 3)
        {
            using SqliteTransaction transaction = connection.BeginTransaction();

            await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS market_states (
                    market_id TEXT NOT NULL PRIMARY KEY,
                    payload_schema_version INTEGER NOT NULL,
                    segment INTEGER NOT NULL,
                    updated_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL
                );
                """,
                cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_market_states_segment
                    ON market_states (segment, market_id);
                """,
                cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                "PRAGMA user_version = 3;",
                cancellationToken).ConfigureAwait(false);

            transaction.Commit();
            version = 3;
        }

        if (version < 4)
        {
            using SqliteTransaction transaction = connection.BeginTransaction();

            await ExecuteAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS economic_cycle_states (
                    region_id TEXT NOT NULL PRIMARY KEY,
                    payload_schema_version INTEGER NOT NULL,
                    updated_at_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL
                );
                """,
                cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                "PRAGMA user_version = 4;",
                cancellationToken).ConfigureAwait(false);

            transaction.Commit();
            version = 4;
        }

        if (version != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"OpenCareer database migration ended at schema {version}; expected {CurrentSchemaVersion}.");
        }
    }

    private static async Task<int> GetSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        object? result = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
