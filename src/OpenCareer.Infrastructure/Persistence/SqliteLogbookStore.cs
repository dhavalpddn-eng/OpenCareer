using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteLogbookStore : ILogbookSource, ILogbookWriter, ILogbookIdempotencySource
{
    private const int PayloadSchemaVersion = 1;

    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqliteLogbookStore> _logger;
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

    public SqliteLogbookStore(
        OpenCareerDatabaseOptions options,
        ILogger<SqliteLogbookStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<LogbookEntry>> QueryAsync(
        LogbookQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        var sql = new StringBuilder(
            """
            SELECT payload_schema_version, payload_json
            FROM logbook_entries
            WHERE 1 = 1
            """);

        if (query.From is { } from)
        {
            sql.AppendLine(" AND ended_at_ms >= $from_ms");
            command.Parameters.AddWithValue("$from_ms", from.ToUnixTimeMilliseconds());
        }

        if (query.To is { } to)
        {
            sql.AppendLine(" AND ended_at_ms <= $to_ms");
            command.Parameters.AddWithValue("$to_ms", to.ToUnixTimeMilliseconds());
        }

        if (query.EntryKind is { } entryKind)
        {
            sql.AppendLine(" AND entry_kind = $entry_kind");
            command.Parameters.AddWithValue("$entry_kind", (int)entryKind);
        }

        if (query.SafetyOutcome is { } safetyOutcome)
        {
            sql.AppendLine(" AND safety_outcome = $safety_outcome");
            command.Parameters.AddWithValue("$safety_outcome", (int)safetyOutcome);
        }

        if (query.MissionOutcome is { } missionOutcome)
        {
            sql.AppendLine(" AND mission_outcome = $mission_outcome");
            command.Parameters.AddWithValue("$mission_outcome", (int)missionOutcome);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            sql.AppendLine(" AND instr(search_text, $search_text) > 0");
            command.Parameters.AddWithValue(
                "$search_text",
                query.SearchText.Trim().ToLowerInvariant());
        }

        sql.AppendLine(" ORDER BY ended_at_ms DESC, entry_id ASC");
        sql.AppendLine(" LIMIT $limit");
        command.Parameters.AddWithValue("$limit", query.Limit);
        command.CommandText = sql.ToString();

        var entries = new List<LogbookEntry>();

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(ReadEntry(
                reader.GetInt32(0),
                reader.GetString(1)));
        }

        return entries;
    }

    public async Task<LogbookEntry?> GetAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId == Guid.Empty)
            throw new ArgumentException("Logbook entry id is required.", nameof(entryId));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT payload_schema_version, payload_json
            FROM logbook_entries
            WHERE entry_id = $entry_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$entry_id", entryId.ToString("N"));

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return ReadEntry(
            reader.GetInt32(0),
            reader.GetString(1));
    }

    public async Task<LogbookEntry?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException(
                "Idempotency key is required.",
                nameof(idempotencyKey));
        }

        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);

        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT payload_schema_version, payload_json
            FROM logbook_entries
            WHERE idempotency_key = $idempotency_key
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$idempotency_key",
            idempotencyKey);

        await using SqliteDataReader reader =
            await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        return ReadEntry(
            reader.GetInt32(0),
            reader.GetString(1));
    }

    public async Task<LogbookAppendResult> TryAppendAsync(
        LogbookEntry entry,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException(
                "Idempotency key is required.",
                nameof(idempotencyKey));
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using SqliteTransaction transaction = connection.BeginTransaction();

            string payload = JsonSerializer.Serialize(entry, _jsonOptions);

            await using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO logbook_entries (
                    entry_id,
                    idempotency_key,
                    payload_schema_version,
                    committed_at_ms,
                    ended_at_ms,
                    entry_kind,
                    safety_outcome,
                    mission_outcome,
                    aircraft_name,
                    departure,
                    arrival,
                    contract_id,
                    search_text,
                    payload_json
                )
                VALUES (
                    $entry_id,
                    $idempotency_key,
                    $payload_schema_version,
                    $committed_at_ms,
                    $ended_at_ms,
                    $entry_kind,
                    $safety_outcome,
                    $mission_outcome,
                    $aircraft_name,
                    $departure,
                    $arrival,
                    $contract_id,
                    $search_text,
                    $payload_json
                )
                ON CONFLICT(idempotency_key) DO NOTHING;
                """;

            AddEntryParameters(insert, entry, idempotencyKey, payload);

            int inserted = await insert
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            if (inserted == 1)
            {
                transaction.Commit();

                _logger.LogInformation(
                    "Committed logbook entry {EntryId} for debrief {DebriefId}.",
                    entry.EntryId,
                    entry.Debrief.DebriefId);

                return new(
                    LogbookAppendDisposition.Appended,
                    entry);
            }

            await using SqliteCommand existingCommand = connection.CreateCommand();
            existingCommand.Transaction = transaction;
            existingCommand.CommandText =
                """
                SELECT payload_schema_version, payload_json
                FROM logbook_entries
                WHERE idempotency_key = $idempotency_key
                LIMIT 1;
                """;
            existingCommand.Parameters.AddWithValue(
                "$idempotency_key",
                idempotencyKey);

            await using SqliteDataReader reader = await existingCommand
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "SQLite reported an idempotency conflict but the existing logbook entry could not be read.");
            }

            LogbookEntry existing = ReadEntry(
                reader.GetInt32(0),
                reader.GetString(1));

            if (existing.Debrief.DebriefId != entry.Debrief.DebriefId)
            {
                throw new InvalidOperationException(
                    $"Logbook idempotency key '{idempotencyKey}' is already associated with another debrief.");
            }

            transaction.Commit();

            return new(
                LogbookAppendDisposition.AlreadyExists,
                existing);
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
                "OpenCareer SQLite database ready at {DatabasePath}, schema {SchemaVersion}.",
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

    private void AddEntryParameters(
        SqliteCommand command,
        LogbookEntry entry,
        string idempotencyKey,
        string payload)
    {
        FlightDebrief debrief = entry.Debrief;

        command.Parameters.AddWithValue("$entry_id", entry.EntryId.ToString("N"));
        command.Parameters.AddWithValue("$idempotency_key", idempotencyKey);
        command.Parameters.AddWithValue("$payload_schema_version", PayloadSchemaVersion);
        command.Parameters.AddWithValue(
            "$committed_at_ms",
            entry.CommittedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue(
            "$ended_at_ms",
            debrief.EndedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$entry_kind", (int)debrief.EntryKind);
        command.Parameters.AddWithValue("$safety_outcome", (int)debrief.SafetyOutcome);
        command.Parameters.AddWithValue("$mission_outcome", (int)debrief.MissionOutcome);
        command.Parameters.AddWithValue("$aircraft_name", debrief.Aircraft.DisplayName);
        command.Parameters.AddWithValue(
            "$departure",
            (object?)(debrief.Route.ActualDeparture ??
                      debrief.Route.PlannedOrigin) ??
            DBNull.Value);
        command.Parameters.AddWithValue(
            "$arrival",
            (object?)(debrief.Route.ActualArrival ??
                      debrief.Route.PlannedDestination) ??
            DBNull.Value);
        command.Parameters.AddWithValue(
            "$contract_id",
            debrief.ContractId is { } contractId
                ? contractId.ToString("N")
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$search_text",
            BuildSearchText(debrief));
        command.Parameters.AddWithValue("$payload_json", payload);
    }

    private static string BuildSearchText(FlightDebrief debrief)
    {
        IEnumerable<string?> values =
        [
            debrief.Aircraft.DisplayName,
            debrief.Aircraft.Family,
            debrief.Aircraft.TailNumber,
            debrief.Route.PlannedOrigin,
            debrief.Route.PlannedDestination,
            debrief.Route.ActualDeparture,
            debrief.Route.ActualArrival,
            debrief.Route.DiversionLocation,
            debrief.Payload.CargoDescription,
            debrief.Payload.Outcome
        ];

        IEnumerable<string?> eventValues = debrief.Events
            .SelectMany(static item => new string?[] { item.Category, item.Text });

        return string.Join(
                '\n',
                values
                    .Concat(eventValues)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value!.Trim()))
            .ToLowerInvariant();
    }

    private LogbookEntry ReadEntry(
        int payloadSchemaVersion,
        string json)
    {
        if (payloadSchemaVersion != PayloadSchemaVersion)
        {
            throw new NotSupportedException(
                $"Logbook payload schema {payloadSchemaVersion} is not supported.");
        }

        try
        {
            return JsonSerializer.Deserialize<LogbookEntry>(
                       json,
                       _jsonOptions) ??
                   throw new InvalidDataException(
                       "Stored logbook payload deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Stored logbook payload is invalid.",
                ex);
        }
    }
}
