using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteJobBoardStateStore : IJobBoardStateStore
{
    private const int PayloadSchemaVersion = 1;

    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqliteJobBoardStateStore> _logger;
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

    public SqliteJobBoardStateStore(
        OpenCareerDatabaseOptions options,
        ILogger<SqliteJobBoardStateStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SaveAsync(
        JobBoardState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            using SqliteTransaction transaction =
                connection.BeginTransaction();

            JobBoardState? existing =
                await ReadStateAsync(
                        connection,
                        transaction,
                        state.AirportIcao,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (existing is not null)
            {
                if (state.UpdatedAt < existing.UpdatedAt)
                {
                    _logger.LogDebug(
                        "Ignored stale job-board state for {AirportIcao} at {UpdatedAt}.",
                        state.AirportIcao,
                        state.UpdatedAt);

                    return;
                }

                ImmutableHashSet<Guid> retired =
                    existing.RetiredOfferIds
                        .Union(state.RetiredOfferIds);

                ImmutableArray<JobMarketOfferDraft> offers =
                    state.Offers
                        .Where(
                            offer =>
                                !retired.Contains(
                                    offer.OfferId))
                        .ToImmutableArray();

                state =
                    state with
                    {
                        Offers = offers,
                        RetiredOfferIds = retired
                    };

                state.Validate();
            }

            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText =
                """
                INSERT INTO job_board_states (
                    airport_icao,
                    payload_schema_version,
                    updated_at_ms,
                    payload_json
                )
                VALUES (
                    $airport_icao,
                    $payload_schema_version,
                    $updated_at_ms,
                    $payload_json
                )
                ON CONFLICT(airport_icao) DO UPDATE SET
                    payload_schema_version = excluded.payload_schema_version,
                    updated_at_ms = excluded.updated_at_ms,
                    payload_json = excluded.payload_json
                WHERE excluded.updated_at_ms >= job_board_states.updated_at_ms;
                """;

            command.Parameters.AddWithValue(
                "$airport_icao",
                state.AirportIcao);
            command.Parameters.AddWithValue(
                "$payload_schema_version",
                PayloadSchemaVersion);
            command.Parameters.AddWithValue(
                "$updated_at_ms",
                state.UpdatedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue(
                "$payload_json",
                JsonSerializer.Serialize(
                    state,
                    _jsonOptions));

            int affected =
                await command
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (affected == 0)
            {
                _logger.LogDebug(
                    "Ignored stale job-board state for {AirportIcao} at {UpdatedAt}.",
                    state.AirportIcao,
                    state.UpdatedAt);

                return;
            }

            transaction.Commit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<JobBoardState?> ReadStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string airportIcao,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT payload_schema_version, payload_json
            FROM job_board_states
            WHERE airport_icao = $airport_icao
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$airport_icao",
            airportIcao);

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

        return ReadState(
            reader.GetInt32(0),
            reader.GetString(1));
    }

    public async Task<JobBoardState?> GetAsync(
        string airportIcao,
        CancellationToken cancellationToken = default)
    {
        string normalizedIcao = NormalizeIcao(airportIcao);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT payload_schema_version, payload_json
            FROM job_board_states
            WHERE airport_icao = $airport_icao
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$airport_icao", normalizedIcao);

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return ReadState(
            reader.GetInt32(0),
            reader.GetString(1));
    }

    public async Task<IReadOnlyList<JobBoardState>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT payload_schema_version, payload_json
            FROM job_board_states
            ORDER BY airport_icao COLLATE BINARY ASC;
            """;

        var states = new List<JobBoardState>();

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            states.Add(
                ReadState(
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
                "Job-board SQLite storage ready at {DatabasePath}, schema {SchemaVersion}.",
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
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private JobBoardState ReadState(
        int payloadSchemaVersion,
        string json)
    {
        if (payloadSchemaVersion != PayloadSchemaVersion)
        {
            throw new NotSupportedException(
                $"Job-board payload schema {payloadSchemaVersion} is not supported.");
        }

        try
        {
            JobBoardState state =
                JsonSerializer.Deserialize<JobBoardState>(
                    json,
                    _jsonOptions)
                ?? throw new InvalidDataException(
                    "Stored job-board payload deserialized to null.");

            state.Validate();
            return state;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Stored job-board payload is invalid.",
                ex);
        }
    }

    private static string NormalizeIcao(
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string normalized =
            value.Trim().ToUpperInvariant();

        if (normalized.Length != 4
            || normalized.Any(c => c is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "A four-letter ICAO airport identifier is required.",
                nameof(value));
        }

        return normalized;
    }
}
