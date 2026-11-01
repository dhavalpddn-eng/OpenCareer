using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteEconomyLedgerStore : IEconomyLedgerStore
{
    private readonly OpenCareerDatabaseOptions _options;
    private readonly ILogger<SqliteEconomyLedgerStore> _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private int _initialized;

    public SqliteEconomyLedgerStore(
        OpenCareerDatabaseOptions options,
        ILogger<SqliteEconomyLedgerStore> logger)
    {
        _options =
            options
            ?? throw new ArgumentNullException(nameof(options));

        _logger =
            logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LedgerPostResult> PostAsync(
        EconomyLedgerTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        transaction.Validate();

        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);

        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await using SqliteConnection connection =
                await OpenConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);

            using SqliteTransaction sqliteTransaction =
                connection.BeginTransaction();

            Guid? existingId =
                await FindExistingTransactionIdAsync(
                        connection,
                        sqliteTransaction,
                        transaction.TransactionId,
                        transaction.IdempotencyKey,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (existingId is { } id)
            {
                EconomyLedgerTransaction existing =
                    await ReadTransactionAsync(
                            connection,
                            sqliteTransaction,
                            id,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (!Equivalent(existing, transaction))
                {
                    throw new InvalidOperationException(
                        "The ledger idempotency key or transaction ID already exists with different financial data.");
                }

                return LedgerPostResult.AlreadyPosted;
            }

            await InsertTransactionAsync(
                    connection,
                    sqliteTransaction,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);

            sqliteTransaction.Commit();
            return LedgerPostResult.Posted;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<EconomyLedgerTransaction?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);

        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT transaction_id
            FROM economy_ledger_transactions
            WHERE idempotency_key = $idempotency_key
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$idempotency_key",
            idempotencyKey);

        object? result =
            await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

        if (result is null or DBNull)
            return null;

        Guid transactionId =
            Guid.Parse(
                Convert.ToString(
                    result,
                    System.Globalization.CultureInfo.InvariantCulture)!);

        return await ReadTransactionAsync(
                connection,
                transaction: null,
                transactionId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<decimal> ReadCashBalanceAsync(
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
            SELECT COALESCE(
                SUM(debit_cents - credit_cents),
                0)
            FROM economy_ledger_postings
            WHERE account_code = $account_code;
            """;

        command.Parameters.AddWithValue(
            "$account_code",
            (int)LedgerAccountCode.Cash);

        object? result =
            await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

        long cents =
            result is null or DBNull
                ? 0
                : Convert.ToInt64(
                    result,
                    System.Globalization.CultureInfo.InvariantCulture);

        return FromCents(cents);
    }

    public async Task<IReadOnlyList<LedgerAccountBalance>>
        ReadAccountBalancesAsync(
            CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);

        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

        var balances =
            new List<LedgerAccountBalance>();

        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                account_code,
                COALESCE(SUM(debit_cents), 0),
                COALESCE(SUM(credit_cents), 0)
            FROM economy_ledger_postings
            GROUP BY account_code
            ORDER BY account_code ASC;
            """;

        await using SqliteDataReader reader =
            await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        while (await reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            var balance =
                new LedgerAccountBalance(
                    (LedgerAccountCode)reader.GetInt32(0),
                    FromCents(reader.GetInt64(1)),
                    FromCents(reader.GetInt64(2)));

            balance.Validate();
            balances.Add(balance);
        }

        return balances;
    }

    public async Task<IReadOnlyList<EconomyLedgerTransaction>>
        ReadRecentAsync(
            int limit,
            CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);

        await using SqliteConnection connection =
            await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

        var ids =
            new List<Guid>();

        await using (SqliteCommand command =
            connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT transaction_id
                FROM economy_ledger_transactions
                ORDER BY
                    occurred_at_utc_ticks DESC,
                    transaction_id ASC
                LIMIT $limit;
                """;

            command.Parameters.AddWithValue(
                "$limit",
                limit);

            await using SqliteDataReader reader =
                await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);

            while (await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                ids.Add(
                    Guid.Parse(reader.GetString(0)));
            }
        }

        var transactions =
            new List<EconomyLedgerTransaction>(ids.Count);

        foreach (Guid id in ids)
        {
            transactions.Add(
                await ReadTransactionAsync(
                        connection,
                        transaction: null,
                        id,
                        cancellationToken)
                    .ConfigureAwait(false));
        }

        return transactions;
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

            Volatile.Write(
                ref _initialized,
                1);

            _logger.LogInformation(
                "Economy-ledger SQLite storage ready at {DatabasePath}, schema {SchemaVersion}.",
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

    private static async Task<Guid?> FindExistingTransactionIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transactionId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT transaction_id
            FROM economy_ledger_transactions
            WHERE transaction_id = $transaction_id
               OR idempotency_key = $idempotency_key
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$transaction_id",
            transactionId.ToString("D"));

        command.Parameters.AddWithValue(
            "$idempotency_key",
            idempotencyKey);

        object? result =
            await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

        if (result is null or DBNull)
            return null;

        return Guid.Parse(
            Convert.ToString(
                result,
                System.Globalization.CultureInfo.InvariantCulture)!);
    }

    private static async Task InsertTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EconomyLedgerTransaction ledgerTransaction,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand command =
            connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO economy_ledger_transactions (
                    transaction_id,
                    idempotency_key,
                    occurred_at_utc_ticks,
                    description,
                    reference_type,
                    reference_id
                )
                VALUES (
                    $transaction_id,
                    $idempotency_key,
                    $occurred_at_utc_ticks,
                    $description,
                    $reference_type,
                    $reference_id
                );
                """;

            command.Parameters.AddWithValue(
                "$transaction_id",
                ledgerTransaction.TransactionId.ToString("D"));

            command.Parameters.AddWithValue(
                "$idempotency_key",
                ledgerTransaction.IdempotencyKey);

            command.Parameters.AddWithValue(
                "$occurred_at_utc_ticks",
                ledgerTransaction.OccurredAt.UtcTicks);

            command.Parameters.AddWithValue(
                "$description",
                ledgerTransaction.Description);

            command.Parameters.AddWithValue(
                "$reference_type",
                ledgerTransaction.ReferenceType);

            command.Parameters.AddWithValue(
                "$reference_id",
                ledgerTransaction.ReferenceId);

            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        for (int index = 0;
            index < ledgerTransaction.Postings.Count;
            index++)
        {
            LedgerPosting posting =
                ledgerTransaction.Postings[index];

            await using SqliteCommand command =
                connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO economy_ledger_postings (
                    transaction_id,
                    posting_index,
                    account_code,
                    debit_cents,
                    credit_cents,
                    memo
                )
                VALUES (
                    $transaction_id,
                    $posting_index,
                    $account_code,
                    $debit_cents,
                    $credit_cents,
                    $memo
                );
                """;

            command.Parameters.AddWithValue(
                "$transaction_id",
                ledgerTransaction.TransactionId.ToString("D"));

            command.Parameters.AddWithValue(
                "$posting_index",
                index);

            command.Parameters.AddWithValue(
                "$account_code",
                (int)posting.Account);

            command.Parameters.AddWithValue(
                "$debit_cents",
                ToCents(posting.Debit));

            command.Parameters.AddWithValue(
                "$credit_cents",
                ToCents(posting.Credit));

            command.Parameters.AddWithValue(
                "$memo",
                posting.Memo);

            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<EconomyLedgerTransaction>
        ReadTransactionAsync(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            Guid transactionId,
            CancellationToken cancellationToken)
    {
        string? idempotencyKey = null;
        long occurredAtMs = 0;
        string? description = null;
        string? referenceType = null;
        string? referenceId = null;

        await using (SqliteCommand command =
            connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT
                    idempotency_key,
                    occurred_at_utc_ticks,
                    description,
                    reference_type,
                    reference_id
                FROM economy_ledger_transactions
                WHERE transaction_id = $transaction_id
                LIMIT 1;
                """;

            command.Parameters.AddWithValue(
                "$transaction_id",
                transactionId.ToString("D"));

            await using SqliteDataReader reader =
                await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (!await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                throw new InvalidDataException(
                    "Persisted economy-ledger transaction was not found.");
            }

            idempotencyKey = reader.GetString(0);
            occurredAtMs = reader.GetInt64(1);
            description = reader.GetString(2);
            referenceType = reader.GetString(3);
            referenceId = reader.GetString(4);
        }

        var postings =
            new List<LedgerPosting>();

        await using (SqliteCommand command =
            connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT
                    account_code,
                    debit_cents,
                    credit_cents,
                    memo
                FROM economy_ledger_postings
                WHERE transaction_id = $transaction_id
                ORDER BY posting_index ASC;
                """;

            command.Parameters.AddWithValue(
                "$transaction_id",
                transactionId.ToString("D"));

            await using SqliteDataReader reader =
                await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);

            while (await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                postings.Add(
                    new LedgerPosting(
                        (LedgerAccountCode)reader.GetInt32(0),
                        FromCents(reader.GetInt64(1)),
                        FromCents(reader.GetInt64(2)),
                        reader.GetString(3)));
            }
        }

        var result =
            new EconomyLedgerTransaction(
                transactionId,
                idempotencyKey,
                new DateTimeOffset(occurredAtMs, TimeSpan.Zero),
                description,
                referenceType,
                referenceId,
                postings);

        result.Validate();
        return result;
    }

    private static bool Equivalent(
        EconomyLedgerTransaction left,
        EconomyLedgerTransaction right) =>
        left.TransactionId == right.TransactionId
        && string.Equals(
            left.IdempotencyKey,
            right.IdempotencyKey,
            StringComparison.Ordinal)
        && left.OccurredAt == right.OccurredAt
        && string.Equals(
            left.Description,
            right.Description,
            StringComparison.Ordinal)
        && string.Equals(
            left.ReferenceType,
            right.ReferenceType,
            StringComparison.Ordinal)
        && string.Equals(
            left.ReferenceId,
            right.ReferenceId,
            StringComparison.Ordinal)
        && left.Postings.SequenceEqual(right.Postings);

    private static long ToCents(
        decimal amount) =>
        checked((long)(amount * 100m));

    private static decimal FromCents(
        long cents) =>
        cents / 100m;

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
