using Microsoft.Data.Sqlite;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Aircraft;

public sealed class SqliteAircraftAvailabilityStore : IAircraftAvailabilityStore, IAircraftReservationStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _initialized;

    public SqliteAircraftAvailabilityStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        string fullPath = Path.GetFullPath(databasePath);
        string? directory = Path.GetDirectoryName(fullPath);

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

    public async Task<AircraftAvailabilityState?> FindAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default)
    {
        string normalizedAircraftId = NormalizeRequired(
            canonicalAircraftId,
            nameof(canonicalAircraftId));

        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        return await FindStateAsync(
                connection,
                normalizedAircraftId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetAsync(
        AircraftAvailabilityState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        AircraftAvailabilityState normalized = state.Normalize();

        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await EnsureInitializedAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var connection = new SqliteConnection(_connectionString);
            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            await UpsertStateAsync(connection, normalized, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<AircraftReservationAcquireResult> TryReserveAsync(
        string canonicalAircraftId,
        string reservationId,
        CancellationToken cancellationToken = default)
    {
        string normalizedAircraftId = NormalizeRequired(
            canonicalAircraftId,
            nameof(canonicalAircraftId));
        string normalizedReservationId = NormalizeRequired(
            reservationId,
            nameof(reservationId));

        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await EnsureInitializedAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var connection = new SqliteConnection(_connectionString);
            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            await ExecuteAsync(
                    connection,
                    "BEGIN IMMEDIATE;",
                    cancellationToken)
                .ConfigureAwait(false);

            try
            {
                AircraftAvailabilityState? current = await FindStateAsync(
                        connection,
                        normalizedAircraftId,
                        cancellationToken)
                    .ConfigureAwait(false);

                AircraftReservationAcquireResult result;

                if (current is null || current.Status == AircraftAvailabilityStatus.Available)
                {
                    await UpsertStateAsync(
                            connection,
                            new AircraftAvailabilityState(
                                normalizedAircraftId,
                                AircraftAvailabilityStatus.Unavailable,
                                normalizedReservationId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    result = AircraftReservationAcquireResult.Acquired;
                }
                else if (string.Equals(
                    current.ReservationId,
                    normalizedReservationId,
                    StringComparison.Ordinal))
                {
                    result = AircraftReservationAcquireResult.AlreadyHeld;
                }
                else if (current.ReservationId is null)
                {
                    result = AircraftReservationAcquireResult.Unavailable;
                }
                else
                {
                    result = AircraftReservationAcquireResult.HeldByAnotherReservation;
                }

                await ExecuteAsync(
                        connection,
                        "COMMIT;",
                        cancellationToken)
                    .ConfigureAwait(false);

                return result;
            }
            catch
            {
                await RollbackQuietlyAsync(connection).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<AircraftReservationReleaseResult> ReleaseReservationAsync(
        string canonicalAircraftId,
        string reservationId,
        CancellationToken cancellationToken = default)
    {
        string normalizedAircraftId = NormalizeRequired(
            canonicalAircraftId,
            nameof(canonicalAircraftId));
        string normalizedReservationId = NormalizeRequired(
            reservationId,
            nameof(reservationId));

        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await EnsureInitializedAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var connection = new SqliteConnection(_connectionString);
            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            await ExecuteAsync(
                    connection,
                    "BEGIN IMMEDIATE;",
                    cancellationToken)
                .ConfigureAwait(false);

            try
            {
                AircraftAvailabilityState? current = await FindStateAsync(
                        connection,
                        normalizedAircraftId,
                        cancellationToken)
                    .ConfigureAwait(false);

                AircraftReservationReleaseResult result;

                if (current is null || current.Status == AircraftAvailabilityStatus.Available)
                {
                    result = AircraftReservationReleaseResult.AlreadyReleased;
                }
                else if (current.ReservationId is null)
                {
                    result = AircraftReservationReleaseResult.NotReserved;
                }
                else if (!string.Equals(
                    current.ReservationId,
                    normalizedReservationId,
                    StringComparison.Ordinal))
                {
                    result = AircraftReservationReleaseResult.HeldByAnotherReservation;
                }
                else
                {
                    await UpsertStateAsync(
                            connection,
                            new AircraftAvailabilityState(
                                normalizedAircraftId,
                                AircraftAvailabilityStatus.Available),
                            cancellationToken)
                        .ConfigureAwait(false);

                    result = AircraftReservationReleaseResult.Released;
                }

                await ExecuteAsync(
                        connection,
                        "COMMIT;",
                        cancellationToken)
                    .ConfigureAwait(false);

                return result;
            }
            catch
            {
                await RollbackQuietlyAsync(connection).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task EnsureInitializedAsync(
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _initialized) == 1)
            return;

        await _initializationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_initialized == 1)
                return;

            await using var connection = new SqliteConnection(_connectionString);
            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            await ExecuteAsync(
                    connection,
                    "PRAGMA journal_mode = WAL;",
                    cancellationToken)
                .ConfigureAwait(false);

            await ExecuteAsync(
                    connection,
                    """
                    CREATE TABLE IF NOT EXISTS aircraft_availability (
                        canonical_aircraft_id TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                        status INTEGER NOT NULL CHECK (status IN (0, 1)),
                        reservation_id TEXT NULL
                    );
                    """,
                    cancellationToken)
                .ConfigureAwait(false);

            await EnsureReservationColumnAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            Volatile.Write(ref _initialized, 1);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static async Task EnsureReservationColumnAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        bool exists = false;

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(aircraft_availability);";

            await using SqliteDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(
                    reader.GetString(1),
                    "reservation_id",
                    StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
            return;

        try
        {
            await ExecuteAsync(
                    connection,
                    "ALTER TABLE aircraft_availability ADD COLUMN reservation_id TEXT NULL;",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SqliteException exception)
            when (exception.SqliteErrorCode == 1
                && exception.Message.Contains(
                    "duplicate column",
                    StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private static async Task<AircraftAvailabilityState?> FindStateAsync(
        SqliteConnection connection,
        string canonicalAircraftId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT canonical_aircraft_id, status, reservation_id
            FROM aircraft_availability
            WHERE canonical_aircraft_id = $canonicalAircraftId COLLATE NOCASE
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$canonicalAircraftId",
            canonicalAircraftId);

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var state = new AircraftAvailabilityState(
            reader.GetString(0),
            (AircraftAvailabilityStatus)reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));

        return state.Normalize();
    }

    private static async Task UpsertStateAsync(
        SqliteConnection connection,
        AircraftAvailabilityState state,
        CancellationToken cancellationToken)
    {
        AircraftAvailabilityState normalized = state.Normalize();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO aircraft_availability (
                canonical_aircraft_id,
                status,
                reservation_id
            )
            VALUES (
                $canonicalAircraftId,
                $status,
                $reservationId
            )
            ON CONFLICT(canonical_aircraft_id) DO UPDATE SET
                canonical_aircraft_id = excluded.canonical_aircraft_id,
                status = excluded.status,
                reservation_id = excluded.reservation_id;
            """;

        command.Parameters.AddWithValue(
            "$canonicalAircraftId",
            normalized.CanonicalAircraftId);
        command.Parameters.AddWithValue(
            "$status",
            (int)normalized.Status);
        command.Parameters.AddWithValue(
            "$reservationId",
            normalized.ReservationId is null
                ? DBNull.Value
                : normalized.ReservationId);

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string NormalizeRequired(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be blank.", parameterName);

        return value.Trim();
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                connection,
                "PRAGMA busy_timeout = 5000;",
                cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
                connection,
                "PRAGMA synchronous = NORMAL;",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task RollbackQuietlyAsync(
        SqliteConnection connection)
    {
        try
        {
            await ExecuteAsync(
                    connection,
                    "ROLLBACK;",
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (SqliteException)
        {
        }
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
