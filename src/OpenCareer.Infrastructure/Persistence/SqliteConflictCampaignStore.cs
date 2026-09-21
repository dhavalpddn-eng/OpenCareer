using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Military;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteConflictCampaignStore : IConflictCampaignStore, IConflictCampaignRecoverySource
{
    private const int PayloadSchemaVersion = ConflictCampaignCheckpoint.CurrentSchemaVersion;

    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqliteConflictCampaignStore> _logger;
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

    public SqliteConflictCampaignStore(
        OpenCareerDatabaseOptions options,
        ILogger<SqliteConflictCampaignStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ConflictCampaignStoreRecord?> LoadAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT revision, checkpoint_schema_version, payload_json
            FROM conflict_campaigns
            WHERE campaign_id = $campaign_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$campaign_id", campaignId);

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        long revision = reader.GetInt64(0);
        var checkpoint = ReadCheckpoint(
            reader.GetInt32(1),
            reader.GetString(2));

        if (!string.Equals(checkpoint.CampaignId, campaignId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Stored conflict checkpoint campaign ID does not match its database key.");
        }

        var record = new ConflictCampaignStoreRecord(revision, checkpoint);
        record.Validate();
        return record;
    }

    public async Task<ConflictCampaignStoreRecord?> LoadMostRecentlySavedAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT campaign_id, revision, checkpoint_schema_version, payload_json
            FROM conflict_campaigns
            ORDER BY saved_at_ms DESC, campaign_id ASC
            LIMIT 1;
            """;

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        string campaignId = reader.GetString(0);
        long revision = reader.GetInt64(1);
        var checkpoint = ReadCheckpoint(
            reader.GetInt32(2),
            reader.GetString(3));

        if (!string.Equals(
            checkpoint.CampaignId,
            campaignId,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Stored conflict checkpoint campaign ID does not match its database key.");
        }

        var record = new ConflictCampaignStoreRecord(revision, checkpoint);
        record.Validate();
        return record;
    }

    public async Task<ConflictCampaignStoreRecord> SaveAsync(
        ConflictCampaignCheckpoint checkpoint,
        long? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        checkpoint.Validate();

        if (expectedRevision is < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using SqliteTransaction transaction = connection.BeginTransaction();

            string payload = JsonSerializer.Serialize(checkpoint, _jsonOptions);
            long newRevision;

            if (expectedRevision is null)
            {
                newRevision = 1;

                await using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    """
                    INSERT INTO conflict_campaigns (
                        campaign_id,
                        revision,
                        checkpoint_schema_version,
                        saved_at_ms,
                        world_updated_at_ms,
                        payload_json
                    )
                    VALUES (
                        $campaign_id,
                        $revision,
                        $checkpoint_schema_version,
                        $saved_at_ms,
                        $world_updated_at_ms,
                        $payload_json
                    )
                    ON CONFLICT(campaign_id) DO NOTHING;
                    """;

                AddParameters(insert, checkpoint, newRevision, payload);

                int inserted = await insert
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (inserted != 1)
                {
                    throw new ConflictCampaignConcurrencyException(
                        $"Conflict campaign '{checkpoint.CampaignId}' already exists.");
                }
            }
            else
            {
                newRevision = checked(expectedRevision.Value + 1);

                await using SqliteCommand update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText =
                    """
                    UPDATE conflict_campaigns
                    SET revision = $revision,
                        checkpoint_schema_version = $checkpoint_schema_version,
                        saved_at_ms = $saved_at_ms,
                        world_updated_at_ms = $world_updated_at_ms,
                        payload_json = $payload_json
                    WHERE campaign_id = $campaign_id
                      AND revision = $expected_revision;
                    """;

                AddParameters(update, checkpoint, newRevision, payload);
                update.Parameters.AddWithValue("$expected_revision", expectedRevision.Value);

                int updated = await update
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (updated != 1)
                {
                    throw new ConflictCampaignConcurrencyException(
                        $"Conflict campaign '{checkpoint.CampaignId}' changed since revision {expectedRevision.Value}.");
                }
            }

            transaction.Commit();

            _logger.LogInformation(
                "Saved conflict campaign {CampaignId} at revision {Revision}, world tick {WorldTick}.",
                checkpoint.CampaignId,
                newRevision,
                checkpoint.World.Tick);

            return new ConflictCampaignStoreRecord(newRevision, checkpoint);
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
                await OpenConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);

            await ExecutePragmaAsync(
                connection,
                "PRAGMA journal_mode = WAL;",
                cancellationToken).ConfigureAwait(false);

            await OpenCareerDatabaseMigrator
                .MigrateAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            Volatile.Write(ref _initialized, 1);

            _logger.LogInformation(
                "Conflict campaign store ready at {DatabasePath}, schema {SchemaVersion}.",
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

    private static void AddParameters(
        SqliteCommand command,
        ConflictCampaignCheckpoint checkpoint,
        long revision,
        string payload)
    {
        command.Parameters.AddWithValue("$campaign_id", checkpoint.CampaignId);
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue(
            "$checkpoint_schema_version",
            PayloadSchemaVersion);
        command.Parameters.AddWithValue(
            "$saved_at_ms",
            checkpoint.SavedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue(
            "$world_updated_at_ms",
            checkpoint.World.UpdatedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$payload_json", payload);
    }

    private ConflictCampaignCheckpoint ReadCheckpoint(
        int payloadSchemaVersion,
        string json)
    {
        if (payloadSchemaVersion != PayloadSchemaVersion)
        {
            throw new NotSupportedException(
                $"Conflict checkpoint payload schema {payloadSchemaVersion} is not supported.");
        }

        try
        {
            var checkpoint = JsonSerializer.Deserialize<ConflictCampaignCheckpoint>(
                json,
                _jsonOptions)
                ?? throw new InvalidDataException(
                    "Stored conflict checkpoint deserialized to null.");

            checkpoint.Validate();
            return checkpoint;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Stored conflict checkpoint is invalid.",
                ex);
        }
    }
}
