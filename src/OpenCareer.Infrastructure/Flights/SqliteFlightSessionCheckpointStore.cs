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
        string Payload);

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

            FlightSession? current =
                TryReadValidCheckpoint(currentRow);

            if (current is not null)
            {
                if (current.SessionId == session.SessionId
                    && current.ContractId != session.ContractId)
                {
                    throw new InvalidOperationException(
                        "Flight-session checkpoint contract identity cannot change within a session.");
                }

                if (current.SessionId == session.SessionId
                    && session.UpdatedAt < current.UpdatedAt)
                {
                    transaction.Commit();
                    return;
                }

                if (current.SessionId == session.SessionId
                    && session.UpdatedAt > current.UpdatedAt)
                {
                    await SavePreviousRowAsync(
                            connection,
                            transaction,
                            currentRow!,
                            session,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (current.SessionId != session.SessionId)
                {
                    await DeletePreviousRowAsync(
                            connection,
                            transaction,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else if (currentRow is null
                || !await PreviousMatchesIncomingAsync(
                        connection,
                        transaction,
                        currentRow,
                        session,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                await DeletePreviousRowAsync(
                        connection,
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await SaveCurrentRowAsync(
                    connection,
                    transaction,
                    session,
                    payload,
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
            reader.GetString(4));
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
                payload_json
            ) VALUES (
                $slotId,
                $sessionId,
                $schemaVersion,
                $status,
                $updatedAtUtcTicks,
                $payloadJson
            )
            ON CONFLICT(slot_id) DO UPDATE SET
                session_id = excluded.session_id,
                payload_schema_version = excluded.payload_schema_version,
                status = excluded.status,
                updated_at_utc_ticks = excluded.updated_at_utc_ticks,
                payload_json = excluded.payload_json;
            """;
        command.Parameters.AddWithValue("$slotId", CurrentSlotId);
        command.Parameters.AddWithValue("$sessionId", session.SessionId.ToString("D"));
        command.Parameters.AddWithValue("$schemaVersion", session.SchemaVersion);
        command.Parameters.AddWithValue("$status", (int)session.Status);
        command.Parameters.AddWithValue("$updatedAtUtcTicks", session.UpdatedAt.UtcTicks);
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

    private static async Task<bool> PreviousMatchesIncomingAsync(
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
            return false;

        try
        {
            FlightSession fallback = ReadValidCheckpoint(previous.Checkpoint);
            ValidateFallbackIdentity(invalidCurrent, previous, fallback);
            return fallback.SessionId == incoming.SessionId
                && fallback.ContractId == incoming.ContractId;
        }
        catch (Exception exception)
            when (IsInvalidCheckpointException(exception))
        {
            return false;
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

        return session;
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
