using System.Collections.Immutable;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Finance;
using OpenCareer.Domain.Maintenance;

namespace OpenCareer.Domain.Ownership;

public enum AircraftPurchaseMethod
{
    Cash,
    Financing
}

public enum OwnedAircraftStatus
{
    Active,
    Sold,
    WrittenOff
}

public enum AircraftStorageClass
{
    Light,
    Medium,
    Large,
    Heavy,
    Rotorcraft
}

public sealed record AircraftStorageOffer(
    string SlotId,
    string AirportIcao,
    AircraftStorageClass StorageClass,
    decimal MonthlyCost,
    bool Available,
    DateTimeOffset ExpiresAt)
{
    public void Validate(DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(AirportIcao);
        if (!Enum.IsDefined(StorageClass) || MonthlyCost < 0 || ExpiresAt <= at)
            throw new ArgumentException("Invalid or expired storage offer.");
    }
}

public sealed record AircraftStorageLease(
    string LeaseId,
    string OwnershipId,
    string SlotId,
    string AirportIcao,
    AircraftStorageClass StorageClass,
    decimal MonthlyCost,
    DateTimeOffset StartedAt,
    bool Active);

public sealed record AircraftInsurancePlan(
    string Id,
    string Name,
    decimal MonthlyPremium,
    decimal Deductible,
    decimal HullCoveragePercent,
    int DailyRedoAllowance)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (MonthlyPremium < 0 || Deductible < 0 || HullCoveragePercent is < 0 or > 1
            || DailyRedoAllowance is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(AircraftInsurancePlan));
    }
}

public sealed record AircraftInsurancePolicy(
    string PolicyId,
    string OwnershipId,
    AircraftInsurancePlan Plan,
    DateTimeOffset StartedAt,
    DateOnly? LastRedoUsedOn,
    bool Active)
{
    public bool CanUseRedo(DateOnly realLocalDay) =>
        Active && Plan.DailyRedoAllowance > 0 && LastRedoUsedOn != realLocalDay;

    public AircraftInsurancePolicy UseRedo(DateOnly realLocalDay)
    {
        Plan.Validate();
        if (!CanUseRedo(realLocalDay))
            throw new InvalidOperationException("No insurance redo is available for this day.");
        return this with { LastRedoUsedOn = realLocalDay };
    }
}

public sealed record OwnedAircraft(
    string OwnershipId,
    string CareerId,
    string AircraftId,
    string DisplayName,
    string SourceListingId,
    decimal PurchasePrice,
    decimal AcquiredConditionPercent,
    string CurrentAirportIcao,
    DateTimeOffset AcquiredAt,
    OwnedAircraftStatus Status)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OwnershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(CareerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(AircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceListingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(CurrentAirportIcao);
        if (PurchasePrice <= 0 || AcquiredConditionPercent is <= 0 or > 100 || !Enum.IsDefined(Status))
            throw new ArgumentException("Invalid owned aircraft.");
    }
}

public enum AircraftLoanStatus
{
    Active,
    PaidOff,
    Defaulted,
    Restructured
}

public sealed record LoanScheduleItem(
    int Sequence,
    DateTimeOffset DueAt,
    decimal Payment,
    decimal Principal,
    decimal Interest,
    decimal RemainingPrincipal);

public sealed record AircraftLoanAccount(
    string LoanId,
    string CareerId,
    string OwnershipId,
    string LenderId,
    decimal OriginalPrincipal,
    decimal RemainingPrincipal,
    decimal AnnualRate,
    int TermMonths,
    decimal MonthlyPayment,
    DateTimeOffset OriginatedAt,
    DateTimeOffset NextPaymentDueAt,
    int PaymentsMade,
    AircraftLoanStatus Status)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(LoanId);
        ArgumentException.ThrowIfNullOrWhiteSpace(CareerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(OwnershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(LenderId);
        if (OriginalPrincipal <= 0 || RemainingPrincipal < 0 || RemainingPrincipal > OriginalPrincipal
            || AnnualRate is < 0 or > 0.5m || TermMonths <= 0 || MonthlyPayment <= 0
            || PaymentsMade is < 0 || PaymentsMade > TermMonths || !Enum.IsDefined(Status))
            throw new ArgumentException("Invalid aircraft loan.");
    }

    public ImmutableArray<LoanScheduleItem> BuildSchedule()
    {
        Validate();
        var result = ImmutableArray.CreateBuilder<LoanScheduleItem>(TermMonths);
        var balance = OriginalPrincipal;
        var monthlyRate = AnnualRate / 12m;

        for (var sequence = 1; sequence <= TermMonths && balance > 0; sequence++)
        {
            var interest = decimal.Round(balance * monthlyRate, 2);
            var scheduled = Math.Min(MonthlyPayment, balance + interest);
            var principal = decimal.Round(scheduled - interest, 2);

            if (sequence == TermMonths || principal > balance)
            {
                principal = balance;
                scheduled = decimal.Round(principal + interest, 2);
            }

            balance = Math.Max(0m, decimal.Round(balance - principal, 2));
            result.Add(new(
                sequence,
                OriginatedAt.AddMonths(sequence),
                scheduled,
                principal,
                interest,
                balance));
        }

        return result.ToImmutable();
    }
}

