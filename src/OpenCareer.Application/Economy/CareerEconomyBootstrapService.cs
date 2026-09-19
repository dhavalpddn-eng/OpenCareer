using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public sealed record CareerEconomyBootstrapResult(
    CareerOpeningBalance OpeningBalance,
    LedgerPostResult PostResult,
    decimal CashBalanceAfter)
{
    public bool WasNewlyInitialized =>
        PostResult == LedgerPostResult.Posted;
}

public sealed class CareerEconomyBootstrapService(
    IEconomyLedgerStore ledgerStore)
{
    private readonly IEconomyLedgerStore _ledgerStore =
        ledgerStore ?? throw new ArgumentNullException(nameof(ledgerStore));

    public async Task<CareerEconomyBootstrapResult> InitializeAsync(
        Guid careerId,
        decimal openingCash,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        CareerOpeningBalance opening =
            CareerOpeningBalanceEngine.Create(
                careerId,
                openingCash,
                createdAt);

        LedgerPostResult postResult =
            await _ledgerStore
                .PostAsync(
                    opening.Transaction,
                    cancellationToken)
                .ConfigureAwait(false);

        decimal balance =
            await _ledgerStore
                .ReadCashBalanceAsync(cancellationToken)
                .ConfigureAwait(false);

        return new CareerEconomyBootstrapResult(
            opening,
            postResult,
            balance);
    }
}
