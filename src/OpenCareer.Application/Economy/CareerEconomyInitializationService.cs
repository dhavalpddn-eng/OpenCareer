using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public sealed record CareerEconomyInitializationResult(
    CareerOpeningBalanceSettlement Settlement,
    LedgerPostResult PostResult,
    decimal CashBalanceAfter)
{
    public bool WasNewlyPosted =>
        PostResult == LedgerPostResult.Posted;
}

public sealed class CareerEconomyInitializationService(
    IEconomyLedgerStore ledgerStore)
{
    private readonly IEconomyLedgerStore _ledgerStore =
        ledgerStore ?? throw new ArgumentNullException(nameof(ledgerStore));

    public async Task<CareerEconomyInitializationResult> EnsureOpeningBalanceAsync(
        Guid transactionId,
        string careerId,
        decimal openingCash,
        DateTimeOffset openedAt,
        CancellationToken cancellationToken = default)
    {
        CareerOpeningBalanceSettlement settlement =
            CareerOpeningBalanceEngine.Create(
                transactionId,
                careerId,
                openingCash,
                openedAt);

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

        return new CareerEconomyInitializationResult(
            settlement,
            postResult,
            balance);
    }
}
