using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using OpenCareer.Application.Planning;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqlitePreparedDispatchPlanStore(
    OpenCareerDatabaseOptions options)
    : IPreparedDispatchPlanStore
{
    private const int PayloadSchemaVersion = 1;

    private readonly OpenCareerDatabaseOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));
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

    public async Task<PreparedDispatchPlan?> ReadAsync(
        Guid contractId,
        CancellationToken cancellationToken = default)
    {
        ValidateContractId(contractId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await OpenAsync(
                    cancellationToken)
                .ConfigureAwait(false);

            return await ReadCoreAsync(
                    connection,
                    contractId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PreparedDispatchPlanSaveResult> SaveAsync(
        PreparedDispatchPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();
        string payload = JsonSerializer.Serialize(plan, _jsonOptions);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await OpenAsync(
                    cancellationToken)
                .ConfigureAwait(false);

            PreparedDispatchPlan? existing = await ReadCoreAsync(
                    connection,
                    plan.ContractId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing == plan)
                return PreparedDispatchPlanSaveResult.AlreadySaved;

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO prepared_dispatch_plans (
                    contract_id,
                    payload_schema_version,
                    prepared_at_utc_ticks,
                    payload_json
                )
                VALUES (
                    $contract_id,
                    $payload_schema_version,
                    $prepared_at_utc_ticks,
                    $payload_json
                )
                ON CONFLICT (contract_id) DO UPDATE SET
                    payload_schema_version = excluded.payload_schema_version,
                    prepared_at_utc_ticks = excluded.prepared_at_utc_ticks,
                    payload_json = excluded.payload_json;
                """;
            command.Parameters.AddWithValue(
                "$contract_id",
                plan.ContractId.ToString("D"));
            command.Parameters.AddWithValue(
                "$payload_schema_version",
                PayloadSchemaVersion);
            command.Parameters.AddWithValue(
                "$prepared_at_utc_ticks",
                plan.PreparedAt.UtcTicks);
            command.Parameters.AddWithValue("$payload_json", payload);

            await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            return existing is null
                ? PreparedDispatchPlanSaveResult.Created
                : PreparedDispatchPlanSaveResult.Replaced;
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
        ValidateContractId(contractId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await OpenAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "DELETE FROM prepared_dispatch_plans WHERE contract_id = $contract_id;";
            command.Parameters.AddWithValue(
                "$contract_id",
                contractId.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> OpenAsync(
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

        await OpenCareerDatabaseMigrator
            .MigrateAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        return connection;
    }

    private async Task<PreparedDispatchPlan?> ReadCoreAsync(
        SqliteConnection connection,
        Guid contractId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT payload_schema_version, prepared_at_utc_ticks, payload_json
            FROM prepared_dispatch_plans
            WHERE contract_id = $contract_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$contract_id",
            contractId.ToString("D"));

        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        int schemaVersion = reader.GetInt32(0);
        if (schemaVersion != PayloadSchemaVersion)
        {
            throw new NotSupportedException(
                $"Prepared dispatch-plan payload schema {schemaVersion} is not supported.");
        }

        PreparedDispatchPlan plan;
        try
        {
            plan = JsonSerializer.Deserialize<PreparedDispatchPlan>(
                    reader.GetString(2),
                    _jsonOptions)
                ?? throw new InvalidDataException(
                    "Stored prepared dispatch plan deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Stored prepared dispatch plan is invalid.",
                ex);
        }

        plan.Validate();
        if (plan.ContractId != contractId
            || plan.PreparedAt.UtcTicks != reader.GetInt64(1))
        {
            throw new InvalidDataException(
                "Stored prepared dispatch-plan identity or timestamp does not match its index columns.");
        }

        return plan;
    }

    private static void ValidateContractId(Guid contractId)
    {
        if (contractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(contractId));
    }
}
