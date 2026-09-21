using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Military;
using OpenCareer.Domain.Military;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteOperationConsequenceStore
    : IOperationConsequenceStore
{
    private const int PayloadSchemaVersion = 1;

    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqliteOperationConsequenceStore> _logger;
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

    public SqliteOperationConsequenceStore(
        OpenCareerDatabaseOptions options,
        ILogger<SqliteOperationConsequenceStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OperationConsequenceStoreRecord?> LoadAsync(
        OperationResolutionKey key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT payload_schema_version, saved_at_ms, payload_json
            FROM military_operation_consequences
            WHERE resolution_key = $resolution_key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$resolution_key",
            key.ToString());

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        int payloadSchemaVersion = reader.GetInt32(0);
        long savedAtMs = reader.GetInt64(1);
        string payload = reader.GetString(2);

        OperationConsequenceResult result =
            ReadResult(
                payloadSchemaVersion,
                payload);

        var record = new OperationConsequenceStoreRecord(
            key,
            result,
            DateTimeOffset.FromUnixTimeMilliseconds(savedAtMs));

        record.Validate();
        return record;
    }

    public async Task<OperationConsequenceStoreRecord> SaveAsync(
        OperationConsequenceResult result,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.Validate();

        if (savedAt == default)
            throw new ArgumentOutOfRangeException(nameof(savedAt));

        if (savedAt < result.Outcome.CompletedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(savedAt),
                "Persistence time cannot precede operation completion.");
        }

        OperationResolutionKey key =
            OperationResolutionKey.Create(
                result.Outcome.OperationId,
                result.Outcome.MissionId);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            string payload =
                JsonSerializer.Serialize(
                    result,
                    _jsonOptions);

            await using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO military_operation_consequences (
                    resolution_key,
                    operation_id,
                    mission_id,
                    payload_schema_version,
                    completed_at_ms,
                    saved_at_ms,
                    campaign_id,
                    sector_id,
                    payload_json
                )
                VALUES (
                    $resolution_key,
                    $operation_id,
                    $mission_id,
                    $payload_schema_version,
                    $completed_at_ms,
                    $saved_at_ms,
                    $campaign_id,
                    $sector_id,
                    $payload_json
                );
                """;

            insert.Parameters.AddWithValue(
                "$resolution_key",
                key.ToString());
            insert.Parameters.AddWithValue(
                "$operation_id",
                result.Outcome.OperationId);
            insert.Parameters.AddWithValue(
                "$mission_id",
                result.Outcome.MissionId.ToString("D"));
            insert.Parameters.AddWithValue(
                "$payload_schema_version",
                PayloadSchemaVersion);
            insert.Parameters.AddWithValue(
                "$completed_at_ms",
                result.Outcome.CompletedAt.ToUnixTimeMilliseconds());
            insert.Parameters.AddWithValue(
                "$saved_at_ms",
                savedAt.ToUnixTimeMilliseconds());
            insert.Parameters.AddWithValue(
                "$campaign_id",
                result.CampaignProgress.CampaignId);
            insert.Parameters.AddWithValue(
                "$sector_id",
                result.TerritoryPressure.State.SectorId);
            insert.Parameters.AddWithValue(
                "$payload_json",
                payload);

            try
            {
                await insert
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SqliteException ex)
                when (ex.SqliteErrorCode == 19)
            {
                throw new OperationConsequenceAlreadyExistsException(key);
            }

            var record = new OperationConsequenceStoreRecord(
                key,
                result,
                savedAt);
            record.Validate();

            transaction.Commit();

            _logger.LogInformation(
                "Persisted military operation consequence {ResolutionKey}.",
                key.ToString());

            return record;
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

            string? directory =
                Path.GetDirectoryName(_options.DatabasePath);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);

            await ExecutePragmaAsync(
                    connection,
                    "PRAGMA journal_mode = WAL;",
                    cancellationToken)
                .ConfigureAwait(false);

            await OpenCareerDatabaseMigrator
                .MigrateAsync(
                    connection,
                    cancellationToken)
                .ConfigureAwait(false);

            Volatile.Write(ref _initialized, 1);

            _logger.LogInformation(
                "Operation consequence store ready at {DatabasePath}, schema {SchemaVersion}.",
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

        var connection =
            new SqliteConnection(builder.ToString());

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ExecutePragmaAsync(
                connection,
                "PRAGMA foreign_keys = ON;",
                cancellationToken)
            .ConfigureAwait(false);

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

    private OperationConsequenceResult ReadResult(
        int payloadSchemaVersion,
        string json)
    {
        if (payloadSchemaVersion != PayloadSchemaVersion)
        {
            throw new NotSupportedException(
                $"Operation consequence payload schema {payloadSchemaVersion} is not supported.");
        }

        try
        {
            OperationConsequenceResult result =
                JsonSerializer.Deserialize<OperationConsequenceResult>(
                    json,
                    _jsonOptions)
                ?? throw new InvalidDataException(
                    "Stored operation consequence deserialized to null.");

            result.Validate();
            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Stored operation consequence is invalid.",
                ex);
        }
    }

    private static void ValidateKey(
        OperationResolutionKey key)
    {
        if (string.IsNullOrWhiteSpace(key.OperationId))
            throw new ArgumentException("Operation ID is required.", nameof(key));

        if (key.MissionId == Guid.Empty)
            throw new ArgumentException("Mission ID is required.", nameof(key));
    }
}
