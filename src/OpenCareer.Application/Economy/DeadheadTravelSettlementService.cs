using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public sealed record DeadheadTravelSettlementResult(
    DeadheadTravelSettlement Settlement,
    LedgerPostResult PostResult,
    decimal CashBalanceAfter)
{
    public bool WasNewlyPosted =>
        PostResult == LedgerPostResult.Posted;
}

public sealed class DeadheadTravelSettlementService(
    IEconomyLedgerStore ledgerStore)
{
    private readonly IEconomyLedgerStore _ledgerStore =
        ledgerStore ?? throw new ArgumentNullException(nameof(ledgerStore));

    public async Task<DeadheadTravelSettlementResult> SettleAsync(
        DeadheadTravelQuote quote,
        DateTimeOffset purchasedAt,
        CancellationToken cancellationToken = default)
    {
        DeadheadTravelSettlement settlement =
            DeadheadTravelSettlementEngine.Create(
                quote,
                purchasedAt);

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

        return new DeadheadTravelSettlementResult(
            settlement,
            postResult,
            balance);
    }
}
