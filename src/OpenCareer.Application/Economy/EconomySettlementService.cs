using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public sealed record EconomySettlementResult(
    PersistedJobContract PersistedContract,
    ContractSettlementSummary Settlement,
    LedgerPostResult PostResult,
    decimal CashBalanceAfter)
{
    public bool WasNewlyPosted =>
        PostResult == LedgerPostResult.Posted;
}

public sealed class EconomySettlementService
{
    private readonly IJobContractStore _contractStore;
    private readonly IEconomyLedgerStore _ledgerStore;
    private readonly SemaphoreSlim _settlementGate = new(1, 1);

    public EconomySettlementService(
        IJobContractStore contractStore,
        IEconomyLedgerStore ledgerStore)
    {
        _contractStore =
            contractStore
            ?? throw new ArgumentNullException(nameof(contractStore));

        _ledgerStore =
            ledgerStore
            ?? throw new ArgumentNullException(nameof(ledgerStore));
    }

    public async Task<EconomySettlementResult> SettleCompletedContractAsync(
        Guid contractId,
        ContractSettlementCosts actualCosts,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken = default)
    {
        if (contractId == Guid.Empty)
        {
            throw new ArgumentException(
                "Contract ID is required.",
                nameof(contractId));
        }

        ArgumentNullException.ThrowIfNull(actualCosts);

        await _settlementGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            PersistedJobContract persisted =
                await _contractStore
                    .ReadJobContractAsync(
                        contractId,
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Persisted job contract was not found.");

            persisted.Validate();

            JobContract contract =
                persisted.Contract;

            if (contract.ContractId != contractId)
            {
                throw new InvalidOperationException(
                    "Persisted job-contract identity does not match the requested settlement.");
            }

            if (contract.Status != ContractStatus.Completed)
            {
                throw new InvalidOperationException(
                    "Only a persisted completed job contract can be settled.");
            }

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

            decimal cashBalanceAfter =
                await _ledgerStore
                    .ReadCashBalanceAsync(cancellationToken)
                    .ConfigureAwait(false);

            return new EconomySettlementResult(
                persisted,
                settlement,
                postResult,
                cashBalanceAfter);
        }
        finally
        {
            _settlementGate.Release();
        }
    }
}
