using Microsoft.Data.Sqlite;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteAcceptedJobCancellationRecoverySource(
    OpenCareerDatabaseOptions options)
    : IAcceptedJobCancellationRecoverySource
{
    private const string ReservationPrefix = "contract:";
    private const string ReservationSuffix = ":aircraft-v1";

    private readonly OpenCareerDatabaseOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private int _initialized;

    public async Task<IReadOnlyList<Guid>> ReadPendingContractIdsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);

        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT contract.contract_id
            FROM job_contracts AS contract
            WHERE contract.status = $cancelled
              AND (
                    EXISTS (
                        SELECT 1
                        FROM prepared_dispatch_plans AS plan
                        WHERE plan.contract_id = contract.contract_id
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM aircraft_availability AS aircraft
                        WHERE aircraft.reservation_id =
                            ($reservation_prefix || contract.contract_id || $reservation_suffix)
                            COLLATE NOCASE
                    )
              )
            ORDER BY
                contract.updated_at_ms DESC,
                contract.contract_id ASC;
            """;
        command.Parameters.AddWithValue(
            "$cancelled",
            (int)ContractStatus.Cancelled);
        command.Parameters.AddWithValue(
            "$reservation_prefix",
            ReservationPrefix);
        command.Parameters.AddWithValue(
            "$reservation_suffix",
            ReservationSuffix);

        var pending = new List<Guid>();
        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            if (!Guid.TryParse(reader.GetString(0), out Guid contractId)
                || contractId == Guid.Empty)
            {
                throw new InvalidDataException(
                    "Persisted cancellation-cleanup contract identity is invalid.");
            }

            pending.Add(contractId);
        }

        return pending;
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

            string? directory = Path.GetDirectoryName(_options.DatabasePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);
            await OpenCareerDatabaseMigrator
                .MigrateAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            Volatile.Write(ref _initialized, 1);
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
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText =
            "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";
        await pragma
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);

        return connection;
    }
}
