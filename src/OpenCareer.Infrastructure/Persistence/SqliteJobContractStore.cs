using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteJobContractStore : IJobContractStore
{
    private const int PayloadSchemaVersion = 1;

    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqliteJobContractStore> _logger;
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

    public SqliteJobContractStore(
        OpenCareerDatabaseOptions options,
        ILogger<SqliteJobContractStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PersistedJobContract?> ReadJobContractAsync(
        Guid contractId,
        CancellationToken cancellationToken = default)
    {
        if (contractId == Guid.Empty)
        {
            throw new ArgumentException(
                "Contract ID is required.",
                nameof(contractId));
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        return await ReadAsync(
                connection,
                transaction: null,
                contractId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JobContractSaveResult> CreateJobContractAsync(
        JobContract contract,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        contract.Validate();

        if (contract.Status != ContractStatus.Offered)
        {
            throw new InvalidOperationException(
                "A new persisted job contract must begin in Offered state.");
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            using SqliteTransaction transaction = connection.BeginTransaction();

            PersistedJobContract? existing =
                await ReadAsync(
                        connection,
                        transaction,
                        contract.ContractId,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (existing is not null)
            {
                if (existing.Contract == contract)
                    return JobContractSaveResult.AlreadySaved;

                throw new InvalidOperationException(
                    "A different job contract already exists under this contract ID.");
            }

            await InsertAsync(
                    connection,
                    transaction,
                    contract,
                    version: 0,
                    cancellationToken)
                .ConfigureAwait(false);

            transaction.Commit();
            return JobContractSaveResult.Created;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<JobContractSaveResult> UpdateJobContractAsync(
        JobContract contract,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        contract.Validate();

        if (expectedVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            using SqliteTransaction transaction = connection.BeginTransaction();

            PersistedJobContract existing =
                await ReadAsync(
                        connection,
                        transaction,
                        contract.ContractId,
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Job contract does not exist.");

            if (existing.Contract == contract)
                return JobContractSaveResult.AlreadySaved;

            if (existing.Version != expectedVersion)
            {
                throw new InvalidOperationException(
                    "Job contract changed before this update could be saved. Re-read and retry.");
            }

            if (!EquivalentImmutableTerms(existing.Contract, contract))
            {
                throw new InvalidOperationException(
                    "Persisted job-contract economic or dispatch terms are immutable.");
            }

            if (!AllowedTransition(
                    existing.Contract.Status,
                    contract.Status))
            {
                throw new InvalidOperationException(
                    $"Persisted job contract cannot transition from {existing.Contract.Status} to {contract.Status}.");
            }

            long nextVersion =
                checked(existing.Version + 1);

            await UpdateAsync(
                    connection,
                    transaction,
                    contract,
                    nextVersion,
                    expectedVersion,
                    cancellationToken)
                .ConfigureAwait(false);

            transaction.Commit();
            return JobContractSaveResult.Updated;
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
                "Job-contract SQLite storage ready at {DatabasePath}, schema {SchemaVersion}.",
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

    private async Task<PersistedJobContract?> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid contractId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT payload_schema_version, payload_json, version
            FROM job_contracts
            WHERE contract_id = $contract_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$contract_id",
            contractId.ToString("D"));

        await using SqliteDataReader reader =
            await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        int payloadSchemaVersion = reader.GetInt32(0);
        if (payloadSchemaVersion != PayloadSchemaVersion)
        {
            throw new NotSupportedException(
                $"Job-contract payload schema {payloadSchemaVersion} is not supported.");
        }

        JobContract persisted;
        try
        {
            persisted =
                JsonSerializer.Deserialize<JobContract>(
                    reader.GetString(1),
                    _jsonOptions)
                ?? throw new InvalidDataException(
                    "Stored job-contract payload deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Stored job-contract payload is invalid.",
                ex);
        }

        persisted.Validate();

        if (persisted.ContractId != contractId)
        {
            throw new InvalidDataException(
                "Persisted job-contract ID does not match its database key.");
        }

        var result =
            new PersistedJobContract(
                persisted,
                reader.GetInt64(2));

        result.Validate();
        return result;
    }

    private async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobContract contract,
        long version,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO job_contracts (
                contract_id,
                payload_schema_version,
                status,
                version,
                updated_at_ms,
                payload_json
            )
            VALUES (
                $contract_id,
                $payload_schema_version,
                $status,
                $version,
                $updated_at_ms,
                $payload_json
            );
            """;

        AddContractParameters(
            command,
            contract,
            version);

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task UpdateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JobContract contract,
        long version,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE job_contracts
            SET payload_schema_version = $payload_schema_version,
                status = $status,
                version = $version,
                updated_at_ms = $updated_at_ms,
                payload_json = $payload_json
            WHERE contract_id = $contract_id
              AND version = $expected_version;
            """;

        AddContractParameters(
            command,
            contract,
            version);

        command.Parameters.AddWithValue(
            "$expected_version",
            expectedVersion);

        int affected =
            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException(
                "Job-contract optimistic-concurrency update failed.");
        }
    }

    private void AddContractParameters(
        SqliteCommand command,
        JobContract contract,
        long version)
    {
        command.Parameters.AddWithValue(
            "$contract_id",
            contract.ContractId.ToString("D"));
        command.Parameters.AddWithValue(
            "$payload_schema_version",
            PayloadSchemaVersion);
        command.Parameters.AddWithValue(
            "$status",
            (int)contract.Status);
        command.Parameters.AddWithValue(
            "$version",
            version);
        command.Parameters.AddWithValue(
            "$updated_at_ms",
            ContractUpdatedAt(contract).ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue(
            "$payload_json",
            JsonSerializer.Serialize(
                contract,
                _jsonOptions));
    }

    private static bool EquivalentImmutableTerms(
        JobContract left,
        JobContract right) =>
        left.ContractId == right.ContractId
        && left.EmployerId == right.EmployerId
        && left.Kind == right.Kind
        && left.ServiceTrack == right.ServiceTrack
        && string.Equals(
            left.OriginIcao,
            right.OriginIcao,
            StringComparison.Ordinal)
        && string.Equals(
            left.DestinationIcao,
            right.DestinationIcao,
            StringComparison.Ordinal)
        && left.Compensation == right.Compensation
        && left.OfferedAt == right.OfferedAt
        && left.MustStartBy == right.MustStartBy
        && left.MustCompleteBy == right.MustCompleteBy
        && left.AircraftRequirements == right.AircraftRequirements
        && left.ReputationReward.Equals(right.ReputationReward)
        && left.ReputationPenalty.Equals(right.ReputationPenalty)
        && string.Equals(
            left.MarketId,
            right.MarketId,
            StringComparison.Ordinal)
        && string.Equals(
            left.WorldEventId,
            right.WorldEventId,
            StringComparison.Ordinal)
        && left.ProviderAircraft
            == right.ProviderAircraft
        && left.GovernmentAuthorizationRequired
            == right.GovernmentAuthorizationRequired;

    private static bool AllowedTransition(
        ContractStatus from,
        ContractStatus to) =>
        (from, to) switch
        {
            (ContractStatus.Offered, ContractStatus.Accepted) => true,
            (ContractStatus.Offered, ContractStatus.Cancelled) => true,
            (ContractStatus.Offered, ContractStatus.Expired) => true,
            (ContractStatus.Accepted, ContractStatus.InProgress) => true,
            (ContractStatus.Accepted, ContractStatus.Failed) => true,
            (ContractStatus.Accepted, ContractStatus.Cancelled) => true,
            (ContractStatus.InProgress, ContractStatus.Completed) => true,
            (ContractStatus.InProgress, ContractStatus.Failed) => true,
            _ => false
        };

    private static DateTimeOffset ContractUpdatedAt(
        JobContract contract) =>
        contract.CompletedAt
        ?? contract.StartedAt
        ?? contract.AcceptedAt
        ?? contract.OfferedAt;

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
}
