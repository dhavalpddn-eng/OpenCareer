using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Military;
using OpenCareer.Domain.Military;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteMilitaryCareerProfileStore
    : IMilitaryCareerProfileStore
{
    private const int PayloadSchemaVersion = 1;
    private const long SlotId = 1;

    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqliteMilitaryCareerProfileStore> _logger;
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

    public SqliteMilitaryCareerProfileStore(
        OpenCareerDatabaseOptions options,
        ILogger<SqliteMilitaryCareerProfileStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MilitaryCareerProfileStoreRecord?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT revision, payload_schema_version, saved_at_ms, payload_json
            FROM military_career_profile
            WHERE slot_id = $slot_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$slot_id", SlotId);

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        long revision = reader.GetInt64(0);
        int payloadSchemaVersion = reader.GetInt32(1);
        long savedAtMs = reader.GetInt64(2);
        string payload = reader.GetString(3);

        MilitaryCareerState career =
            ReadCareer(payloadSchemaVersion, payload);

        var record = new MilitaryCareerProfileStoreRecord(
            revision,
            career,
            DateTimeOffset.FromUnixTimeMilliseconds(savedAtMs));
        record.Validate();
        return record;
    }

    public async Task<MilitaryCareerProfileStoreRecord> SaveAsync(
        MilitaryCareerState career,
        long? expectedRevision,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(career);
        career.Validate();

        if (expectedRevision is < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using SqliteTransaction transaction = connection.BeginTransaction();

            string payload = JsonSerializer.Serialize(career, _jsonOptions);
            long newRevision;

            if (expectedRevision is null)
            {
                newRevision = 1;

                await using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    """
                    INSERT INTO military_career_profile (
                        slot_id,
                        revision,
                        payload_schema_version,
                        saved_at_ms,
                        payload_json
                    )
                    VALUES (
                        $slot_id,
                        $revision,
                        $payload_schema_version,
                        $saved_at_ms,
                        $payload_json
                    )
                    ON CONFLICT(slot_id) DO NOTHING;
                    """;

                AddParameters(
                    insert,
                    newRevision,
                    savedAt,
                    payload);

                int inserted = await insert
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (inserted != 1)
                {
                    throw new MilitaryCareerProfileConcurrencyException(
                        "Military career profile already exists.");
                }
            }
            else
            {
                newRevision = checked(expectedRevision.Value + 1);

                await using SqliteCommand update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText =
                    """
                    UPDATE military_career_profile
                    SET revision = $revision,
                        payload_schema_version = $payload_schema_version,
                        saved_at_ms = $saved_at_ms,
                        payload_json = $payload_json
                    WHERE slot_id = $slot_id
                      AND revision = $expected_revision;
                    """;

                AddParameters(
                    update,
                    newRevision,
                    savedAt,
                    payload);
                update.Parameters.AddWithValue(
                    "$expected_revision",
                    expectedRevision.Value);

                int updated = await update
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (updated != 1)
                {
                    throw new MilitaryCareerProfileConcurrencyException(
                        $"Military career profile changed since revision {expectedRevision.Value}.");
                }
            }

            transaction.Commit();

            var record = new MilitaryCareerProfileStoreRecord(
                newRevision,
                career,
                savedAt);
            record.Validate();

            _logger.LogInformation(
                "Saved military career profile at revision {Revision}.",
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
                "Military career profile store ready at {DatabasePath}, schema {SchemaVersion}.",
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

    private static void AddParameters(
        SqliteCommand command,
        long revision,
        DateTimeOffset savedAt,
        string payload)
    {
        command.Parameters.AddWithValue("$slot_id", SlotId);
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue(
            "$payload_schema_version",
            PayloadSchemaVersion);
        command.Parameters.AddWithValue(
            "$saved_at_ms",
            savedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$payload_json", payload);
    }

    private MilitaryCareerState ReadCareer(
        int payloadSchemaVersion,
        string json)
    {
        if (payloadSchemaVersion != PayloadSchemaVersion)
        {
            throw new NotSupportedException(
                $"Military career profile payload schema {payloadSchemaVersion} is not supported.");
        }

        try
        {
            MilitaryCareerState career =
                JsonSerializer.Deserialize<MilitaryCareerState>(
                    json,
                    _jsonOptions)
                ?? throw new InvalidDataException(
                    "Stored military career profile deserialized to null.");

            career.Validate();
            return career;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Stored military career profile is invalid.",
                ex);
        }
    }
}
