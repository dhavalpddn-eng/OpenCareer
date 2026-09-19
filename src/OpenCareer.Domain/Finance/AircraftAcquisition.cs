using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Domain.Finance;

public enum AircraftAcquisitionMethod
{
    Cash,
    Financed
}

public sealed record AircraftLoanAgreement(
    Guid LoanId,
    string OwnershipId,
    string LenderId,
    decimal OriginalPrincipal,
    decimal AnnualRate,
    int TermMonths,
    decimal ScheduledMonthlyPayment,
    DateTimeOffset OriginatedAt)
{
    public void Validate()
    {
        if (LoanId == Guid.Empty)
            throw new ArgumentException("Loan ID is required.", nameof(LoanId));

        ArgumentException.ThrowIfNullOrWhiteSpace(OwnershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(LenderId);

        if (OriginalPrincipal <= 0m
            || decimal.Round(OriginalPrincipal, 2, MidpointRounding.AwayFromZero)
                != OriginalPrincipal)
        {
            throw new ArgumentOutOfRangeException(nameof(OriginalPrincipal));
        }

        if (AnnualRate is < 0m or > 0.5m)
            throw new ArgumentOutOfRangeException(nameof(AnnualRate));

        if (TermMonths is < 1 or > 240)
            throw new ArgumentOutOfRangeException(nameof(TermMonths));

        if (ScheduledMonthlyPayment <= 0m
            || decimal.Round(
                ScheduledMonthlyPayment,
                2,
                MidpointRounding.AwayFromZero)
                != ScheduledMonthlyPayment)
        {
            throw new ArgumentOutOfRangeException(nameof(ScheduledMonthlyPayment));
        }
    }
}

public sealed record AircraftLoanRepaymentState(
    Guid LoanId,
    string OwnershipId,
    decimal RemainingPrincipal,
    long Version,
    DateTimeOffset UpdatedAt)
{
    public static AircraftLoanRepaymentState Start(
        AircraftLoanAgreement agreement)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        agreement.Validate();

        return new AircraftLoanRepaymentState(
            agreement.LoanId,
            agreement.OwnershipId,
            agreement.OriginalPrincipal,
            Version: 0,
            agreement.OriginatedAt);
    }

    public AircraftLoanRepaymentState ApplyPrincipal(
        decimal principalPaid,
        DateTimeOffset updatedAt)
    {
        Validate();

        if (principalPaid < 0m
            || decimal.Round(
                principalPaid,
                2,
                MidpointRounding.AwayFromZero)
                != principalPaid)
        {
            throw new ArgumentOutOfRangeException(nameof(principalPaid));
        }

        if (principalPaid > RemainingPrincipal)
        {
            throw new InvalidOperationException(
                "Loan principal payment exceeds the remaining balance.");
        }

        if (updatedAt < UpdatedAt)
        {
            throw new ArgumentException(
                "Loan repayment state cannot move backward in time.",
                nameof(updatedAt));
        }

        if (principalPaid == 0m)
            return this;

        return this with
        {
            RemainingPrincipal =
                RemainingPrincipal - principalPaid,
            Version = checked(Version + 1),
            UpdatedAt = updatedAt
        };
    }

    public void Validate()
    {
        if (LoanId == Guid.Empty)
            throw new ArgumentException("Loan ID is required.", nameof(LoanId));

        ArgumentException.ThrowIfNullOrWhiteSpace(OwnershipId);

        if (RemainingPrincipal < 0m
            || decimal.Round(
                RemainingPrincipal,
                2,
                MidpointRounding.AwayFromZero)
                != RemainingPrincipal
            || Version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AircraftLoanRepaymentState));
        }
    }
}

