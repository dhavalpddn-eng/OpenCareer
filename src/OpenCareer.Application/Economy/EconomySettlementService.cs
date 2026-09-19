using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public interface IPersistedContractSettlementStore :
    IEconomyLedgerStore,
    IJobContractStore
{
    Task<LedgerPostResult> CompleteAndPostContractSettlementAsync(
        JobContract completedContract,
        long expectedContractVersion,
        ContractSettlementSummary settlement,
        CancellationToken cancellationToken = default);
}

public sealed record PersistedEconomySettlementResult(
    PersistedJobContract Contract,
    ContractSettlementSummary Settlement,
    LedgerPostResult PostResult,
    decimal CashBalanceAfter)
{
    public bool WasNewlyPosted =>
        PostResult == LedgerPostResult.Posted;
}

public sealed class PersistedEconomySettlementService(
    IPersistedContractSettlementStore store)
{
    private readonly IPersistedContractSettlementStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    public async Task<PersistedEconomySettlementResult> CompleteAndSettleAsync(
        Guid contractId,
        long expectedContractVersion,
        DateTimeOffset completedAt,
        bool flightCompletionVerified,
        ContractSettlementCosts actualCosts,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken = default)
    {
        if (contractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(contractId));

        if (expectedContractVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedContractVersion));

        ArgumentNullException.ThrowIfNull(actualCosts);

        PersistedJobContract current =
            await _store
                .ReadJobContractAsync(
                    contractId,
                    cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Persisted job contract was not found.");

        JobContract completed;
        if (current.Contract.Status == ContractStatus.Completed)
        {
            if (current.Contract.CompletedAt != completedAt)
            {
                throw new InvalidOperationException(
                    "Completed contract timestamp does not match the persisted completion.");
            }

            if (!flightCompletionVerified)
            {
                throw new InvalidOperationException(
                    "A retry must still provide verified flight completion.");
            }

            completed = current.Contract;
        }
        else
        {
            completed =
                current.Contract.Complete(
                    completedAt,
                    flightCompletionVerified);
        }

        ContractSettlementSummary settlement =
            ContractSettlementEngine.Create(
                completed,
                actualCosts,
                settledAt);

        LedgerPostResult postResult =
            await _store
                .CompleteAndPostContractSettlementAsync(
                    completed,
                    expectedContractVersion,
                    settlement,
                    cancellationToken)
                .ConfigureAwait(false);

        PersistedJobContract persistedAfter =
            await _store
                .ReadJobContractAsync(
                    contractId,
                    cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Completed job contract disappeared after settlement.");

        decimal cashBalance =
            await _store
                .ReadCashBalanceAsync(cancellationToken)
                .ConfigureAwait(false);

        return new PersistedEconomySettlementResult(
            persistedAfter,
            settlement,
            postResult,
            cashBalance);
    }
}

public sealed record EconomySettlementResult(
    ContractSettlementSummary Settlement,
    LedgerPostResult PostResult,
    decimal CashBalanceAfter)
{
    public bool WasNewlyPosted =>
        PostResult == LedgerPostResult.Posted;
}

public sealed class EconomySettlementService(
    IEconomyLedgerStore ledgerStore)
{
    private readonly IEconomyLedgerStore _ledgerStore =
        ledgerStore ?? throw new ArgumentNullException(nameof(ledgerStore));

    public async Task<EconomySettlementResult> SettleCompletedContractAsync(
        JobContract contract,
        ContractSettlementCosts actualCosts,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken = default)
    {
        ContractSettlementSummary settlement =
            ContractSettlementEngine.Create(
                contract,
                actualCosts,
                settledAt);

        LedgerPostResult postResult =
            await _ledgerStore
                .PostAsync(
                    settlement.Transaction,
                    cancellationToken)
                .ConfigureAwait(false);

        decimal balance =
            await _ledgerStore
                .ReadCashBalanceAsync(cancellationToken)
                .ConfigureAwait(false);

        return new EconomySettlementResult(
            settlement,
            postResult,
            balance);
    }
}
