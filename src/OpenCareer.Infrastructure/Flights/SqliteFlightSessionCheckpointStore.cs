using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Infrastructure.Flights;

public sealed class SqliteFlightSessionCheckpointStore :
    IFlightSessionCheckpointStore
{
    private const int CurrentSchemaVersion = 1;
    private const long CurrentSlotId = 1;

    private sealed record CheckpointRow(
        string SessionId,
        int SchemaVersion,
        int Status,
        long UpdatedAtUtcTicks,
        string? AuthorityJson,
        string Payload);

    private sealed record CheckpointAuthority(
        Guid? ContractId,
        DateTimeOffset CreatedAt,
        FlightSessionPlan? Plan);

    private sealed record PreviousCheckpointRow(
        string SuccessorSessionId,
        string? SuccessorContractId,
        CheckpointRow Checkpoint);

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _initialized;

    public SqliteFlightSessionCheckpointStore(
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        string fullPath =
            Path.GetFullPath(databasePath);

        string? directory =
            Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        _connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            }.ToString();
    }

    public async Task SaveAsync(
        FlightSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        session = session.EnsureLegLandingEpisodes();
        ValidateForPersistence(session);

        string payload =
            JsonSerializer.Serialize(
                session,
                JsonOptions);
        string authorityJson =
            SerializeAuthority(session);

        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await EnsureInitializedAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var connection =
                new SqliteConnection(_connectionString);

            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(
                    connection,
                    cancellationToken)
                .ConfigureAwait(false);

            using SqliteTransaction transaction =
                connection.BeginTransaction();

            CheckpointRow? currentRow =
                await ReadCurrentRowAsync(
                        connection,
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (currentRow is not null
                && !string.Equals(
                    currentRow.SessionId,
                    session.SessionId.ToString("D"),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The persisted flight session must be cleared before another session can be saved.");
            }

            FlightSession? current =
                TryReadValidCheckpoint(currentRow);

            if (current is not null)
            {
                if (current.ContractId != session.ContractId
                    || current.CreatedAt != session.CreatedAt
                    || current.Plan != session.Plan)
                {
                    throw new InvalidOperationException(
                        "Flight-session checkpoint immutable authority cannot change within a session.");
                }

                if (current.IsTerminal
                    && session.Status != current.Status)
                {
                    throw new InvalidOperationException(
                        "Flight-session checkpoint terminal authority cannot change within a session.");
                }

                if (session.UpdatedAt < current.UpdatedAt)
                {
                    transaction.Commit();
                    return;
                }

                if (session.UpdatedAt > current.UpdatedAt
                    || (session.UpdatedAt == current.UpdatedAt
                        && !string.Equals(
                            payload,
                            currentRow!.Payload,
                            StringComparison.Ordinal)))
                {
                    await SavePreviousRowAsync(
                            connection,
                            transaction,
                            currentRow!,
                            session,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else if (currentRow is null)
            {
                await DeletePreviousRowAsync(
                        connection,
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                FlightSession? previous =
                    await TryReadMatchingPreviousAsync(
                            connection,
                            transaction,
                            currentRow,
                            session,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (previous is null)
                {
                    if (IsTerminalStatus(currentRow.Status)
                        && session.Status != (FlightSessionStatus)currentRow.Status)
                    {
                        throw new InvalidOperationException(
                            "Flight-session checkpoint terminal authority cannot change within a session.");
                    }

                    ValidateStoredAuthority(
                        currentRow.AuthorityJson,
                        session);
                }

                if (session.UpdatedAt.UtcTicks < currentRow.UpdatedAtUtcTicks)
                {
                    transaction.Commit();
                    return;
                }

                if (previous is null)
                {
                    await DeletePreviousRowAsync(
                            connection,
                            transaction,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (session.UpdatedAt < previous.UpdatedAt)
                {
                    transaction.Commit();
                    return;
                }
            }

            await SaveCurrentRowAsync(
                    connection,
                    transaction,
                    session,
                    payload,
                    authorityJson,
                    cancellationToken)
                .ConfigureAwait(false);

            transaction.Commit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<FlightSession?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var connection =
            new SqliteConnection(_connectionString);

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        CheckpointRow? current =
            await ReadCurrentRowAsync(
                    connection,
                    transaction: null,
                    cancellationToken)
                .ConfigureAwait(false);

        if (current is null)
            return null;

        try
        {
            return ReadValidCheckpoint(current);
        }
        catch (Exception currentFailure)
            when (IsInvalidCheckpointException(currentFailure))
        {
            PreviousCheckpointRow? previous =
                await ReadPreviousRowAsync(
                        connection,
                        transaction: null,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (previous is null)
            {
                throw new InvalidDataException(
                    "The current flight-session checkpoint is invalid and no previous checkpoint is available.",
                    currentFailure);
            }

            try
            {
                FlightSession fallback =
                    ReadValidCheckpoint(previous.Checkpoint);

                ValidateFallbackIdentity(
                    current,
                    previous,
                    fallback);

                return fallback;
            }
            catch (Exception fallbackFailure)
                when (IsInvalidCheckpointException(fallbackFailure))
            {
                throw new InvalidDataException(
                    "The current and previous flight-session checkpoints are invalid or ambiguous.",
                    new AggregateException(
                        currentFailure,
                        fallbackFailure));
            }
        }
    }

    public async Task ClearAsync(
        CancellationToken cancellationToken = default)
    {
        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await EnsureInitializedAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var connection =
                new SqliteConnection(_connectionString);

            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(
                    connection,
                    cancellationToken)
                .ConfigureAwait(false);

            await using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                DELETE FROM flight_session_checkpoint_previous;
                DELETE FROM flight_session_checkpoint;
                """;

            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task ClearAsync(
        Guid expectedSessionId,
        CancellationToken cancellationToken = default)
    {
        if (expectedSessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Session ID is required.",
                nameof(expectedSessionId));
        }

        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await EnsureInitializedAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var connection =
                new SqliteConnection(_connectionString);

            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(
                    connection,
                    cancellationToken)
                .ConfigureAwait(false);

            using SqliteTransaction transaction =
                connection.BeginTransaction();

            await using SqliteCommand command =
                connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                DELETE FROM flight_session_checkpoint
                WHERE slot_id = $slotId
                    AND session_id = $expectedSessionId;
                """;
            command.Parameters.AddWithValue("$slotId", CurrentSlotId);
            command.Parameters.AddWithValue(
                "$expectedSessionId",
                expectedSessionId.ToString("D"));

            int removed =
                await command
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (removed == 0)
            {
                CheckpointRow? current =
                    await ReadCurrentRowAsync(
                            connection,
                            transaction,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (current is not null)
                {
                    throw new InvalidOperationException(
                        "The persisted flight session changed before checkpoint cleanup completed.");
                }
            }

            await DeletePreviousRowAsync(
                    connection,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);

            transaction.Commit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static async Task<CheckpointRow?> ReadCurrentRowAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                session_id,
                payload_schema_version,
                status,
                updated_at_utc_ticks,
                authority_json,
                payload_json
            FROM flight_session_checkpoint
            WHERE slot_id = $slotId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$slotId", CurrentSlotId);

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new CheckpointRow(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5));
    }

    private static async Task<PreviousCheckpointRow?> ReadPreviousRowAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                successor_session_id,
                successor_contract_id,
                session_id,
                payload_schema_version,
                status,
                updated_at_utc_ticks,
                payload_json
            FROM flight_session_checkpoint_previous
            WHERE slot_id = $slotId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$slotId", CurrentSlotId);

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new PreviousCheckpointRow(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            new CheckpointRow(
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt64(5),
                AuthorityJson: null,
                reader.GetString(6)));
    }

    private static async Task SavePreviousRowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CheckpointRow previous,
        FlightSession successor,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO flight_session_checkpoint_previous (
                slot_id,
                successor_session_id,
                successor_contract_id,
                session_id,
                payload_schema_version,
                status,
                updated_at_utc_ticks,
                payload_json
            ) VALUES (
                $slotId,
                $successorSessionId,
                $successorContractId,
                $sessionId,
                $schemaVersion,
                $status,
                $updatedAtUtcTicks,
                $payloadJson
            )
            ON CONFLICT(slot_id) DO UPDATE SET
                successor_session_id = excluded.successor_session_id,
                successor_contract_id = excluded.successor_contract_id,
                session_id = excluded.session_id,
                payload_schema_version = excluded.payload_schema_version,
                status = excluded.status,
                updated_at_utc_ticks = excluded.updated_at_utc_ticks,
                payload_json = excluded.payload_json;
            """;
        command.Parameters.AddWithValue("$slotId", CurrentSlotId);
        command.Parameters.AddWithValue(
            "$successorSessionId",
            successor.SessionId.ToString("D"));
        command.Parameters.AddWithValue(
            "$successorContractId",
            successor.ContractId is { } contractId
                ? contractId.ToString("D")
                : DBNull.Value);
        command.Parameters.AddWithValue("$sessionId", previous.SessionId);
        command.Parameters.AddWithValue("$schemaVersion", previous.SchemaVersion);
        command.Parameters.AddWithValue("$status", previous.Status);
        command.Parameters.AddWithValue("$updatedAtUtcTicks", previous.UpdatedAtUtcTicks);
        command.Parameters.AddWithValue("$payloadJson", previous.Payload);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SaveCurrentRowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FlightSession session,
        string payload,
        string authorityJson,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO flight_session_checkpoint (
                slot_id,
                session_id,
                payload_schema_version,
                status,
                updated_at_utc_ticks,
                authority_json,
                payload_json
            ) VALUES (
                $slotId,
                $sessionId,
                $schemaVersion,
                $status,
                $updatedAtUtcTicks,
                $authorityJson,
                $payloadJson
            )
            ON CONFLICT(slot_id) DO UPDATE SET
                session_id = excluded.session_id,
                payload_schema_version = excluded.payload_schema_version,
                status = excluded.status,
                updated_at_utc_ticks = excluded.updated_at_utc_ticks,
                authority_json = excluded.authority_json,
                payload_json = excluded.payload_json;
            """;
        command.Parameters.AddWithValue("$slotId", CurrentSlotId);
        command.Parameters.AddWithValue("$sessionId", session.SessionId.ToString("D"));
        command.Parameters.AddWithValue("$schemaVersion", session.SchemaVersion);
        command.Parameters.AddWithValue("$status", (int)session.Status);
        command.Parameters.AddWithValue("$updatedAtUtcTicks", session.UpdatedAt.UtcTicks);
        command.Parameters.AddWithValue("$authorityJson", authorityJson);
        command.Parameters.AddWithValue("$payloadJson", payload);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeletePreviousRowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM flight_session_checkpoint_previous;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<FlightSession?> TryReadMatchingPreviousAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CheckpointRow invalidCurrent,
        FlightSession incoming,
        CancellationToken cancellationToken)
    {
        PreviousCheckpointRow? previous =
            await ReadPreviousRowAsync(
                    connection,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);

        if (previous is null)
            return null;

        try
        {
            FlightSession fallback = ReadValidCheckpoint(previous.Checkpoint);
            ValidateFallbackIdentity(invalidCurrent, previous, fallback);

            if (fallback.SessionId != incoming.SessionId)
                return null;

            if (fallback.ContractId != incoming.ContractId
                || fallback.CreatedAt != incoming.CreatedAt
                || fallback.Plan != incoming.Plan)
            {
                throw new InvalidOperationException(
                    "Flight-session checkpoint immutable authority cannot change within a session.");
            }

            if (fallback.IsTerminal
                && incoming.Status != fallback.Status)
            {
                throw new InvalidOperationException(
                    "Flight-session checkpoint terminal authority cannot change within a session.");
            }

            return fallback;
        }
        catch (Exception exception)
            when (IsInvalidCheckpointException(exception))
        {
            return null;
        }
    }

    private static FlightSession? TryReadValidCheckpoint(CheckpointRow? row)
    {
        if (row is null)
            return null;

        try
        {
            return ReadValidCheckpoint(row);
        }
        catch (Exception exception)
            when (IsInvalidCheckpointException(exception))
        {
            return null;
        }
    }

    private static bool IsTerminalStatus(int status) =>
        status is (int)FlightSessionStatus.Interrupted
            or (int)FlightSessionStatus.Completed
            or (int)FlightSessionStatus.Cancelled;

    private static FlightSession ReadValidCheckpoint(CheckpointRow row)
    {
        if (row.SchemaVersion != CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Flight-session checkpoint schema {row.SchemaVersion} is not supported.");
        }

        FlightSession session =
            JsonSerializer.Deserialize<FlightSession>(
                row.Payload,
                JsonOptions)
            ?? throw new InvalidDataException(
                "Flight-session checkpoint payload is empty.");

        session = session.EnsureLegLandingEpisodes();

        ValidateLoadedCheckpoint(
            session,
            row.SessionId,
            row.SchemaVersion,
            row.Status,
            row.UpdatedAtUtcTicks);
        ValidateStoredAuthority(
            row.AuthorityJson,
            session,
            invalidDataOnMismatch: true);

        return session;
    }

    private static string SerializeAuthority(FlightSession session) =>
        JsonSerializer.Serialize(
            new CheckpointAuthority(
                session.ContractId,
                session.CreatedAt,
                session.Plan),
            JsonOptions);

    private static void ValidateStoredAuthority(
        string? authorityJson,
        FlightSession session,
        bool invalidDataOnMismatch = false)
    {
        if (authorityJson is null)
            return;

        CheckpointAuthority authority;

        try
        {
            authority =
                JsonSerializer.Deserialize<CheckpointAuthority>(
                    authorityJson,
                    JsonOptions)
                ?? throw new InvalidDataException(
                    "Flight-session checkpoint authority metadata is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Flight-session checkpoint authority metadata is invalid.",
                exception);
        }

        if (authority.ContractId != session.ContractId
            || authority.CreatedAt != session.CreatedAt
            || authority.Plan != session.Plan)
        {
            const string message =
                "Flight-session checkpoint immutable authority cannot change within a session.";

            if (invalidDataOnMismatch)
                throw new InvalidDataException(message);

            throw new InvalidOperationException(message);
        }
    }

    private static void ValidateFallbackIdentity(
        CheckpointRow invalidCurrent,
        PreviousCheckpointRow previous,
        FlightSession fallback)
    {
        string fallbackSessionId = fallback.SessionId.ToString("D");
        string? fallbackContractId =
            fallback.ContractId?.ToString("D");

        if (!string.Equals(
                invalidCurrent.SessionId,
                previous.SuccessorSessionId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                previous.SuccessorSessionId,
                fallbackSessionId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                previous.SuccessorContractId,
                fallbackContractId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Previous flight-session checkpoint identity does not match its current successor.");
        }
    }

    private static bool IsInvalidCheckpointException(Exception exception) =>
        exception is InvalidDataException
            or JsonException
            or NotSupportedException
            or ArgumentException;

    private async Task EnsureInitializedAsync(
        CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initializationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_initialized)
                return;

            await using var connection =
                new SqliteConnection(_connectionString);

            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(
                    connection,
                    cancellationToken)
                .ConfigureAwait(false);

            await ExecutePragmaAsync(
                    connection,
                    "PRAGMA journal_mode = WAL;",
                    cancellationToken)
                .ConfigureAwait(false);

            await OpenCareerDatabaseMigrator
                .MigrateAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await ExecutePragmaAsync(
                connection,
                "PRAGMA busy_timeout = 5000;",
                cancellationToken)
            .ConfigureAwait(false);

        await ExecutePragmaAsync(
                connection,
                "PRAGMA synchronous = NORMAL;",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ExecutePragmaAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText = sql;

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ValidateForPersistence(
        FlightSession session)
    {
        if (session.SessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Flight session ID cannot be empty.",
                nameof(session));
        }

        if (session.SchemaVersion != CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Flight-session schema {session.SchemaVersion} is not supported.");
        }

        if (session.UpdatedAt < session.CreatedAt)
        {
            throw new ArgumentException(
                "Flight session cannot be updated before it was created.",
                nameof(session));
        }

        if (!Enum.IsDefined(session.Status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(session),
                "Flight session status is invalid.");
        }

        if (!Enum.IsDefined(session.OperationState))
        {
            throw new ArgumentOutOfRangeException(
                nameof(session),
                "Flight operation state is invalid.");
        }

        session.Plan?.Validate();

        try
        {
            session.ValidateLegs();
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or InvalidOperationException)
        {
            throw new InvalidDataException(
                "FlightSession flight-leg state is invalid.",
                exception);
        }
    }

    private static void ValidateLoadedCheckpoint(
        FlightSession session,
        string persistedSessionId,
        int persistedSchemaVersion,
        int persistedStatus,
        long persistedUpdatedAtUtcTicks)
    {
        ValidateForPersistence(session);

        if (!Guid.TryParse(
                persistedSessionId,
                out Guid sessionId)
            || sessionId != session.SessionId)
        {
            throw new InvalidDataException(
                "Flight-session checkpoint identity does not match its payload.");
        }

        if (persistedSchemaVersion != session.SchemaVersion)
        {
            throw new InvalidDataException(
                "Flight-session checkpoint schema metadata does not match its payload.");
        }

        if (persistedStatus != (int)session.Status)
        {
            throw new InvalidDataException(
                "Flight-session checkpoint status metadata does not match its payload.");
        }

        if (persistedUpdatedAtUtcTicks
            != session.UpdatedAt.UtcTicks)
        {
            throw new InvalidDataException(
                "Flight-session checkpoint timestamp metadata does not match its payload.");
        }
    }
}