public sealed record AircraftOwnershipRecord(
    string OwnershipId,
    string DealerId,
    string ListingId,
    string AircraftId,
    AircraftAcquisitionMethod AcquisitionMethod,
    decimal AcquisitionPrice,
    DateTimeOffset AcquiredAt,
    string StorageIcao,
    Guid? LoanId = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OwnershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DealerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ListingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(AircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(StorageIcao);

        if (!Enum.IsDefined(AcquisitionMethod))
            throw new ArgumentOutOfRangeException(nameof(AcquisitionMethod));

        if (AcquisitionPrice <= 0m
            || decimal.Round(
                AcquisitionPrice,
                2,
                MidpointRounding.AwayFromZero)
                != AcquisitionPrice)
        {
            throw new ArgumentOutOfRangeException(nameof(AcquisitionPrice));
        }

        if (StorageIcao.Length != 4
            || StorageIcao.Any(c => c is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "Storage airport must be a normalized four-letter ICAO identifier.",
                nameof(StorageIcao));
        }

        if (AcquisitionMethod == AircraftAcquisitionMethod.Cash
            && LoanId is not null)
        {
            throw new ArgumentException(
                "Cash aircraft ownership cannot reference a loan.");
        }

        if (AcquisitionMethod == AircraftAcquisitionMethod.Financed
            && LoanId is null)
        {
            throw new ArgumentException(
                "Financed aircraft ownership requires a loan.");
        }
    }
}

public sealed record AircraftAcquisitionSettlement(
    AircraftOwnershipRecord Ownership,
    AircraftLoanAgreement? Loan,
    decimal RequiredOperatingReserve,
    EconomyLedgerTransaction Transaction)
{
    public decimal CashPaid =>
        -Transaction.CashChange;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Ownership);
        Ownership.Validate();

        if (RequiredOperatingReserve < 0m
            || decimal.Round(
                RequiredOperatingReserve,
                2,
                MidpointRounding.AwayFromZero)
                != RequiredOperatingReserve)
        {
            throw new ArgumentOutOfRangeException(nameof(RequiredOperatingReserve));
        }

        ArgumentNullException.ThrowIfNull(Transaction);
        Transaction.Validate();

        if (Transaction.ReferenceType != "AircraftAcquisition"
            || Transaction.ReferenceId != Ownership.OwnershipId)
        {
            throw new ArgumentException(
                "Aircraft acquisition transaction reference is invalid.");
        }

        decimal assetDebit =
            Transaction.Postings
                .Where(posting =>
                    posting.Account
                        == LedgerAccountCode.AircraftAsset)
                .Sum(posting => posting.Debit);

        if (assetDebit != Ownership.AcquisitionPrice
            || Transaction.CashChange > 0m)
        {
            throw new ArgumentException(
                "Aircraft acquisition ledger transaction does not match the ownership record.");
        }

        decimal loanCredit =
            Transaction.Postings
                .Where(posting =>
                    posting.Account
                        == LedgerAccountCode.LoanPayable)
                .Sum(posting => posting.Credit);

        if (Ownership.AcquisitionMethod
                == AircraftAcquisitionMethod.Cash)
        {
            if (Loan is not null
                || loanCredit != 0m
                || CashPaid != Ownership.AcquisitionPrice)
            {
                throw new ArgumentException(
                    "Cash aircraft acquisition is inconsistent.");
            }
        }
        else
        {
            ArgumentNullException.ThrowIfNull(Loan);
            Loan.Validate();

            if (Ownership.LoanId != Loan.LoanId
                || !string.Equals(
                    Ownership.OwnershipId,
                    Loan.OwnershipId,
                    StringComparison.Ordinal)
                || loanCredit != Loan.OriginalPrincipal
                || CashPaid + Loan.OriginalPrincipal
                    != Ownership.AcquisitionPrice)
            {
                throw new ArgumentException(
                    "Financed aircraft acquisition is inconsistent.");
            }
        }
    }
}

public static class AircraftAcquisitionEngine
{
    public static AircraftAcquisitionSettlement CreateCash(
        Guid acquisitionId,
        string ownershipId,
        DealerOffer offer,
        DealerStock currentStock,
        CareerCreditHistory history,
        string storageIcao,
        DateTimeOffset acquiredAt)
    {
        ValidateIdentity(
            acquisitionId,
            ownershipId,
            storageIcao);

        if (!AircraftDealer.CanBuyWithCash(
                offer,
                currentStock,
                history,
                acquiredAt))
        {
            throw new InvalidOperationException(
                "Current cash, operating reserve, asset eligibility or dealer inventory does not permit this purchase.");
        }

        var ownership = new AircraftOwnershipRecord(
            ownershipId,
            offer.DealerId,
            offer.ListingId,
            offer.AircraftId,
            AircraftAcquisitionMethod.Cash,
            offer.SalePrice,
            acquiredAt,
            storageIcao);

        var transaction =
            CreatePurchaseTransaction(
                acquisitionId,
                ownership,
                cashPaid: offer.SalePrice,
                loanPrincipal: 0m);

        var result =
            new AircraftAcquisitionSettlement(
                ownership,
                Loan: null,
                history.RequiredOperatingReserve,
                transaction);

        result.Validate();
        return result;
    }

