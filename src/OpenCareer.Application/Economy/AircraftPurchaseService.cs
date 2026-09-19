using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Finance;

namespace OpenCareer.Application.Economy;

public sealed record AircraftPurchaseResult(
    AircraftPurchaseSettlement Settlement,
    LedgerPostResult PostResult,
    decimal CashBalanceAfter)
{
    public bool WasNewlyPosted =>
        PostResult == LedgerPostResult.Posted;
}

public sealed class AircraftPurchaseService(
    IAircraftPurchaseLedgerStore store)
{
    private readonly IAircraftPurchaseLedgerStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    public Task<AircraftPurchaseResult> PurchaseWithCashAsync(
        Guid purchaseId,
        string ownershipId,
        DealerOffer offer,
        DealerStock currentStock,
        decimal requiredOperatingReserve,
        DateTimeOffset purchasedAt,
        CancellationToken cancellationToken = default) =>
        PurchaseAsync(
            purchaseId,
            ownershipId,
            offer,
            currentStock,
            AircraftPurchaseFunding.Cash,
            requiredOperatingReserve,
            purchasedAt,
            loanDecision: null,
            loanId: null,
            lenderId: null,
            loanTermCycles: null,
            cancellationToken);

    public Task<AircraftPurchaseResult> PurchaseWithFinancingAsync(
        Guid purchaseId,
        string ownershipId,
        DealerOffer offer,
        DealerStock currentStock,
        decimal requiredOperatingReserve,
        DateTimeOffset purchasedAt,
        LoanDecision loanDecision,
        Guid loanId,
        string lenderId,
        int loanTermCycles,
        CancellationToken cancellationToken = default) =>
        PurchaseAsync(
            purchaseId,
            ownershipId,
            offer,
            currentStock,
            AircraftPurchaseFunding.Financed,
            requiredOperatingReserve,
            purchasedAt,
            loanDecision,
            loanId,
            lenderId,
            loanTermCycles,
            cancellationToken);

    private async Task<AircraftPurchaseResult> PurchaseAsync(
        Guid purchaseId,
        string ownershipId,
        DealerOffer offer,
        DealerStock currentStock,
        AircraftPurchaseFunding funding,
        decimal requiredOperatingReserve,
        DateTimeOffset purchasedAt,
        LoanDecision? loanDecision,
        Guid? loanId,
        string? lenderId,
        int? loanTermCycles,
        CancellationToken cancellationToken)
    {
        decimal availableCash =
            await _store
                .ReadCashBalanceAsync(cancellationToken)
                .ConfigureAwait(false);

        AircraftPurchaseSettlement settlement =
            AircraftPurchaseAccountingEngine.Create(
                new AircraftPurchaseAccountingRequest(
                    purchaseId,
                    ownershipId,
                    offer,
                    currentStock,
                    funding,
                    availableCash,
                    requiredOperatingReserve,
                    purchasedAt,
                    loanDecision,
                    loanId,
                    lenderId,
                    loanTermCycles));

        LedgerPostResult postResult =
            await _store
                .PostAircraftPurchaseAsync(
                    settlement,
                    cancellationToken)
                .ConfigureAwait(false);

        decimal balance =
            await _store
                .ReadCashBalanceAsync(cancellationToken)
                .ConfigureAwait(false);

        return new AircraftPurchaseResult(
            settlement,
            postResult,
            balance);
    }
}
