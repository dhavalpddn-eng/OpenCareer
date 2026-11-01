using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteEconomicCycleStateStore : IEconomicCycleStateStore
{
    private const int PayloadSchemaVersion = 1;

    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqliteEconomicCycleStateStore> _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private int _initialized;

    public SqliteEconomicCycleStateStore(
        OpenCareerDatabaseOptions options,
        ILogger<SqliteEconomicCycleStateStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SaveAsync(
        EconomicCycleState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (string.IsNullOrWhiteSpace(state.RegionId))
            throw new ArgumentException("Region id is required.", nameof(state));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO economic_cycle_states (
                    region_id,
                    payload_schema_version,
                    updated_at_ms,
                    payload_json
                )
                VALUES (
                    $region_id,
                    $payload_schema_version,
                    $updated_at_ms,
                    $payload_json
                )
                ON CONFLICT(region_id) DO UPDATE SET
                    payload_schema_version = excluded.payload_schema_version,
                    updated_at_ms = excluded.updated_at_ms,
                    payload_json = excluded.payload_json
                WHERE excluded.updated_at_ms >= economic_cycle_states.updated_at_ms;
                """;

            command.Parameters.AddWithValue("$region_id", state.RegionId);
            command.Parameters.AddWithValue("$payload_schema_version", PayloadSchemaVersion);
            command.Parameters.AddWithValue(
                "$updated_at_ms",
                state.UpdatedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue(
                "$payload_json",
                JsonSerializer.Serialize(state, _jsonOptions));

            int affected = await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            if (affected == 0)
            {
                _logger.LogDebug(
                    "Ignored stale economic-cycle snapshot {RegionId} at {UpdatedAt}.",
                    state.RegionId,
                    state.UpdatedAt);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<EconomicCycleState?> GetAsync(
        string regionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(regionId))
            throw new ArgumentException("Region id is required.", nameof(regionId));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT payload_schema_version, payload_json
            FROM economic_cycle_states
            WHERE region_id = $region_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$region_id", regionId);

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return ReadState(
            reader.GetInt32(0),
            reader.GetString(1));
    }

    public async Task<IReadOnlyList<EconomicCycleState>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT payload_schema_version, payload_json
            FROM economic_cycle_states
            ORDER BY region_id COLLATE BINARY ASC;
            """;

        var states = new List<EconomicCycleState>();

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            states.Add(ReadState(
                reader.GetInt32(0),
                reader.GetString(1)));
        }

        return states;
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
                "Economic-cycle SQLite storage ready at {DatabasePath}, schema {SchemaVersion}.",
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

    private EconomicCycleState ReadState(
        int payloadSchemaVersion,
        string json)
    {
        if (payloadSchemaVersion != PayloadSchemaVersion)
        {
            throw new NotSupportedException(
                $"Economic-cycle payload schema {payloadSchemaVersion} is not supported.");
        }

        try
        {
            return JsonSerializer.Deserialize<EconomicCycleState>(
                       json,
                       _jsonOptions) ??
                   throw new InvalidDataException(
                       "Stored economic-cycle payload deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Stored economic-cycle payload is invalid.",
                ex);
        }
    }
}
