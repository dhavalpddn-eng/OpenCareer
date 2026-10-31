using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteMarketStateStore : IMarketStateStore
{
    private const int PayloadSchemaVersion = 1;

    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqliteMarketStateStore> _logger;
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

    public SqliteMarketStateStore(
        OpenCareerDatabaseOptions options,
        ILogger<SqliteMarketStateStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SaveAsync(
        MarketState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (string.IsNullOrWhiteSpace(state.MarketId))
            throw new ArgumentException("Market id is required.", nameof(state));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO market_states (
                    market_id,
                    payload_schema_version,
                    segment,
                    updated_at_ms,
                    payload_json
                )
                VALUES (
                    $market_id,
                    $payload_schema_version,
                    $segment,
                    $updated_at_ms,
                    $payload_json
                )
                ON CONFLICT(market_id) DO UPDATE SET
                    payload_schema_version = excluded.payload_schema_version,
                    segment = excluded.segment,
                    updated_at_ms = excluded.updated_at_ms,
                    payload_json = excluded.payload_json
                WHERE excluded.updated_at_ms >= market_states.updated_at_ms;
                """;

            command.Parameters.AddWithValue("$market_id", state.MarketId);
            command.Parameters.AddWithValue("$payload_schema_version", PayloadSchemaVersion);
            command.Parameters.AddWithValue("$segment", (int)state.Segment);
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
                    "Ignored stale market snapshot {MarketId} at {UpdatedAt}.",
                    state.MarketId,
                    state.UpdatedAt);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<MarketState?> GetAsync(
        string marketId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(marketId))
            throw new ArgumentException("Market id is required.", nameof(marketId));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT payload_schema_version, payload_json
            FROM market_states
            WHERE market_id = $market_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$market_id", marketId);

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return ReadState(
            reader.GetInt32(0),
            reader.GetString(1));
    }

    public async Task<IReadOnlyList<MarketState>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT payload_schema_version, payload_json
            FROM market_states
            ORDER BY market_id COLLATE BINARY ASC;
            """;

        var states = new List<MarketState>();

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
                "Market-state SQLite storage ready at {DatabasePath}, schema {SchemaVersion}.",
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

    private MarketState ReadState(
        int payloadSchemaVersion,
        string json)
    {
        if (payloadSchemaVersion != PayloadSchemaVersion)
        {
            throw new NotSupportedException(
                $"Market-state payload schema {payloadSchemaVersion} is not supported.");
        }

        try
        {
            return JsonSerializer.Deserialize<MarketState>(
                       json,
                       _jsonOptions) ??
                   throw new InvalidDataException(
                       "Stored market-state payload deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Stored market-state payload is invalid.",
                ex);
        }
    }
}
