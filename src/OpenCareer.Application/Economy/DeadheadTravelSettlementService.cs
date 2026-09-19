using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public interface ICashGuardedEconomyLedgerStore : IEconomyLedgerStore
{
    Task<LedgerPostResult> PostWithMinimumCashAsync(
        EconomyLedgerTransaction transaction,
        decimal minimumCashAfter,
        CancellationToken cancellationToken = default);
}

public sealed record DeadheadTravelSettlementResult(
    DeadheadTravelSettlement Settlement,
    LedgerPostResult PostResult,
    decimal CashBalanceAfter)
{
    public bool WasNewlyPosted =>
        PostResult == LedgerPostResult.Posted;
}

public sealed class DeadheadTravelSettlementService(
    ICashGuardedEconomyLedgerStore ledgerStore)
{
    private readonly ICashGuardedEconomyLedgerStore _ledgerStore =
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
                .PostWithMinimumCashAsync(
                    settlement.Transaction,
                    minimumCashAfter: 0m,
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
