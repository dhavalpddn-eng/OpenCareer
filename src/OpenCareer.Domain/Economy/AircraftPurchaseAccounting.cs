using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Finance;

namespace OpenCareer.Domain.Economy;

public enum AircraftPurchaseFunding
{
    Cash,
    Financed
}

public sealed record AircraftPurchaseAccountingRequest(
    Guid PurchaseId,
    string OwnershipId,
    DealerOffer Offer,
    DealerStock CurrentStock,
    AircraftPurchaseFunding Funding,
    decimal AvailableCash,
    decimal RequiredOperatingReserve,
    DateTimeOffset PurchasedAt,
    LoanDecision? LoanDecision = null,
    Guid? LoanId = null,
    string? LenderId = null,
    int? LoanTermCycles = null)
{
    public void Validate()
    {
        if (PurchaseId == Guid.Empty)
            throw new ArgumentException("Purchase ID is required.", nameof(PurchaseId));

        ArgumentException.ThrowIfNullOrWhiteSpace(OwnershipId);
        ArgumentNullException.ThrowIfNull(Offer);
        ArgumentNullException.ThrowIfNull(CurrentStock);

        if (!Enum.IsDefined(Funding))
            throw new ArgumentOutOfRangeException(nameof(Funding));

        Money.Validate(AvailableCash, nameof(AvailableCash));
        Money.Validate(RequiredOperatingReserve, nameof(RequiredOperatingReserve));

        AircraftDealer.ValidateOfferForPurchase(
            Offer,
            CurrentStock,
            PurchasedAt);

        if (!CurrentStock.CivilianSaleAuthorized
            || !CurrentStock.Aircraft.Access.HasFlag(AircraftAccess.Civilian))
        {
            throw new InvalidOperationException("Aircraft is not eligible for civilian ownership.");
        }

        if (Funding == AircraftPurchaseFunding.Cash)
        {
            if (LoanDecision is not null
                || LoanId is not null
                || LenderId is not null
                || LoanTermCycles is not null)
            {
                throw new ArgumentException("Cash purchase cannot include loan terms.");
            }

            if (Offer.SalePrice
                > Math.Max(0m, AvailableCash - RequiredOperatingReserve))
            {
                throw new InvalidOperationException(
                    "Cash purchase would violate the required operating reserve.");
            }

            return;
        }

        ArgumentNullException.ThrowIfNull(LoanDecision);
        if (!LoanDecision.Approved
            || LoanDecision.Reason != LoanDeclineReason.None)
        {
            throw new InvalidOperationException("Financed purchase requires an approved loan decision.");
        }


        if (LoanId is null || LoanId == Guid.Empty)
            throw new ArgumentException("Financed purchase requires a loan ID.", nameof(LoanId));

        ArgumentException.ThrowIfNullOrWhiteSpace(LenderId);

        if (LoanTermCycles is null or < 1 or > 240)
            throw new ArgumentOutOfRangeException(nameof(LoanTermCycles));

        decimal expectedPrincipal =
            Money.Normalize(
                Offer.SalePrice
                - Deposit);

        if (LoanDecision.RequestedPrincipal != expectedPrincipal)
            throw new ArgumentException(
                "Approved loan principal does not match the purchase price and deposit.");

        if (Deposit
            > Math.Max(0m, AvailableCash - RequiredOperatingReserve))
        {
            throw new InvalidOperationException(
                "Loan deposit would violate the required operating reserve.");
        }
    }

    public decimal Deposit =>
        Funding == AircraftPurchaseFunding.Cash
            ? Offer.SalePrice
            : Money.Normalize(
                Offer.SalePrice
                - (LoanDecision?.RequestedPrincipal ?? 0m));
}

public sealed record AircraftPurchaseSettlement(
    Guid PurchaseId,
    string OwnershipId,
    string ListingId,
    string AircraftId,
    decimal SalePrice,
    decimal CashPaid,
    decimal FinancedPrincipal,
    AircraftLoanAgreement? Loan,
    EconomyLedgerTransaction Transaction)
{
    public void Validate()
    {
        if (PurchaseId == Guid.Empty)
            throw new ArgumentException("Purchase ID is required.", nameof(PurchaseId));

        ArgumentException.ThrowIfNullOrWhiteSpace(OwnershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ListingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(AircraftId);

        Money.Validate(SalePrice, nameof(SalePrice));
        Money.Validate(CashPaid, nameof(CashPaid));
        Money.Validate(FinancedPrincipal, nameof(FinancedPrincipal));

        if (SalePrice <= 0m
            || Money.Normalize(CashPaid + FinancedPrincipal) != SalePrice)
        {
            throw new ArgumentException("Aircraft purchase funding does not reconcile to sale price.");
        }

        Loan?.Validate();

        if ((Loan is null) != (FinancedPrincipal == 0m))
            throw new ArgumentException("Loan agreement presence does not match financed principal.");

        if (Loan is not null
            && (Loan.OriginalPrincipal != FinancedPrincipal
                || !string.Equals(
                    Loan.OwnershipId,
                    OwnershipId,
                    StringComparison.Ordinal)))
        {
            throw new ArgumentException("Loan agreement does not match the aircraft purchase.");
        }

        ArgumentNullException.ThrowIfNull(Transaction);
        Transaction.Validate();

        if (Transaction.TransactionId != PurchaseId
            || Transaction.CashChange != -CashPaid)
        {
            throw new ArgumentException("Aircraft purchase ledger transaction does not reconcile.");
        }
    }
}

public static class AircraftPurchaseAccountingEngine
{
    public static AircraftPurchaseSettlement Create(
        AircraftPurchaseAccountingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        decimal deposit = request.Deposit;
        decimal financed =
            request.Funding == AircraftPurchaseFunding.Financed
                ? request.LoanDecision!.RequestedPrincipal
                : 0m;

        AircraftLoanAgreement? loan =
            request.Funding == AircraftPurchaseFunding.Financed
                ? AircraftLoanAgreement.FromApprovedDecision(
                    request.LoanId!.Value,
                    request.OwnershipId,
                    request.LenderId!,
                    request.LoanDecision!,
                    request.LoanTermCycles!.Value,
                    request.PurchasedAt)
                : null;

        var postings = new List<LedgerPosting>
        {
            LedgerPosting.DebitTo(
                LedgerAccountCode.AircraftAsset,
                request.Offer.SalePrice,
                $"Aircraft asset {request.CurrentStock.Aircraft.DisplayName}")
        };

        if (deposit > 0m)
        {
            postings.Add(
                LedgerPosting.CreditTo(
                    LedgerAccountCode.Cash,
                    deposit,
                    "Aircraft purchase cash/deposit"));
        }

        if (financed > 0m)
        {
            postings.Add(
                LedgerPosting.CreditTo(
                    LedgerAccountCode.LoanPayable,
                    financed,
                    "Aircraft purchase financing"));
        }

        var transaction = new EconomyLedgerTransaction(
            request.PurchaseId,
            $"aircraft-purchase:{request.PurchaseId:D}:v1",
            request.PurchasedAt,
            $"Purchase {request.CurrentStock.Aircraft.DisplayName}",
            "AircraftPurchase",
            request.OwnershipId,
            postings);
        transaction.Validate();

        var settlement = new AircraftPurchaseSettlement(
            request.PurchaseId,
            request.OwnershipId,
            request.Offer.ListingId,
            request.Offer.AircraftId,
            request.Offer.SalePrice,
            deposit,
            financed,
            loan,
            transaction);
        settlement.Validate();
        return settlement;
    }
}
