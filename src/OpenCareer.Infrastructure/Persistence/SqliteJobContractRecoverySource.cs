using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteJobContractRecoverySource
    : IJobContractRecoverySource
{
    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqliteJobContractRecoverySource> _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private int _initialized;

    public SqliteJobContractRecoverySource(
        OpenCareerDatabaseOptions options,
        ILogger<SqliteJobContractRecoverySource> logger)
    {
        _options =
            options
            ?? throw new ArgumentNullException(nameof(options));
        _logger =
            logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<JobContractRecoveryCandidate>>
        ReadRecoveryCandidatesAsync(
            CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);

        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                contract_id,
                status,
                version,
                updated_at_ms
            FROM job_contracts
            WHERE status IN (
                $accepted,
                $in_progress,
                $completed
            )
            ORDER BY
                updated_at_ms DESC,
                contract_id ASC;
            """;

        command.Parameters.AddWithValue(
            "$accepted",
            (int)ContractStatus.Accepted);
        command.Parameters.AddWithValue(
            "$in_progress",
            (int)ContractStatus.InProgress);
        command.Parameters.AddWithValue(
            "$completed",
            (int)ContractStatus.Completed);

        var candidates =
            new List<JobContractRecoveryCandidate>();

        await using SqliteDataReader reader =
            await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        while (await reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            string contractIdText =
                reader.GetString(0);

            if (!Guid.TryParse(
                    contractIdText,
                    out Guid contractId))
            {
                throw new InvalidDataException(
                    "Persisted recoverable job-contract ID is invalid.");
            }

            var status =
                (ContractStatus)reader.GetInt32(1);

            DateTimeOffset updatedAt;
            try
            {
                updatedAt =
                    DateTimeOffset.FromUnixTimeMilliseconds(
                        reader.GetInt64(3));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new InvalidDataException(
                    "Persisted recoverable job-contract timestamp is invalid.",
                    ex);
            }

            var candidate =
                new JobContractRecoveryCandidate(
                    contractId,
                    status,
                    reader.GetInt64(2),
                    updatedAt);

            candidate.Validate();
            candidates.Add(candidate);
        }

        return candidates;
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
                Path.GetDirectoryName(
                    _options.DatabasePath);

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
                "Job-contract recovery source ready at {DatabasePath}, schema {SchemaVersion}.",
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
}
