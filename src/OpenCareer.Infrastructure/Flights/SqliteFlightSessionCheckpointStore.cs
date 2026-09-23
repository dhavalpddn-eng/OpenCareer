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

            using var transaction = connection.BeginTransaction();
            await using (var previous = connection.CreateCommand())
            {
                previous.Transaction = transaction;
                previous.CommandText = "SELECT payload_json FROM flight_session_checkpoint WHERE slot_id = $slotId;";
                previous.Parameters.AddWithValue("$slotId", CurrentSlotId);
                if (await previous.ExecuteScalarAsync(cancellationToken) is string previousPayload)
                {
                    FlightSession saved = JsonSerializer.Deserialize<FlightSession>(previousPayload, JsonOptions)
                        ?? throw new InvalidDataException("Flight-session checkpoint payload is empty.");
                    if (saved.SessionId != session.SessionId || saved.AircraftIdentity != session.AircraftIdentity)
                        throw new InvalidOperationException("Flight-session checkpoint cannot replace its airframe identity.");
                }
            }

            await using var command =
                connection.CreateCommand();

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
                )
                VALUES (
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
                    payload_json = excluded.payload_json
                WHERE excluded.updated_at_utc_ticks
                    >= flight_session_checkpoint.updated_at_utc_ticks;
                """;

            command.Parameters.AddWithValue(
                "$slotId",
                CurrentSlotId);

            command.Parameters.AddWithValue(
                "$sessionId",
                session.SessionId.ToString("D"));

            command.Parameters.AddWithValue(
                "$schemaVersion",
                session.SchemaVersion);

            command.Parameters.AddWithValue(
                "$status",
                (int)session.Status);

            command.Parameters.AddWithValue(
                "$updatedAtUtcTicks",
                session.UpdatedAt.UtcTicks);

            command.Parameters.AddWithValue(
                "$payloadJson",
                payload);

            await command
                .ExecuteNonQueryAsync(cancellationToken)
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

        await using var command =
            connection.CreateCommand();

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

        command.Parameters.AddWithValue(
            "$slotId",
            CurrentSlotId);

        await using var reader =
            await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        string sessionIdText =
            reader.GetString(0);

        int schemaVersion =
            reader.GetInt32(1);

        int statusValue =
            reader.GetInt32(2);

        long updatedAtUtcTicks =
            reader.GetInt64(3);

        string payload =
            reader.GetString(4);

        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Flight-session checkpoint schema {schemaVersion} is not supported.");
        }

        FlightSession session =
            JsonSerializer.Deserialize<FlightSession>(
                payload,
                JsonOptions)
            ?? throw new InvalidDataException(
                "Flight-session checkpoint payload is empty.");

        ValidateLoadedCheckpoint(
            session,
            sessionIdText,
            schemaVersion,
            statusValue,
            updatedAtUtcTicks);

        return session;
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
                DELETE FROM flight_session_checkpoint
                WHERE slot_id = $slotId;
                """;

            command.Parameters.AddWithValue(
                "$slotId",
                CurrentSlotId);

            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

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
        session.AircraftIdentity?.Validate();
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
