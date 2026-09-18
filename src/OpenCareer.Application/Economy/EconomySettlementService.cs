using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

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
