using Microsoft.Data.Sqlite;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Finance;

namespace OpenCareer.Infrastructure.Economy;

public sealed class SqliteEconomyLedgerStore : IAircraftAcquisitionStore
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

    public async Task<LedgerPostResult> AcquireAircraftAsync(
        AircraftAcquisitionSettlement settlement,
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

                AircraftOwnershipRecord? persistedOwnership =
                    await ReadAircraftOwnershipAsync(
                        connection,
                        sqliteTransaction,
                        settlement.Ownership.OwnershipId,
                        cancellationToken).ConfigureAwait(false);

                AircraftLoanAgreement? persistedLoan =
                    settlement.Loan is null
                        ? null
                        : await ReadAircraftLoanAsync(
                            connection,
                            sqliteTransaction,
                            settlement.Loan.LoanId,
                            cancellationToken).ConfigureAwait(false);

                if (!Equivalent(existing, settlement.Transaction)
                    || persistedOwnership != settlement.Ownership
                    || persistedLoan != settlement.Loan)
                {
                    throw new InvalidOperationException(
                        "Aircraft purchase identity already exists with different financial or ownership data.");
                }

                return LedgerPostResult.AlreadyPosted;
            }

            if (await ListingAlreadyConsumedAsync(
                    connection,
                    sqliteTransaction,
                    settlement.Ownership.DealerId,
                    settlement.Ownership.ListingId,
                    cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "Dealer listing has already been purchased.");
            }

            if (await ReadAircraftOwnershipAsync(
                    connection,
                    sqliteTransaction,
                    settlement.Ownership.OwnershipId,
                    cancellationToken).ConfigureAwait(false)
                is not null)
            {
                throw new InvalidOperationException(
                    "Aircraft ownership ID already exists.");
            }

            decimal currentCash =
                await ReadCashBalanceAsync(
                    connection,
                    sqliteTransaction,
                    cancellationToken).ConfigureAwait(false);

            decimal cashAfter =
                currentCash + settlement.Transaction.CashChange;

            if (cashAfter < settlement.RequiredOperatingReserve)
            {
                throw new InvalidOperationException(
                    "Aircraft purchase would violate the required operating reserve using the authoritative ledger balance.");
            }

            await InsertTransactionAsync(
                connection,
                sqliteTransaction,
                settlement.Transaction,
                cancellationToken).ConfigureAwait(false);

            await InsertAircraftOwnershipAsync(
                connection,
                sqliteTransaction,
                settlement.Ownership,
                cancellationToken).ConfigureAwait(false);

            if (settlement.Loan is not null)
            {
                await InsertAircraftLoanAsync(
                    connection,
                    sqliteTransaction,
                    settlement.Loan,
                    cancellationToken).ConfigureAwait(false);
            }

            await UpsertBillingStateAsync(
                connection,
                sqliteTransaction,
                ActivePlayBillingState.Start(
                    settlement.Ownership.OwnershipId),
                cancellationToken).ConfigureAwait(false);

            sqliteTransaction.Commit();
            return LedgerPostResult.Posted;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<AircraftOwnershipRecord?> ReadAircraftOwnershipAsync(
        string ownershipId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipId);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await ReadAircraftOwnershipAsync(
            connection,
            sqliteTransaction: null,
            ownershipId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AircraftLoanAgreement?> ReadAircraftLoanAsync(
        Guid loanId,
        CancellationToken cancellationToken = default)
    {
        if (loanId == Guid.Empty)
            throw new ArgumentException("Loan ID is required.", nameof(loanId));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await ReadAircraftLoanAsync(
            connection,
            sqliteTransaction: null,
            loanId,
            cancellationToken).ConfigureAwait(false);
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

                CREATE TABLE IF NOT EXISTS OwnedAircraft (
                    OwnershipId TEXT NOT NULL PRIMARY KEY,
                    DealerId TEXT NOT NULL,
                    ListingId TEXT NOT NULL,
                    AircraftId TEXT NOT NULL,
                    AcquisitionMethod INTEGER NOT NULL,
                    AcquisitionPriceCents INTEGER NOT NULL CHECK (AcquisitionPriceCents > 0),
                    AcquiredAtUtcTicks INTEGER NOT NULL,
                    StorageIcao TEXT NOT NULL,
                    LoanId TEXT NULL,
                    UNIQUE (DealerId, ListingId),
                    UNIQUE (LoanId)
                );

                CREATE TABLE IF NOT EXISTS AircraftLoans (
                    LoanId TEXT NOT NULL PRIMARY KEY,
                    OwnershipId TEXT NOT NULL UNIQUE,
                    LenderId TEXT NOT NULL,
                    OriginalPrincipalCents INTEGER NOT NULL CHECK (OriginalPrincipalCents > 0),
                    AnnualRateText TEXT NOT NULL,
                    TermMonths INTEGER NOT NULL CHECK (TermMonths > 0),
                    ScheduledPaymentCents INTEGER NOT NULL CHECK (ScheduledPaymentCents > 0),
                    OriginatedAtUtcTicks INTEGER NOT NULL,
                    FOREIGN KEY (OwnershipId)
                        REFERENCES OwnedAircraft(OwnershipId)
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

    private static async Task<decimal> ReadCashBalanceAsync(
        SqliteConnection connection,
        SqliteTransaction sqliteTransaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
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
            : Convert.ToInt64(
                result,
                System.Globalization.CultureInfo.InvariantCulture);

        return FromCents(cents);
    }

    private static async Task<bool> ListingAlreadyConsumedAsync(
        SqliteConnection connection,
        SqliteTransaction sqliteTransaction,
        string dealerId,
        string listingId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText =
            """
            SELECT 1
            FROM OwnedAircraft
            WHERE DealerId = $dealerId
              AND ListingId = $listingId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$dealerId", dealerId);
        command.Parameters.AddWithValue("$listingId", listingId);

        object? result =
            await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

        return result is not null and not DBNull;
    }

    private static async Task InsertAircraftOwnershipAsync(
        SqliteConnection connection,
        SqliteTransaction sqliteTransaction,
        AircraftOwnershipRecord ownership,
        CancellationToken cancellationToken)
    {
        ownership.Validate();

        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText =
            """
            INSERT INTO OwnedAircraft (
                OwnershipId,
                DealerId,
                ListingId,
                AircraftId,
                AcquisitionMethod,
                AcquisitionPriceCents,
                AcquiredAtUtcTicks,
                StorageIcao,
                LoanId)
            VALUES (
                $ownershipId,
                $dealerId,
                $listingId,
                $aircraftId,
                $method,
                $price,
                $acquiredAt,
                $storageIcao,
                $loanId);
            """;
        command.Parameters.AddWithValue("$ownershipId", ownership.OwnershipId);
        command.Parameters.AddWithValue("$dealerId", ownership.DealerId);
        command.Parameters.AddWithValue("$listingId", ownership.ListingId);
        command.Parameters.AddWithValue("$aircraftId", ownership.AircraftId);
        command.Parameters.AddWithValue("$method", (int)ownership.AcquisitionMethod);
        command.Parameters.AddWithValue("$price", ToCents(ownership.AcquisitionPrice));
        command.Parameters.AddWithValue("$acquiredAt", ownership.AcquiredAt.UtcTicks);
        command.Parameters.AddWithValue("$storageIcao", ownership.StorageIcao);
        command.Parameters.AddWithValue(
            "$loanId",
            ownership.LoanId is { } loanId
                ? loanId.ToString("D")
                : DBNull.Value);

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task InsertAircraftLoanAsync(
        SqliteConnection connection,
        SqliteTransaction sqliteTransaction,
        AircraftLoanAgreement loan,
        CancellationToken cancellationToken)
    {
        loan.Validate();

        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText =
            """
            INSERT INTO AircraftLoans (
                LoanId,
                OwnershipId,
                LenderId,
                OriginalPrincipalCents,
                AnnualRateText,
                TermMonths,
                ScheduledPaymentCents,
                OriginatedAtUtcTicks)
            VALUES (
                $loanId,
                $ownershipId,
                $lenderId,
                $principal,
                $annualRate,
                $termMonths,
                $payment,
                $originatedAt);
            """;
        command.Parameters.AddWithValue("$loanId", loan.LoanId.ToString("D"));
        command.Parameters.AddWithValue("$ownershipId", loan.OwnershipId);
        command.Parameters.AddWithValue("$lenderId", loan.LenderId);
        command.Parameters.AddWithValue("$principal", ToCents(loan.OriginalPrincipal));
        command.Parameters.AddWithValue(
            "$annualRate",
            loan.AnnualRate.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$termMonths", loan.TermMonths);
        command.Parameters.AddWithValue("$payment", ToCents(loan.ScheduledMonthlyPayment));
        command.Parameters.AddWithValue("$originatedAt", loan.OriginatedAt.UtcTicks);

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<AircraftOwnershipRecord?> ReadAircraftOwnershipAsync(
        SqliteConnection connection,
        SqliteTransaction? sqliteTransaction,
        string ownershipId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText =
            """
            SELECT
                DealerId,
                ListingId,
                AircraftId,
                AcquisitionMethod,
                AcquisitionPriceCents,
                AcquiredAtUtcTicks,
                StorageIcao,
                LoanId
            FROM OwnedAircraft
            WHERE OwnershipId = $ownershipId;
            """;
        command.Parameters.AddWithValue("$ownershipId", ownershipId);

        await using var reader =
            await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var ownership = new AircraftOwnershipRecord(
            ownershipId,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            (AircraftAcquisitionMethod)reader.GetInt32(3),
            FromCents(reader.GetInt64(4)),
            new DateTimeOffset(reader.GetInt64(5), TimeSpan.Zero),
            reader.GetString(6),
            reader.IsDBNull(7)
                ? null
                : Guid.Parse(reader.GetString(7)));

        ownership.Validate();
        return ownership;
    }

    private static async Task<AircraftLoanAgreement?> ReadAircraftLoanAsync(
        SqliteConnection connection,
        SqliteTransaction? sqliteTransaction,
        Guid loanId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText =
            """
            SELECT
                OwnershipId,
                LenderId,
                OriginalPrincipalCents,
                AnnualRateText,
                TermMonths,
                ScheduledPaymentCents,
                OriginatedAtUtcTicks
            FROM AircraftLoans
            WHERE LoanId = $loanId;
            """;
        command.Parameters.AddWithValue("$loanId", loanId.ToString("D"));

        await using var reader =
            await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        decimal annualRate = decimal.Parse(
            reader.GetString(3),
            System.Globalization.CultureInfo.InvariantCulture);

        var loan = new AircraftLoanAgreement(
            loanId,
            reader.GetString(0),
            reader.GetString(1),
            FromCents(reader.GetInt64(2)),
            annualRate,
            reader.GetInt32(4),
            FromCents(reader.GetInt64(5)),
            new DateTimeOffset(reader.GetInt64(6), TimeSpan.Zero));

        loan.Validate();
        return loan;
    }

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
