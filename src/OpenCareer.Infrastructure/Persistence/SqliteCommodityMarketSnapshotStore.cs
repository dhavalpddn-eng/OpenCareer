using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteCommodityMarketSnapshotStore :
    ICommodityMarketSnapshotStore
{
    private const int PayloadSchemaVersion = 1;

    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqliteCommodityMarketSnapshotStore> _logger;
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

    public SqliteCommodityMarketSnapshotStore(
        OpenCareerDatabaseOptions options,
        ILogger<SqliteCommodityMarketSnapshotStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SaveAsync(
        CommodityMarketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO commodity_market_snapshots (
                    scope,
                    location_id,
                    payload_schema_version,
                    captured_at_ms,
                    payload_json
                )
                VALUES (
                    $scope,
                    $location_id,
                    $payload_schema_version,
                    $captured_at_ms,
                    $payload_json
                )
                ON CONFLICT(scope, location_id) DO UPDATE SET
                    payload_schema_version = excluded.payload_schema_version,
                    captured_at_ms = excluded.captured_at_ms,
                    payload_json = excluded.payload_json
                WHERE excluded.captured_at_ms
                    >= commodity_market_snapshots.captured_at_ms;
                """;

            var payload =
                new CommodityMarketSnapshotPayload(
                    snapshot.Scope,
                    snapshot.LocationId,
                    snapshot.CapturedAt,
                    snapshot.Commodities);

            command.Parameters.AddWithValue(
                "$scope",
                (int)snapshot.Scope);
            command.Parameters.AddWithValue(
                "$location_id",
                snapshot.LocationId);
            command.Parameters.AddWithValue(
                "$payload_schema_version",
                PayloadSchemaVersion);
            command.Parameters.AddWithValue(
                "$captured_at_ms",
                snapshot.CapturedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue(
                "$payload_json",
                JsonSerializer.Serialize(
                    payload,
                    _jsonOptions));

            int affected = await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            if (affected == 0)
            {
                _logger.LogDebug(
                    "Ignored stale commodity market snapshot {Scope}/{LocationId} at {CapturedAt}.",
                    snapshot.Scope,
                    snapshot.LocationId,
                    snapshot.CapturedAt);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<CommodityMarketSnapshot?> GetAsync(
        CommodityMarketScope scope,
        string locationId,
        CancellationToken cancellationToken = default)
    {
        ValidateLookup(
            scope,
            locationId);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT payload_schema_version, payload_json
            FROM commodity_market_snapshots
            WHERE scope = $scope
                AND location_id = $location_id
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$scope",
            (int)scope);
        command.Parameters.AddWithValue(
            "$location_id",
            locationId);

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return ReadSnapshot(
            reader.GetInt32(0),
            reader.GetString(1));
    }

    public async Task<IReadOnlyList<CommodityMarketSnapshot>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT payload_schema_version, payload_json
            FROM commodity_market_snapshots
            ORDER BY scope ASC, location_id COLLATE BINARY ASC;
            """;

        var snapshots =
            new List<CommodityMarketSnapshot>();

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            snapshots.Add(
                ReadSnapshot(
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

            string? directory =
                Path.GetDirectoryName(
                    _options.DatabasePath);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await ExecutePragmaAsync(
                connection,
                "PRAGMA journal_mode = WAL;",
                cancellationToken).ConfigureAwait(false);

            await OpenCareerDatabaseMigrator
                .MigrateAsync(
                    connection,
                    cancellationToken)
                .ConfigureAwait(false);

            Volatile.Write(
                ref _initialized,
                1);

            _logger.LogInformation(
                "Commodity-market SQLite storage ready at {DatabasePath}, schema {SchemaVersion}.",
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
        var builder =
            new SqliteConnectionStringBuilder
            {
                DataSource = _options.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            };

        var connection =
            new SqliteConnection(
                builder.ToString());

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

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
        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText = sql;

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private CommodityMarketSnapshot ReadSnapshot(
        int payloadSchemaVersion,
        string json)
    {
        if (payloadSchemaVersion != PayloadSchemaVersion)
        {
            throw new NotSupportedException(
                $"Commodity-market payload schema {payloadSchemaVersion} is not supported.");
        }

        try
        {
            CommodityMarketSnapshotPayload payload =
                JsonSerializer.Deserialize<CommodityMarketSnapshotPayload>(
                    json,
                    _jsonOptions)
                ?? throw new InvalidDataException(
                    "Stored commodity-market payload deserialized to null.");

            return CommodityMarketSnapshot.Create(
                payload.Scope,
                payload.LocationId,
                payload.CapturedAt,
                payload.Commodities);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Stored commodity-market payload is invalid.",
                ex);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException(
                "Stored commodity-market payload violates domain constraints.",
                ex);
        }
    }

    private static void ValidateLookup(
        CommodityMarketScope scope,
        string locationId)
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
    }

    private sealed record CommodityMarketSnapshotPayload(
        CommodityMarketScope Scope,
        string LocationId,
        DateTimeOffset CapturedAt,
        IReadOnlyList<CommodityMarketEntry> Commodities);
}