    public static AircraftAcquisitionSettlement CreateFinanced(
        Guid acquisitionId,
        string ownershipId,
        DealerOffer offer,
        DealerStock currentStock,
        CareerCreditHistory history,
        LenderProfile lender,
        decimal deposit,
        int termMonths,
        string storageIcao,
        DateTimeOffset acquiredAt,
        decimal marketRateAdjustment = 0m)
    {
        ValidateIdentity(
            acquisitionId,
            ownershipId,
            storageIcao);

        LoanDecision decision =
            AircraftDealer.QuoteFinancing(
                offer,
                currentStock,
                history,
                lender,
                deposit,
                termMonths,
                acquiredAt,
                marketRateAdjustment);

        if (!decision.Approved)
        {
            throw new InvalidOperationException(
                $"Aircraft financing was not approved: {decision.Reason}.");
        }

        var loan =
            new AircraftLoanAgreement(
                LoanId: acquisitionId,
                OwnershipId: ownershipId,
                LenderId: lender.Id,
                OriginalPrincipal:
                    decision.RequestedPrincipal,
                AnnualRate:
                    decision.AnnualRate,
                TermMonths:
                    termMonths,
                ScheduledMonthlyPayment:
                    decision.MonthlyPayment,
                OriginatedAt:
                    acquiredAt);
        loan.Validate();

        var ownership =
            new AircraftOwnershipRecord(
                ownershipId,
                offer.DealerId,
                offer.ListingId,
                offer.AircraftId,
                AircraftAcquisitionMethod.Financed,
                offer.SalePrice,
                acquiredAt,
                storageIcao,
                loan.LoanId);

        var transaction =
            CreatePurchaseTransaction(
                acquisitionId,
                ownership,
                cashPaid: deposit,
                loanPrincipal:
                    decision.RequestedPrincipal);

        var result =
            new AircraftAcquisitionSettlement(
                ownership,
                loan,
                history.RequiredOperatingReserve,
                transaction);

        result.Validate();
        return result;
    }

    private static EconomyLedgerTransaction CreatePurchaseTransaction(
        Guid acquisitionId,
        AircraftOwnershipRecord ownership,
        decimal cashPaid,
        decimal loanPrincipal)
    {
        var postings = new List<LedgerPosting>
        {
            LedgerPosting.DebitTo(
                LedgerAccountCode.AircraftAsset,
                ownership.AcquisitionPrice,
                "Aircraft acquisition")
        };

        if (cashPaid > 0m)
        {
            postings.Add(
                LedgerPosting.CreditTo(
                    LedgerAccountCode.Cash,
                    cashPaid,
                    "Aircraft purchase cash"));
        }

        if (loanPrincipal > 0m)
        {
            postings.Add(
                LedgerPosting.CreditTo(
                    LedgerAccountCode.LoanPayable,
                    loanPrincipal,
                    "Aircraft purchase financing"));
        }

        var transaction =
            new EconomyLedgerTransaction(
                acquisitionId,
                $"aircraft:{ownership.DealerId}:{ownership.ListingId}:purchase-v1",
                ownership.AcquiredAt,
                $"Aircraft purchase {ownership.AircraftId}",
                "AircraftAcquisition",
                ownership.OwnershipId,
                postings);

        transaction.Validate();
        return transaction;
    }

    private static void ValidateIdentity(
        Guid acquisitionId,
        string ownershipId,
        string storageIcao)
    {
        if (acquisitionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Acquisition ID is required.",
                nameof(acquisitionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            ownershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            storageIcao);

        if (storageIcao.Length != 4
            || storageIcao.Any(c => c is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "Storage airport must be a normalized four-letter ICAO identifier.",
                nameof(storageIcao));
        }
    }
}
