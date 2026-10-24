using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Economy;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteWorldEventStateStore : IWorldEventStateStore
{
    private const int PayloadSchemaVersion = 1;

    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqliteWorldEventStateStore> _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    private int _initialized;

    public SqliteWorldEventStateStore(
        OpenCareerDatabaseOptions options,
        ILogger<SqliteWorldEventStateStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SaveAsync(
        WorldEventStateSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO world_event_states (
                    snapshot_id,
                    payload_schema_version,
                    updated_at_ms,
                    payload_json
                )
                VALUES (
                    $snapshot_id,
                    $payload_schema_version,
                    $updated_at_ms,
                    $payload_json
                )
                ON CONFLICT(snapshot_id) DO UPDATE SET
                    payload_schema_version = excluded.payload_schema_version,
                    updated_at_ms = excluded.updated_at_ms,
                    payload_json = excluded.payload_json
                WHERE excluded.updated_at_ms >= world_event_states.updated_at_ms;
                """;

            command.Parameters.AddWithValue("$snapshot_id", snapshot.SnapshotId);
            command.Parameters.AddWithValue("$payload_schema_version", PayloadSchemaVersion);
            command.Parameters.AddWithValue(
                "$updated_at_ms",
                snapshot.UpdatedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue(
                "$payload_json",
                JsonSerializer.Serialize(snapshot, _jsonOptions));

            int affected = await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            if (affected == 0)
            {
                _logger.LogDebug(
                    "Ignored stale world-event snapshot {SnapshotId} at {UpdatedAt}.",
                    snapshot.SnapshotId,
                    snapshot.UpdatedAt);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<WorldEventStateSnapshot?> GetAsync(
        string snapshotId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(snapshotId))
            throw new ArgumentException("Snapshot id is required.", nameof(snapshotId));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT payload_schema_version, payload_json
            FROM world_event_states
            WHERE snapshot_id = $snapshot_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$snapshot_id", snapshotId);

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return ReadSnapshot(
            reader.GetInt32(0),
            reader.GetString(1));
    }

    public async Task<IReadOnlyList<WorldEventStateSnapshot>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT payload_schema_version, payload_json
            FROM world_event_states
            ORDER BY snapshot_id COLLATE BINARY ASC;
            """;

        var snapshots = new List<WorldEventStateSnapshot>();

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            snapshots.Add(ReadSnapshot(
                reader.GetInt32(0),
                reader.GetString(1)));
        }

        return snapshots;
    }

    private async Task EnsureInitializedAsync(
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _initialized) == 1)
            return;

        await _initializationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_initialized == 1)
                return;

            string? directory = Path.GetDirectoryName(_options.DatabasePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await ExecutePragmaAsync(
                connection,
                "PRAGMA journal_mode = WAL;",
                cancellationToken).ConfigureAwait(false);

            await OpenCareerDatabaseMigrator
                .MigrateAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            Volatile.Write(ref _initialized, 1);

            _logger.LogInformation(
                "World-event SQLite storage ready at {DatabasePath}, schema {SchemaVersion}.",
                _options.DatabasePath,
                OpenCareerDatabaseMigrator.CurrentSchemaVersion);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecutePragmaAsync(
            connection,
            "PRAGMA foreign_keys = ON;",
            cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(
            connection,
            "PRAGMA busy_timeout = 5000;",
            cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(
            connection,
            "PRAGMA synchronous = NORMAL;",
            cancellationToken).ConfigureAwait(false);

        return connection;
    }

    private static async Task ExecutePragmaAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private WorldEventStateSnapshot ReadSnapshot(
        int payloadSchemaVersion,
        string json)
    {
        if (payloadSchemaVersion != PayloadSchemaVersion)
        {
            throw new NotSupportedException(
                $"World-event payload schema {payloadSchemaVersion} is not supported.");
        }

        try
        {
            WorldEventStateSnapshot snapshot =
                JsonSerializer.Deserialize<WorldEventStateSnapshot>(
                    json,
                    _jsonOptions) ??
                throw new InvalidDataException(
                    "Stored world-event payload deserialized to null.");

            snapshot.Validate();
            return snapshot;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Stored world-event payload is invalid.",
                ex);
        }
    }
}
