using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqlitePlayerCareerProfileStore
    : IPlayerCareerProfileStore
{
    private const int PayloadSchemaVersion = 1;
    private const long SlotId = 1;

    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqlitePlayerCareerProfileStore> _logger;
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

    public SqlitePlayerCareerProfileStore(
        OpenCareerDatabaseOptions options,
        ILogger<SqlitePlayerCareerProfileStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PlayerCareerProfileStoreRecord?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT career_id, revision, payload_schema_version, saved_at_ms, payload_json, saved_at_utc_ticks
            FROM player_career_profile
            WHERE slot_id = $slot_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$slot_id", SlotId);

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        string storedCareerId = reader.GetString(0);
        long revision = reader.GetInt64(1);
        int payloadSchemaVersion = reader.GetInt32(2);
        long savedAtMs = reader.GetInt64(3);
        string payload = reader.GetString(4);

        PlayerCareerProfile profile =
            ReadProfile(payloadSchemaVersion, payload);

        if (!string.Equals(
                storedCareerId,
                profile.CareerId.ToString("D"),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Stored player career profile identity does not match its payload.");
        }

        DateTimeOffset persistedSavedAt = reader.IsDBNull(5)
            ? RestoreSavedAtPrecision(DateTimeOffset.FromUnixTimeMilliseconds(savedAtMs), profile.CreatedAt)
            : new DateTimeOffset(reader.GetInt64(5), TimeSpan.Zero);

        if (!reader.IsDBNull(5) && persistedSavedAt.ToUnixTimeMilliseconds() != savedAtMs)
            throw new InvalidDataException("Player career save timestamp metadata is inconsistent.");

        var record = new PlayerCareerProfileStoreRecord(
            revision,
            profile,
            persistedSavedAt);
        record.Validate();
        return record;
    }

    public async Task<PlayerCareerProfileStoreRecord> SaveAsync(
        PlayerCareerProfile profile,
        long? expectedRevision,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();

        if (savedAt == default || savedAt < profile.CreatedAt)
            throw new ArgumentOutOfRangeException(nameof(savedAt));

        if (expectedRevision is < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));

        // The logbook's authoritative commit retains sub-millisecond precision. Truncating this
        // marker can falsely place a successfully applied profile before that commit.
        DateTimeOffset persistedSavedAt = savedAt.ToUniversalTime();

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using SqliteTransaction transaction = connection.BeginTransaction();

            string payload = JsonSerializer.Serialize(profile, _jsonOptions);
            string careerId = profile.CareerId.ToString("D");
            long newRevision;

            if (expectedRevision is null)
            {
                newRevision = 1;

                await using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    """
                    INSERT INTO player_career_profile (
                        slot_id,
                        career_id,
                        revision,
                        payload_schema_version,
                        saved_at_ms,
                        saved_at_utc_ticks,
                        payload_json
                    )
                    VALUES (
                        $slot_id,
                        $career_id,
                        $revision,
                        $payload_schema_version,
                        $saved_at_ms,
                        $saved_at_utc_ticks,
                        $payload_json
                    )
                    ON CONFLICT(slot_id) DO NOTHING;
                    """;

                AddParameters(
                    insert,
                    careerId,
                    newRevision,
                    persistedSavedAt,
                    payload);

                int inserted = await insert
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (inserted != 1)
                {
                    throw new PlayerCareerProfileConcurrencyException(
                        "Player career profile already exists.");
                }
            }
            else
            {
                newRevision = checked(expectedRevision.Value + 1);

                await using SqliteCommand update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText =
                    """
                    UPDATE player_career_profile
                    SET revision = $revision,
                        payload_schema_version = $payload_schema_version,
                        saved_at_ms = $saved_at_ms,
                        saved_at_utc_ticks = $saved_at_utc_ticks,
                        payload_json = $payload_json
                    WHERE slot_id = $slot_id
                      AND career_id = $career_id
                      AND revision = $expected_revision;
                    """;

                AddParameters(
                    update,
                    careerId,
                    newRevision,
                    persistedSavedAt,
                    payload);
                update.Parameters.AddWithValue(
                    "$expected_revision",
                    expectedRevision.Value);

                int updated = await update
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (updated != 1)
                {
                    throw new PlayerCareerProfileConcurrencyException(
                        $"Player career profile changed since revision {expectedRevision.Value} or its identity does not match.");
                }
            }

            transaction.Commit();

            var record = new PlayerCareerProfileStoreRecord(
                newRevision,
                profile,
                persistedSavedAt);
            record.Validate();

            _logger.LogInformation(
                "Saved player career profile {CareerId} at revision {Revision}.",
                profile.CareerId,
                newRevision);

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
                    cancellationToken)
                .ConfigureAwait(false);

            await OpenCareerDatabaseMigrator
                .MigrateAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            Volatile.Write(ref _initialized, 1);

            _logger.LogInformation(
                "Player career profile store ready at {DatabasePath}, schema {SchemaVersion}.",
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
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static DateTimeOffset RestoreSavedAtPrecision(
        DateTimeOffset persistedSavedAt,
        DateTimeOffset createdAt)
    {
        if (persistedSavedAt >= createdAt)
            return persistedSavedAt;

        TimeSpan precisionLoss =
            createdAt - persistedSavedAt;

        return precisionLoss < TimeSpan.FromMilliseconds(1)
            ? createdAt
            : persistedSavedAt;
    }

    private static void AddParameters(
        SqliteCommand command,
        string careerId,
        long revision,
        DateTimeOffset savedAt,
        string payload)
    {
        command.Parameters.AddWithValue("$slot_id", SlotId);
        command.Parameters.AddWithValue("$career_id", careerId);
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue(
            "$payload_schema_version",
            PayloadSchemaVersion);
        command.Parameters.AddWithValue(
            "$saved_at_ms",
            savedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$saved_at_utc_ticks", savedAt.UtcTicks);
        command.Parameters.AddWithValue("$payload_json", payload);
    }

    private PlayerCareerProfile ReadProfile(
        int payloadSchemaVersion,
        string json)
    {
        if (payloadSchemaVersion != PayloadSchemaVersion)
        {
            throw new NotSupportedException(
                $"Player career profile payload schema {payloadSchemaVersion} is not supported.");
        }

        try
        {
            PlayerCareerProfile profile =
                JsonSerializer.Deserialize<PlayerCareerProfile>(
                    json,
                    _jsonOptions)
                ?? throw new InvalidDataException(
                    "Stored player career profile deserialized to null.");

            profile.Validate();
            return profile;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Stored player career profile is invalid.",
                ex);
        }
    }
}
