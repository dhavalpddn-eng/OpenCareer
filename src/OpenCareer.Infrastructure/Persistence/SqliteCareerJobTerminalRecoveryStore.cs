using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using OpenCareer.Application.Careers;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteCareerJobTerminalRecoveryStore
    : ICareerJobTerminalRecoveryStore
{
    private const int PayloadSchemaVersion = 1;

    private readonly OpenCareerDatabaseOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
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

    public SqliteCareerJobTerminalRecoveryStore(
        OpenCareerDatabaseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<CareerJobPlayableCompletionRequest?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await OpenCareerDatabaseMigrator
                .MigrateAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            return await ReadCoreAsync(connection, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        CareerJobPlayableCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        string payload = JsonSerializer.Serialize(request, _jsonOptions);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await OpenCareerDatabaseMigrator
                .MigrateAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            await using SqliteCommand existing = connection.CreateCommand();
            existing.CommandText =
                "SELECT contract_id, payload_json FROM career_terminal_recovery WHERE slot_id = 1;";

            await using SqliteDataReader reader =
                await existing.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string existingContractId = reader.GetString(0);
                string existingPayload = reader.GetString(1);

                if (string.Equals(
                        existingContractId,
                        request.ContractId.ToString("D"),
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existingPayload, payload, StringComparison.Ordinal))
                {
                    return;
                }

                throw new InvalidOperationException(
                    "A different career terminal workflow is already pending recovery.");
            }

            await reader.DisposeAsync().ConfigureAwait(false);

            await using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO career_terminal_recovery (
                    slot_id,
                    contract_id,
                    payload_schema_version,
                    payload_json
                )
                VALUES (1, $contract_id, $payload_schema_version, $payload_json);
                """;
            insert.Parameters.AddWithValue(
                "$contract_id",
                request.ContractId.ToString("D"));
            insert.Parameters.AddWithValue(
                "$payload_schema_version",
                PayloadSchemaVersion);
            insert.Parameters.AddWithValue("$payload_json", payload);

            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(
        Guid contractId,
        CancellationToken cancellationToken = default)
    {
        if (contractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(contractId));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await OpenCareerDatabaseMigrator
                .MigrateAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "DELETE FROM career_terminal_recovery WHERE slot_id = 1 AND contract_id = $contract_id;";
            command.Parameters.AddWithValue("$contract_id", contractId.ToString("D"));

            int affected =
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (affected == 0
                && await ReadCoreAsync(connection, cancellationToken).ConfigureAwait(false)
                    is not null)
            {
                throw new InvalidOperationException(
                    "Pending career terminal workflow belongs to a different contract.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CareerJobPlayableCompletionRequest?> ReadCoreAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT contract_id, payload_schema_version, payload_json
            FROM career_terminal_recovery
            WHERE slot_id = 1;
            """;

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        int schemaVersion = reader.GetInt32(1);
        if (schemaVersion != PayloadSchemaVersion)
        {
            throw new NotSupportedException(
                $"Career terminal recovery payload schema {schemaVersion} is not supported.");
        }

        CareerJobPlayableCompletionRequest request;
        try
        {
            request =
                JsonSerializer.Deserialize<CareerJobPlayableCompletionRequest>(
                    reader.GetString(2),
                    _jsonOptions)
                ?? throw new InvalidDataException(
                    "Stored career terminal recovery payload deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Stored career terminal recovery payload is invalid.",
                ex);
        }

        request.Validate();

        if (!Guid.TryParse(reader.GetString(0), out Guid contractId)
            || contractId != request.ContractId)
        {
            throw new InvalidDataException(
                "Stored career terminal recovery contract identity does not match its payload.");
        }

        return request;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_options.DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText =
            "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return connection;
    }
}
