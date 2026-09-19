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
        AircraftPurchaseSettlement? existing =
            await _store
                .ReadAircraftPurchaseAsync(
                    ownershipId,
                    cancellationToken)
                .ConfigureAwait(false);

        if (existing is not null)
        {
            ValidateRetryMatchesExisting(
                existing,
                purchaseId,
                offer,
                funding,
                purchasedAt,
                loanDecision,
                loanId,
                lenderId,
                loanTermCycles);

            decimal existingBalance =
                await _store
                    .ReadCashBalanceAsync(cancellationToken)
                    .ConfigureAwait(false);

            return new AircraftPurchaseResult(
                existing,
                LedgerPostResult.AlreadyPosted,
                existingBalance);
        }

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

    private static void ValidateRetryMatchesExisting(
        AircraftPurchaseSettlement existing,
        Guid purchaseId,
        DealerOffer offer,
        AircraftPurchaseFunding funding,
        DateTimeOffset purchasedAt,
        LoanDecision? loanDecision,
        Guid? loanId,
        string? lenderId,
        int? loanTermCycles)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(offer);

        bool basicMatch =
            existing.PurchaseId == purchaseId
            && string.Equals(
                existing.ListingId,
                offer.ListingId,
                StringComparison.Ordinal)
            && string.Equals(
                existing.AircraftId,
                offer.AircraftId,
                StringComparison.Ordinal)
            && existing.SalePrice == offer.SalePrice
            && existing.Transaction.OccurredAt.UtcTicks
                == purchasedAt.UtcTicks;

        if (!basicMatch)
        {
            throw new InvalidOperationException(
                "Aircraft ownership already has a different persisted purchase.");
        }

        if (funding == AircraftPurchaseFunding.Cash)
        {
            if (existing.Loan is not null
                || existing.FinancedPrincipal != 0m
                || existing.CashPaid != offer.SalePrice)
            {
                throw new InvalidOperationException(
                    "Persisted aircraft purchase funding does not match the retry.");
            }

            return;
        }

        if (loanDecision is null
            || loanId is null
            || lenderId is null
            || loanTermCycles is null
            || existing.Loan is null
            || existing.FinancedPrincipal != loanDecision.RequestedPrincipal
            || existing.Loan.LoanId != loanId.Value
            || !string.Equals(
                existing.Loan.LenderId,
                lenderId,
                StringComparison.Ordinal)
            || existing.Loan.TermCycles != loanTermCycles.Value
            || existing.Loan.AnnualRate != loanDecision.AnnualRate
            || existing.Loan.ScheduledPayment != loanDecision.MonthlyPayment)
        {
            throw new InvalidOperationException(
                "Persisted aircraft financing does not match the retry.");
        }
    }
}
