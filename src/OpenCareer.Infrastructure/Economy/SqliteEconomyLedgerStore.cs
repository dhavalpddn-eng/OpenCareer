using Microsoft.Data.Sqlite;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Infrastructure.Economy;

public sealed class SqliteEconomyLedgerStore : IActivePlayBillingLedgerStore, IAircraftPurchaseLedgerStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _initialized;

    public SqliteEconomyLedgerStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        string fullPath = Path.GetFullPath(databasePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public async Task<LedgerPostResult> PostAsync(
        EconomyLedgerTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        transaction.Validate();

        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

            using var sqliteTransaction = connection.BeginTransaction();

            string? existingId = await FindExistingTransactionIdAsync(
                connection,
                sqliteTransaction,
                transaction.TransactionId,
                transaction.IdempotencyKey,
                cancellationToken).ConfigureAwait(false);

            if (existingId is not null)
            {
                EconomyLedgerTransaction existing =
                    await ReadTransactionAsync(
                        connection,
                        sqliteTransaction,
                        Guid.Parse(existingId),
                        cancellationToken).ConfigureAwait(false);

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
                cancellationToken).ConfigureAwait(false);

            sqliteTransaction.Commit();
            return LedgerPostResult.Posted;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<LedgerPostResult> PostAircraftPurchaseAsync(
        AircraftPurchaseSettlement settlement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        settlement.Validate();

        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

            using var sqliteTransaction = connection.BeginTransaction();

            AircraftPurchaseSettlement? existingPurchase =
                await ReadAircraftPurchaseAsync(
                    connection,
                    sqliteTransaction,
                    settlement.PurchaseId,
                    cancellationToken).ConfigureAwait(false);

            if (existingPurchase is not null)
            {
                if (!EquivalentAircraftPurchase(existingPurchase, settlement))
                {
                    throw new InvalidOperationException(
                        "Aircraft purchase ID already exists with different financial data.");
                }

                return LedgerPostResult.AlreadyPosted;
            }

            string? consumedBy =
                await FindConsumedListingOrOwnershipAsync(
                    connection,
                    sqliteTransaction,
                    settlement.ListingId,
                    settlement.OwnershipId,
                    cancellationToken).ConfigureAwait(false);

            if (consumedBy is not null)
            {
                throw new InvalidOperationException(
                    $"Aircraft listing or ownership is already consumed by purchase {consumedBy}.");
            }

            string? existingLedgerTransaction =
                await FindExistingTransactionIdAsync(
                    connection,
                    sqliteTransaction,
                    settlement.Transaction.TransactionId,
                    settlement.Transaction.IdempotencyKey,
                    cancellationToken).ConfigureAwait(false);

            if (existingLedgerTransaction is not null)
            {
                throw new InvalidOperationException(
                    "Aircraft purchase ledger identity is already in use without the matching purchase record.");
            }

            await InsertTransactionAsync(
                connection,
                sqliteTransaction,
                settlement.Transaction,
                cancellationToken).ConfigureAwait(false);

            await InsertAircraftPurchaseAsync(
                connection,
                sqliteTransaction,
                settlement,
                cancellationToken).ConfigureAwait(false);

            sqliteTransaction.Commit();
            return LedgerPostResult.Posted;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<AircraftPurchaseSettlement?> ReadAircraftPurchaseAsync(
        string ownershipId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipId);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        Guid? purchaseId = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT PurchaseId
                FROM AircraftPurchases
                WHERE OwnershipId = $ownershipId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$ownershipId", ownershipId);

            object? result =
                await command
                    .ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (result is not null and not DBNull)
                purchaseId = Guid.Parse(Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture)!);
        }

        return purchaseId is { } id
            ? await ReadAircraftPurchaseAsync(
                connection,
                sqliteTransaction: null,
                id,
                cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<ActivePlayBillingState> ReadActivePlayBillingStateAsync(
        string ownershipId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipId);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        ActivePlayBillingState? state =
            await ReadBillingStateAsync(
                connection,
                sqliteTransaction: null,
                ownershipId,
                cancellationToken).ConfigureAwait(false);

        return state ?? ActivePlayBillingState.Start(ownershipId);
    }

    public async Task<LedgerPostResult> PostActivePlayRecurringCostAsync(
        PersistedActivePlayRecurringCostSettlementSummary settlement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        settlement.Validate();

        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

            using var sqliteTransaction = connection.BeginTransaction();

            string? existingId = await FindExistingTransactionIdAsync(
                connection,
                sqliteTransaction,
                settlement.Transaction.TransactionId,
                settlement.Transaction.IdempotencyKey,
                cancellationToken).ConfigureAwait(false);

            if (existingId is not null)
            {
                EconomyLedgerTransaction existing =
                    await ReadTransactionAsync(
                        connection,
                        sqliteTransaction,
                        Guid.Parse(existingId),
                        cancellationToken).ConfigureAwait(false);

                if (!Equivalent(existing, settlement.Transaction))
                {
                    throw new InvalidOperationException(
                        "The recurring-cost idempotency key or transaction ID already exists with different financial data.");
                }

                return LedgerPostResult.AlreadyPosted;
            }

            ActivePlayBillingState persistedBefore =
                await ReadBillingStateAsync(
                    connection,
                    sqliteTransaction,
                    settlement.OwnershipId,
                    cancellationToken).ConfigureAwait(false)
                ?? ActivePlayBillingState.Start(settlement.OwnershipId);

            if (!EquivalentBillingState(persistedBefore, settlement.StateBefore))
            {
                throw new InvalidOperationException(
                    "Active-play billing state changed before recurring costs could be posted. Re-read the state and retry the activity settlement.");
            }

            await InsertTransactionAsync(
                connection,
                sqliteTransaction,
                settlement.Transaction,
                cancellationToken).ConfigureAwait(false);

            await UpsertBillingStateAsync(
                connection,
                sqliteTransaction,
                settlement.StateAfter,
                cancellationToken).ConfigureAwait(false);

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
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        Guid? transactionId = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT TransactionId
                FROM EconomyLedgerTransactions
                WHERE IdempotencyKey = $idempotencyKey
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$idempotencyKey", idempotencyKey);

            object? result =
                await command
                    .ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (result is not null and not DBNull)
            {
                transactionId = Guid.Parse(
                    Convert.ToString(
                        result,
                        System.Globalization.CultureInfo.InvariantCulture)!);
            }
        }

        return transactionId is { } id
            ? await ReadTransactionAsync(
                connection,
                sqliteTransaction: null,
                id,
                cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<decimal> ReadCashBalanceAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(SUM(DebitCents - CreditCents), 0)
            FROM EconomyLedgerPostings
            WHERE AccountCode = $cashAccount;
            """;
        command.Parameters.AddWithValue(
            "$cashAccount",
            (int)LedgerAccountCode.Cash);

        object? result =
            await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

        long cents = result is null or DBNull
            ? 0
            : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);

        return FromCents(cents);
    }

    public async Task<IReadOnlyList<EconomyLedgerTransaction>> ReadRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var ids = new List<Guid>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT TransactionId
                FROM EconomyLedgerTransactions
                ORDER BY OccurredAtUtcTicks DESC, TransactionId ASC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);

            await using var reader =
                await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                ids.Add(Guid.Parse(reader.GetString(0)));
        }

        var transactions = new List<EconomyLedgerTransaction>(ids.Count);
        foreach (Guid id in ids)
        {
            transactions.Add(
                await ReadTransactionAsync(
                    connection,
                    sqliteTransaction: null,
                    id,
                    cancellationToken).ConfigureAwait(false));
        }

        return transactions;
    }

    private async Task EnsureInitializedAsync(
        CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initializationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_initialized)
                return;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS EconomyLedgerTransactions (
                    TransactionId TEXT NOT NULL PRIMARY KEY,
                    IdempotencyKey TEXT NOT NULL UNIQUE,
                    OccurredAtUtcTicks INTEGER NOT NULL,
                    Description TEXT NOT NULL,
                    ReferenceType TEXT NOT NULL,
                    ReferenceId TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS EconomyLedgerPostings (
                    TransactionId TEXT NOT NULL,
                    PostingIndex INTEGER NOT NULL,
                    AccountCode INTEGER NOT NULL,
                    DebitCents INTEGER NOT NULL,
                    CreditCents INTEGER NOT NULL,
                    Memo TEXT NOT NULL,
                    PRIMARY KEY (TransactionId, PostingIndex),
                    FOREIGN KEY (TransactionId)
                        REFERENCES EconomyLedgerTransactions(TransactionId)
                        ON DELETE CASCADE,
                    CHECK (DebitCents >= 0),
                    CHECK (CreditCents >= 0),
                    CHECK (
                        (DebitCents > 0 AND CreditCents = 0)
                        OR (CreditCents > 0 AND DebitCents = 0)
                    )
                );

                CREATE TABLE IF NOT EXISTS ActivePlayBillingState (
                    OwnershipId TEXT NOT NULL PRIMARY KEY,
                    CycleIndex INTEGER NOT NULL CHECK (CycleIndex >= 0),
                    CycleProgressTicks INTEGER NOT NULL CHECK (CycleProgressTicks >= 0),
                    Version INTEGER NOT NULL CHECK (Version >= 0),
                    UpdatedAtUtcTicks INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS AircraftPurchases (
                    PurchaseId TEXT NOT NULL PRIMARY KEY,
                    OwnershipId TEXT NOT NULL UNIQUE,
                    ListingId TEXT NOT NULL UNIQUE,
                    AircraftId TEXT NOT NULL,
                    SalePriceCents INTEGER NOT NULL CHECK (SalePriceCents > 0),
                    CashPaidCents INTEGER NOT NULL CHECK (CashPaidCents >= 0),
                    FinancedPrincipalCents INTEGER NOT NULL CHECK (FinancedPrincipalCents >= 0),
                    PurchasedAtUtcTicks INTEGER NOT NULL,
                    LoanId TEXT NULL UNIQUE,
                    LenderId TEXT NULL,
                    AnnualRate TEXT NULL,
                    TermCycles INTEGER NULL,
                    ScheduledPaymentCents INTEGER NULL,
                    FOREIGN KEY (PurchaseId)
                        REFERENCES EconomyLedgerTransactions(TransactionId)
                        ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_EconomyLedgerTransactions_Occurred
                    ON EconomyLedgerTransactions (OccurredAtUtcTicks DESC);

                CREATE INDEX IF NOT EXISTS IX_EconomyLedgerPostings_Account
                    ON EconomyLedgerPostings (AccountCode, TransactionId);
                """;

            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static async Task<ActivePlayBillingState?> ReadBillingStateAsync(
        SqliteConnection connection,
        SqliteTransaction? sqliteTransaction,
        string ownershipId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText =
            """
            SELECT CycleIndex, CycleProgressTicks, Version, UpdatedAtUtcTicks
            FROM ActivePlayBillingState
            WHERE OwnershipId = $ownershipId;
            """;
        command.Parameters.AddWithValue("$ownershipId", ownershipId);

        await using var reader =
            await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var state = new ActivePlayBillingState(
            ownershipId,
            reader.GetInt32(0),
            TimeSpan.FromTicks(reader.GetInt64(1)),
            reader.GetInt64(2),
            new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero));
        state.Validate();
        return state;
    }

    private static async Task UpsertBillingStateAsync(
        SqliteConnection connection,
        SqliteTransaction sqliteTransaction,
        ActivePlayBillingState state,
        CancellationToken cancellationToken)
    {
        state.Validate();

        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText =
            """
            INSERT INTO ActivePlayBillingState (
                OwnershipId,
                CycleIndex,
                CycleProgressTicks,
                Version,
                UpdatedAtUtcTicks)
            VALUES (
                $ownershipId,
                $cycleIndex,
                $cycleProgressTicks,
                $version,
                $updatedAt)
            ON CONFLICT(OwnershipId) DO UPDATE SET
                CycleIndex = excluded.CycleIndex,
                CycleProgressTicks = excluded.CycleProgressTicks,
                Version = excluded.Version,
                UpdatedAtUtcTicks = excluded.UpdatedAtUtcTicks;
            """;
        command.Parameters.AddWithValue("$ownershipId", state.OwnershipId);
        command.Parameters.AddWithValue("$cycleIndex", state.CycleIndex);
        command.Parameters.AddWithValue("$cycleProgressTicks", state.CycleProgress.Ticks);
        command.Parameters.AddWithValue("$version", state.Version);
        command.Parameters.AddWithValue("$updatedAt", state.UpdatedAt.UtcTicks);

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool EquivalentBillingState(
        ActivePlayBillingState left,
        ActivePlayBillingState right) =>
        string.Equals(left.OwnershipId, right.OwnershipId, StringComparison.Ordinal)
        && left.CycleIndex == right.CycleIndex
        && left.CycleProgress == right.CycleProgress
        && left.Version == right.Version
        && left.UpdatedAt.UtcTicks == right.UpdatedAt.UtcTicks;

    private static async Task EnableForeignKeysAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string?> FindExistingTransactionIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid transactionId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT TransactionId
            FROM EconomyLedgerTransactions
            WHERE TransactionId = $transactionId
               OR IdempotencyKey = $idempotencyKey
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$transactionId",
            transactionId.ToString("D"));
        command.Parameters.AddWithValue(
            "$idempotencyKey",
            idempotencyKey);

        object? result =
            await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

        return result is null or DBNull
            ? null
            : Convert.ToString(
                result,
                System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task InsertTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EconomyLedgerTransaction ledgerTransaction,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO EconomyLedgerTransactions (
                    TransactionId,
                    IdempotencyKey,
                    OccurredAtUtcTicks,
                    Description,
                    ReferenceType,
                    ReferenceId)
                VALUES (
                    $transactionId,
                    $idempotencyKey,
                    $occurredAt,
                    $description,
                    $referenceType,
                    $referenceId);
                """;

            command.Parameters.AddWithValue(
                "$transactionId",
                ledgerTransaction.TransactionId.ToString("D"));
            command.Parameters.AddWithValue(
                "$idempotencyKey",
                ledgerTransaction.IdempotencyKey);
            command.Parameters.AddWithValue(
                "$occurredAt",
                ledgerTransaction.OccurredAt.UtcTicks);
            command.Parameters.AddWithValue(
                "$description",
                ledgerTransaction.Description);
            command.Parameters.AddWithValue(
                "$referenceType",
                ledgerTransaction.ReferenceType);
            command.Parameters.AddWithValue(
                "$referenceId",
                ledgerTransaction.ReferenceId);

            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        for (int index = 0; index < ledgerTransaction.Postings.Count; index++)
        {
            LedgerPosting posting = ledgerTransaction.Postings[index];

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO EconomyLedgerPostings (
                    TransactionId,
                    PostingIndex,
                    AccountCode,
                    DebitCents,
                    CreditCents,
                    Memo)
                VALUES (
                    $transactionId,
                    $postingIndex,
                    $accountCode,
                    $debitCents,
                    $creditCents,
                    $memo);
                """;

            command.Parameters.AddWithValue(
                "$transactionId",
                ledgerTransaction.TransactionId.ToString("D"));
            command.Parameters.AddWithValue(
                "$postingIndex",
                index);
            command.Parameters.AddWithValue(
                "$accountCode",
                (int)posting.Account);
            command.Parameters.AddWithValue(
                "$debitCents",
                ToCents(posting.Debit));
            command.Parameters.AddWithValue(
                "$creditCents",
                ToCents(posting.Credit));
            command.Parameters.AddWithValue(
                "$memo",
                posting.Memo);

            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<EconomyLedgerTransaction> ReadTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction? sqliteTransaction,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        string? idempotencyKey = null;
        long occurredAtTicks = 0;
        string? description = null;
        string? referenceType = null;
        string? referenceId = null;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = sqliteTransaction;
            command.CommandText =
                """
                SELECT
                    IdempotencyKey,
                    OccurredAtUtcTicks,
                    Description,
                    ReferenceType,
                    ReferenceId
                FROM EconomyLedgerTransactions
                WHERE TransactionId = $transactionId;
                """;
            command.Parameters.AddWithValue(
                "$transactionId",
                transactionId.ToString("D"));

            await using var reader =
                await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"Ledger transaction {transactionId:D} was not found.");
            }

            idempotencyKey = reader.GetString(0);
            occurredAtTicks = reader.GetInt64(1);
            description = reader.GetString(2);
            referenceType = reader.GetString(3);
            referenceId = reader.GetString(4);
        }

        var postings = new List<LedgerPosting>();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = sqliteTransaction;
            command.CommandText =
                """
                SELECT
                    AccountCode,
                    DebitCents,
                    CreditCents,
                    Memo
                FROM EconomyLedgerPostings
                WHERE TransactionId = $transactionId
                ORDER BY PostingIndex ASC;
                """;
            command.Parameters.AddWithValue(
                "$transactionId",
                transactionId.ToString("D"));

            await using var reader =
                await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                postings.Add(
                    new LedgerPosting(
                        (LedgerAccountCode)reader.GetInt32(0),
                        FromCents(reader.GetInt64(1)),
                        FromCents(reader.GetInt64(2)),
                        reader.GetString(3)));
            }
        }

        var transaction = new EconomyLedgerTransaction(
            transactionId,
            idempotencyKey!,
            new DateTimeOffset(occurredAtTicks, TimeSpan.Zero),
            description!,
            referenceType!,
            referenceId!,
            postings);

        transaction.Validate();
        return transaction;
    }

    private static bool Equivalent(
        EconomyLedgerTransaction left,
        EconomyLedgerTransaction right)
    {
        if (left.TransactionId != right.TransactionId
            || !string.Equals(
                left.IdempotencyKey,
                right.IdempotencyKey,
                StringComparison.Ordinal)
            || left.OccurredAt.UtcTicks != right.OccurredAt.UtcTicks
            || !string.Equals(
                left.Description,
                right.Description,
                StringComparison.Ordinal)
            || !string.Equals(
                left.ReferenceType,
                right.ReferenceType,
                StringComparison.Ordinal)
            || !string.Equals(
                left.ReferenceId,
                right.ReferenceId,
                StringComparison.Ordinal)
            || left.Postings.Count != right.Postings.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Postings.Count; index++)
        {
            if (left.Postings[index] != right.Postings[index])
                return false;
        }

        return true;
    }

    private static long ToCents(decimal amount)
    {
        decimal cents = amount * 100m;
        if (cents > long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(amount));

        return checked((long)cents);
    }

    private static decimal FromCents(long cents) =>
        cents / 100m;
}
