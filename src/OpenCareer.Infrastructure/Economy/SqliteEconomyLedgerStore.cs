using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Finance;

namespace OpenCareer.Infrastructure.Economy;

public sealed class SqliteEconomyLedgerStore :
    IAircraftAcquisitionStore,
    ICashGuardedEconomyLedgerStore,
    IPersistedContractSettlementStore
{
    private static readonly JsonSerializerOptions ContractJsonOptions =
        new(JsonSerializerDefaults.Web);

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

    public async Task<PersistedJobContract?> ReadJobContractAsync(
        Guid contractId,
        CancellationToken cancellationToken = default)
    {
        if (contractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(contractId));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await ReadJobContractAsync(
            connection,
            sqliteTransaction: null,
            contractId,
            cancellationToken).ConfigureAwait(false);
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

        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var sqliteTransaction = connection.BeginTransaction();

            PersistedJobContract? existing =
                await ReadJobContractAsync(
                    connection,
                    sqliteTransaction,
                    contract.ContractId,
                    cancellationToken).ConfigureAwait(false);

            if (existing is not null)
            {
                if (existing.Contract == contract)
                    return JobContractSaveResult.AlreadySaved;

                throw new InvalidOperationException(
                    "A different job contract already exists under this contract ID.");
            }

            await InsertJobContractAsync(
                connection,
                sqliteTransaction,
                contract,
                version: 0,
                cancellationToken).ConfigureAwait(false);

            sqliteTransaction.Commit();
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

        await _writeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var sqliteTransaction = connection.BeginTransaction();

            PersistedJobContract existing =
                await ReadJobContractAsync(
                    connection,
                    sqliteTransaction,
                    contract.ContractId,
                    cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Job contract does not exist.");

            if (existing.Contract == contract)
                return JobContractSaveResult.AlreadySaved;

            if (existing.Version != expectedVersion)
            {
                throw new InvalidOperationException(
                    "Job contract changed before this update could be saved. Re-read and retry.");
            }

            if (!EquivalentImmutableContract(
                    existing.Contract,
                    contract))
            {
                throw new InvalidOperationException(
                    "Persisted job-contract economic or dispatch terms are immutable.");
            }

            if (!AllowedContractTransition(
                    existing.Contract.Status,
                    contract.Status))
            {
                throw new InvalidOperationException(
                    $"Persisted job contract cannot transition from {existing.Contract.Status} to {contract.Status}.");
            }

            long nextVersion =
                checked(existing.Version + 1);

            await UpdateJobContractRowAsync(
                connection,
                sqliteTransaction,
                contract,
                nextVersion,
                expectedVersion,
                cancellationToken).ConfigureAwait(false);

            sqliteTransaction.Commit();
            return JobContractSaveResult.Updated;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<LedgerPostResult> CompleteAndPostContractSettlementAsync(
        JobContract completedContract,
        long expectedContractVersion,
        ContractSettlementSummary settlement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completedContract);
        ArgumentNullException.ThrowIfNull(settlement);
        completedContract.Validate();
        settlement.Validate();

        if (completedContract.Status != ContractStatus.Completed)
        {
            throw new InvalidOperationException(
                "Only a completed contract can be atomically settled.");
        }

        if (expectedContractVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedContractVersion));

        if (settlement.ContractId != completedContract.ContractId
            || settlement.Transaction.TransactionId != completedContract.ContractId)
        {
            throw new ArgumentException(
                "Settlement does not belong to the completed contract.",
                nameof(settlement));
        }

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

            PersistedJobContract persisted =
                await ReadJobContractAsync(
                    connection,
                    sqliteTransaction,
                    completedContract.ContractId,
                    cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Persisted job contract was not found.");

            string? existingId =
                await FindExistingTransactionIdAsync(
                    connection,
                    sqliteTransaction,
                    settlement.Transaction.TransactionId,
                    settlement.Transaction.IdempotencyKey,
                    cancellationToken).ConfigureAwait(false);

            if (existingId is not null)
            {
                EconomyLedgerTransaction existingTransaction =
                    await ReadTransactionAsync(
                        connection,
                        sqliteTransaction,
                        Guid.Parse(existingId),
                        cancellationToken).ConfigureAwait(false);

                if (!Equivalent(
                        existingTransaction,
                        settlement.Transaction)
                    || persisted.Contract != completedContract)
                {
                    throw new InvalidOperationException(
                        "Existing contract settlement conflicts with the persisted completed contract.");
                }

                return LedgerPostResult.AlreadyPosted;
            }

            if (persisted.Version != expectedContractVersion)
            {
                throw new InvalidOperationException(
                    "Job contract changed before completion settlement. Re-read and retry.");
            }

            if (!EquivalentImmutableContract(
                    persisted.Contract,
                    completedContract))
            {
                throw new InvalidOperationException(
                    "Completed contract economic or dispatch terms differ from the persisted contract.");
            }

            if (!AllowedContractTransition(
                    persisted.Contract.Status,
                    completedContract.Status))
            {
                throw new InvalidOperationException(
                    $"Persisted job contract cannot transition from {persisted.Contract.Status} to {completedContract.Status}.");
            }

            await InsertTransactionAsync(
                connection,
                sqliteTransaction,
                settlement.Transaction,
                cancellationToken).ConfigureAwait(false);

            await UpdateJobContractRowAsync(
                connection,
                sqliteTransaction,
                completedContract,
                checked(persisted.Version + 1),
                persisted.Version,
                cancellationToken).ConfigureAwait(false);

            sqliteTransaction.Commit();
            return LedgerPostResult.Posted;
        }
        finally
        {
            _writeGate.Release();
        }
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

    public async Task<LedgerPostResult> PostWithMinimumCashAsync(
        EconomyLedgerTransaction transaction,
        decimal minimumCashAfter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        transaction.Validate();

        if (minimumCashAfter < 0m
            || decimal.Round(
                minimumCashAfter,
                2,
                MidpointRounding.AwayFromZero)
                != minimumCashAfter)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumCashAfter));
        }

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
                        "The cash-guarded ledger identity already exists with different financial data.");
                }

                return LedgerPostResult.AlreadyPosted;
            }

            decimal currentCash =
                await ReadCashBalanceAsync(
                    connection,
                    sqliteTransaction,
                    cancellationToken).ConfigureAwait(false);

            decimal cashAfter =
                currentCash + transaction.CashChange;

            if (cashAfter < minimumCashAfter)
            {
                throw new InvalidOperationException(
                    "The transaction would reduce cash below the required minimum balance.");
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

            AircraftLoanRepaymentState? loanStateAfter = null;
            if (settlement.Accrual.LoanPrincipal > 0m)
            {
                AircraftLoanAgreement? loan =
                    await ReadAircraftLoanByOwnershipAsync(
                        connection,
                        sqliteTransaction,
                        settlement.OwnershipId,
                        cancellationToken).ConfigureAwait(false);

                if (loan is null)
                {
                    throw new InvalidOperationException(
                        "Recurring ownership costs contain loan principal but no loan exists for this aircraft.");
                }

                AircraftLoanRepaymentState stateBefore =
                    await ReadAircraftLoanStateAsync(
                        connection,
                        sqliteTransaction,
                        loan.LoanId,
                        cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        "Aircraft loan repayment state is missing.");

                loanStateAfter =
                    stateBefore.ApplyPrincipal(
                        settlement.Accrual.LoanPrincipal,
                        settlement.Transaction.OccurredAt);
            }

            await InsertTransactionAsync(
                connection,
                sqliteTransaction,
                settlement.Transaction,
                cancellationToken).ConfigureAwait(false);

            if (loanStateAfter is not null)
            {
                await UpsertAircraftLoanStateAsync(
                    connection,
                    sqliteTransaction,
                    loanStateAfter,
                    cancellationToken).ConfigureAwait(false);
            }

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

                await UpsertAircraftLoanStateAsync(
                    connection,
                    sqliteTransaction,
                    AircraftLoanRepaymentState.Start(
                        settlement.Loan),
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

    public async Task<AircraftLoanRepaymentState?> ReadAircraftLoanStateAsync(
        Guid loanId,
        CancellationToken cancellationToken = default)
    {
        if (loanId == Guid.Empty)
            throw new ArgumentException("Loan ID is required.", nameof(loanId));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await ReadAircraftLoanStateAsync(
            connection,
            sqliteTransaction: null,
            loanId,
            cancellationToken).ConfigureAwait(false);
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
                CREATE TABLE IF NOT EXISTS JobContracts (
                    ContractId TEXT NOT NULL PRIMARY KEY,
                    ContractJson TEXT NOT NULL,
                    Status INTEGER NOT NULL,
                    Version INTEGER NOT NULL CHECK (Version >= 0),
                    UpdatedAtUtcTicks INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_JobContracts_Status_Updated
                    ON JobContracts (Status, UpdatedAtUtcTicks DESC);

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

                CREATE TABLE IF NOT EXISTS AircraftLoanState (
                    LoanId TEXT NOT NULL PRIMARY KEY,
                    OwnershipId TEXT NOT NULL UNIQUE,
                    RemainingPrincipalCents INTEGER NOT NULL CHECK (RemainingPrincipalCents >= 0),
                    Version INTEGER NOT NULL CHECK (Version >= 0),
                    UpdatedAtUtcTicks INTEGER NOT NULL,
                    FOREIGN KEY (LoanId)
                        REFERENCES AircraftLoans(LoanId)
                        ON DELETE CASCADE,
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

    private static async Task<PersistedJobContract?> ReadJobContractAsync(
        SqliteConnection connection,
        SqliteTransaction? sqliteTransaction,
        Guid contractId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText =
            """
            SELECT ContractJson, Version
            FROM JobContracts
            WHERE ContractId = $contractId;
            """;
        command.Parameters.AddWithValue(
            "$contractId",
            contractId.ToString("D"));

        await using var reader =
            await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        JobContract contract =
            JsonSerializer.Deserialize<JobContract>(
                reader.GetString(0),
                ContractJsonOptions)
            ?? throw new InvalidDataException(
                "Persisted job contract JSON was empty.");

        contract.Validate();

        if (contract.ContractId != contractId)
        {
            throw new InvalidDataException(
                "Persisted job contract ID does not match its database key.");
        }

        var result =
            new PersistedJobContract(
                contract,
                reader.GetInt64(1));
        result.Validate();
        return result;
    }

    private static async Task InsertJobContractAsync(
        SqliteConnection connection,
        SqliteTransaction sqliteTransaction,
        JobContract contract,
        long version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText =
            """
            INSERT INTO JobContracts (
                ContractId,
                ContractJson,
                Status,
                Version,
                UpdatedAtUtcTicks)
            VALUES (
                $contractId,
                $contractJson,
                $status,
                $version,
                $updatedAt);
            """;
        command.Parameters.AddWithValue(
            "$contractId",
            contract.ContractId.ToString("D"));
        command.Parameters.AddWithValue(
            "$contractJson",
            JsonSerializer.Serialize(
                contract,
                ContractJsonOptions));
        command.Parameters.AddWithValue(
            "$status",
            (int)contract.Status);
        command.Parameters.AddWithValue(
            "$version",
            version);
        command.Parameters.AddWithValue(
            "$updatedAt",
            ContractUpdatedAt(contract).UtcTicks);

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task UpdateJobContractRowAsync(
        SqliteConnection connection,
        SqliteTransaction sqliteTransaction,
        JobContract contract,
        long version,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText =
            """
            UPDATE JobContracts
            SET ContractJson = $contractJson,
                Status = $status,
                Version = $version,
                UpdatedAtUtcTicks = $updatedAt
            WHERE ContractId = $contractId
              AND Version = $expectedVersion;
            """;
        command.Parameters.AddWithValue(
            "$contractJson",
            JsonSerializer.Serialize(
                contract,
                ContractJsonOptions));
        command.Parameters.AddWithValue(
            "$status",
            (int)contract.Status);
        command.Parameters.AddWithValue(
            "$version",
            version);
        command.Parameters.AddWithValue(
            "$updatedAt",
            ContractUpdatedAt(contract).UtcTicks);
        command.Parameters.AddWithValue(
            "$contractId",
            contract.ContractId.ToString("D"));
        command.Parameters.AddWithValue(
            "$expectedVersion",
            expectedVersion);

        int affected =
            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException(
                "Job contract optimistic-concurrency update failed.");
        }
    }

    private static bool EquivalentImmutableContract(
        JobContract left,
        JobContract right) =>
        left.ContractId == right.ContractId
        && left.EmployerId == right.EmployerId
        && left.Kind == right.Kind
        && left.ServiceTrack == right.ServiceTrack
        && string.Equals(left.OriginIcao, right.OriginIcao, StringComparison.Ordinal)
        && string.Equals(left.DestinationIcao, right.DestinationIcao, StringComparison.Ordinal)
        && left.Compensation == right.Compensation
        && left.OfferedAt == right.OfferedAt
        && left.MustAcceptBy == right.MustAcceptBy
        && left.MustStartBy == right.MustStartBy
        && left.MustCompleteBy == right.MustCompleteBy
        && left.AircraftRequirements == right.AircraftRequirements
        && left.ReputationReward.Equals(right.ReputationReward)
        && left.ReputationPenalty.Equals(right.ReputationPenalty)
        && string.Equals(left.MarketId, right.MarketId, StringComparison.Ordinal)
        && string.Equals(left.WorldEventId, right.WorldEventId, StringComparison.Ordinal)
        && left.GovernmentAuthorizationRequired == right.GovernmentAuthorizationRequired
        && left.EconomicSnapshot == right.EconomicSnapshot;

    private static bool AllowedContractTransition(
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

    private static async Task<AircraftLoanAgreement?> ReadAircraftLoanByOwnershipAsync(
        SqliteConnection connection,
        SqliteTransaction? sqliteTransaction,
        string ownershipId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText =
            """
            SELECT LoanId
            FROM AircraftLoans
            WHERE OwnershipId = $ownershipId;
            """;
        command.Parameters.AddWithValue("$ownershipId", ownershipId);

        object? result =
            await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

        if (result is null or DBNull)
            return null;

        return await ReadAircraftLoanAsync(
            connection,
            sqliteTransaction,
            Guid.Parse(Convert.ToString(
                result,
                System.Globalization.CultureInfo.InvariantCulture)!),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AircraftLoanRepaymentState?> ReadAircraftLoanStateAsync(
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
                RemainingPrincipalCents,
                Version,
                UpdatedAtUtcTicks
            FROM AircraftLoanState
            WHERE LoanId = $loanId;
            """;
        command.Parameters.AddWithValue("$loanId", loanId.ToString("D"));

        await using var reader =
            await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var state = new AircraftLoanRepaymentState(
            loanId,
            reader.GetString(0),
            FromCents(reader.GetInt64(1)),
            reader.GetInt64(2),
            new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero));

        state.Validate();
        return state;
    }

    private static async Task UpsertAircraftLoanStateAsync(
        SqliteConnection connection,
        SqliteTransaction sqliteTransaction,
        AircraftLoanRepaymentState state,
        CancellationToken cancellationToken)
    {
        state.Validate();

        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText =
            """
            INSERT INTO AircraftLoanState (
                LoanId,
                OwnershipId,
                RemainingPrincipalCents,
                Version,
                UpdatedAtUtcTicks)
            VALUES (
                $loanId,
                $ownershipId,
                $remainingPrincipal,
                $version,
                $updatedAt)
            ON CONFLICT(LoanId) DO UPDATE SET
                OwnershipId = excluded.OwnershipId,
                RemainingPrincipalCents = excluded.RemainingPrincipalCents,
                Version = excluded.Version,
                UpdatedAtUtcTicks = excluded.UpdatedAtUtcTicks;
            """;
        command.Parameters.AddWithValue("$loanId", state.LoanId.ToString("D"));
        command.Parameters.AddWithValue("$ownershipId", state.OwnershipId);
        command.Parameters.AddWithValue(
            "$remainingPrincipal",
            ToCents(state.RemainingPrincipal));
        command.Parameters.AddWithValue("$version", state.Version);
        command.Parameters.AddWithValue("$updatedAt", state.UpdatedAt.UtcTicks);

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
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
