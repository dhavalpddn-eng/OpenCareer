using Microsoft.Data.Sqlite;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Aircraft;

public sealed class SqliteAircraftAvailabilityStore : IAircraftAvailabilityStore
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
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);

        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT canonical_aircraft_id, status
            FROM aircraft_availability
            WHERE canonical_aircraft_id = $canonicalAircraftId COLLATE NOCASE
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$canonicalAircraftId",
            canonicalAircraftId.Trim());

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var state = new AircraftAvailabilityState(
            reader.GetString(0),
            (AircraftAvailabilityStatus)reader.GetInt32(1));

        return state.Normalize();
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

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO aircraft_availability (
                    canonical_aircraft_id,
                    status
                )
                VALUES (
                    $canonicalAircraftId,
                    $status
                )
                ON CONFLICT(canonical_aircraft_id) DO UPDATE SET
                    canonical_aircraft_id = excluded.canonical_aircraft_id,
                    status = excluded.status;
                """;

            command.Parameters.AddWithValue(
                "$canonicalAircraftId",
                normalized.CanonicalAircraftId);
            command.Parameters.AddWithValue(
                "$status",
                (int)normalized.Status);

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
                        status INTEGER NOT NULL CHECK (status IN (0, 1))
                    );
                    """,
                    cancellationToken)
                .ConfigureAwait(false);

            Volatile.Write(ref _initialized, 1);
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