public sealed record AircraftPurchasePlan(
    string OperationId,
    string CareerId,
    string OwnershipId,
    string InsurancePolicyId,
    string StorageLeaseId,
    string? LoanId,
    DealerOffer Offer,
    DealerStock Stock,
    AircraftPurchaseMethod Method,
    decimal Deposit,
    decimal CashDebit,
    LoanDecision? LoanDecision,
    string? LenderId,
    int LoanTermMonths,
    AircraftStorageOffer Storage,
    AircraftStorageClass RequiredStorageClass,
    AircraftInsurancePlan Insurance,
    MaintenanceProgram MaintenanceProgram,
    string DeliveryAirportIcao,
    DateTimeOffset PurchasedAt)
{
    public string OfferKey =>
        $"{Offer.DealerId}:{Offer.ListingId}:{Offer.IssuedAt.UtcDateTime.Ticks}";

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(CareerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(OwnershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(InsurancePolicyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(StorageLeaseId);
        ArgumentNullException.ThrowIfNull(Offer);
        ArgumentNullException.ThrowIfNull(Stock);
        Stock.Validate();
        ArgumentNullException.ThrowIfNull(Storage);
        Storage.Validate(PurchasedAt);
        ArgumentNullException.ThrowIfNull(Insurance);
        Insurance.Validate();
        ArgumentNullException.ThrowIfNull(MaintenanceProgram);
        MaintenanceProgram.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(DeliveryAirportIcao);

        if (!Enum.IsDefined(Method)
            || Offer.ListingId != Stock.ListingId
            || Offer.AircraftId != Stock.Aircraft.AircraftId
            || Offer.SalePrice <= 0
            || RequiredStorageClass != Storage.StorageClass
            || !Storage.Available
            || Deposit < 0
            || CashDebit <= 0)
        {
            throw new ArgumentException("Invalid aircraft purchase plan.");
        }

        if (Method == AircraftPurchaseMethod.Cash)
        {
            if (LoanDecision is not null || LenderId is not null || LoanId is not null || LoanTermMonths != 0 || Deposit != Offer.SalePrice)
                throw new ArgumentException("Cash purchase cannot contain financing.");
        }
        else
        {
            ArgumentNullException.ThrowIfNull(LoanDecision);
            var decision = LoanDecision ?? throw new ArgumentException("Financed purchase requires an approved loan decision.");
            if (!decision.Approved || string.IsNullOrWhiteSpace(LenderId) || string.IsNullOrWhiteSpace(LoanId)
                || LoanTermMonths <= 0 || Deposit <= 0 || decision.RequestedPrincipal != Offer.SalePrice - Deposit)
                throw new ArgumentException("Invalid financed purchase.");
        }
    }

    public OwnedAircraft CreateOwnedAircraft() =>
        new(
            OwnershipId,
            CareerId,
            Stock.Aircraft.AircraftId,
            Stock.Aircraft.DisplayName,
            Stock.ListingId,
            Offer.SalePrice,
            Stock.ConditionPercent,
            DeliveryAirportIcao,
            PurchasedAt,
            OwnedAircraftStatus.Active);

    public AircraftInsurancePolicy CreateInsurancePolicy() =>
        new(InsurancePolicyId, OwnershipId, Insurance, PurchasedAt, null, true);

    public AircraftStorageLease CreateStorageLease() =>
        new(StorageLeaseId, OwnershipId, Storage.SlotId, Storage.AirportIcao, Storage.StorageClass, Storage.MonthlyCost, PurchasedAt, true);

    public AircraftLoanAccount? CreateLoan()
    {
        if (Method != AircraftPurchaseMethod.Financing)
            return null;

        var decision = LoanDecision!;
        var loan = new AircraftLoanAccount(
            LoanId!,
            CareerId,
            OwnershipId,
            LenderId!,
            decision.RequestedPrincipal,
            decision.RequestedPrincipal,
            decision.AnnualRate,
            LoanTermMonths,
            decision.MonthlyPayment,
            PurchasedAt,
            PurchasedAt.AddMonths(1),
            0,
            AircraftLoanStatus.Active);
        loan.Validate();
        return loan;
    }
}

public sealed record AircraftPurchaseReceipt(
    string OperationId,
    string OwnershipId,
    string? LoanId,
    decimal PurchasePrice,
    decimal CashDebit,
    DateTimeOffset PurchasedAt);

public static class AircraftPurchasePlanner
{
    public static AircraftPurchasePlan PlanCash(
        string operationId,
        string careerId,
        string ownershipId,
        string insurancePolicyId,
        string storageLeaseId,
        DealerOffer offer,
        DealerStock stock,
        CareerCreditHistory history,
        AircraftStorageOffer storage,
        AircraftStorageClass requiredStorageClass,
        AircraftInsurancePlan insurance,
        MaintenanceProgram maintenanceProgram,
        DateTimeOffset time)
    {
        if (!AircraftDealer.CanBuyWithCash(offer, stock, history, time))
            throw new InvalidOperationException("Career cannot buy this aircraft with cash.");

        ValidateAncillary(history, storage, requiredStorageClass, insurance, time);

        var cashDebit = offer.SalePrice + storage.MonthlyCost + insurance.MonthlyPremium;
        EnsureReserve(history, cashDebit);

        var plan = new AircraftPurchasePlan(
            operationId,
            careerId,
            ownershipId,
            insurancePolicyId,
            storageLeaseId,
            null,
            offer,
            stock,
            AircraftPurchaseMethod.Cash,
            offer.SalePrice,
            cashDebit,
            null,
            null,
            0,
            storage,
            requiredStorageClass,
            insurance,
            maintenanceProgram,
            storage.AirportIcao,
            time);
        plan.Validate();
        return plan;
    }

    public static AircraftPurchasePlan PlanFinanced(
        string operationId,
        string careerId,
        string ownershipId,
        string insurancePolicyId,
        string storageLeaseId,
        string loanId,
        DealerOffer offer,
        DealerStock stock,
        CareerCreditHistory history,
        LenderProfile lender,
        decimal deposit,
        int termMonths,
        AircraftStorageOffer storage,
        AircraftStorageClass requiredStorageClass,
        AircraftInsurancePlan insurance,
        MaintenanceProgram maintenanceProgram,
        DateTimeOffset time,
        decimal marketRateAdjustment = 0m)
    {
        var decision = AircraftDealer.QuoteFinancing(
            offer,
            stock,
            history,
            lender,
            deposit,
            termMonths,
            time,
            marketRateAdjustment);

        if (!decision.Approved)
            throw new InvalidOperationException($"Financing declined: {decision.Reason}.");

        ValidateAncillary(history, storage, requiredStorageClass, insurance, time);

        var cashDebit = deposit + storage.MonthlyCost + insurance.MonthlyPremium;
        EnsureReserve(history, cashDebit);

        var plan = new AircraftPurchasePlan(
            operationId,
            careerId,
            ownershipId,
            insurancePolicyId,
            storageLeaseId,
            loanId,
            offer,
            stock,
            AircraftPurchaseMethod.Financing,
            deposit,
            cashDebit,
            decision,
            lender.Id,
            termMonths,
            storage,
            requiredStorageClass,
            insurance,
            maintenanceProgram,
            storage.AirportIcao,
            time);
        plan.Validate();
        return plan;
    }

    private static void ValidateAncillary(
        CareerCreditHistory history,
        AircraftStorageOffer storage,
        AircraftStorageClass requiredStorageClass,
        AircraftInsurancePlan insurance,
        DateTimeOffset time)
    {
        history.Validate();
        storage.Validate(time);
        insurance.Validate();
        if (!storage.Available || storage.StorageClass != requiredStorageClass)
            throw new InvalidOperationException("Compatible storage is not available.");
    }

    private static void EnsureReserve(CareerCreditHistory history, decimal cashDebit)
    {
        if (history.AvailableCash - cashDebit < history.RequiredOperatingReserve)
            throw new InvalidOperationException("Purchase would violate the required operating reserve.");
    }
}

public static class InitialAircraftInsurance
{
    public static AircraftInsurancePlan LiabilityOnly { get; } =
        new("liability", "Liability only", 180m, 5_000m, 0m, 0);

    public static AircraftInsurancePlan StandardHull { get; } =
        new("standard-hull", "Standard hull", 420m, 2_500m, 0.80m, 1);

    public static AircraftInsurancePlan PremiumHull { get; } =
        new("premium-hull", "Premium hull", 690m, 1_000m, 1m, 1);
}
