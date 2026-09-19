using OpenCareer.Domain.Careers;

namespace OpenCareer.Domain.Economy;

public enum LedgerAccountCode
{
    Cash,
    ContractRevenue,
    WageIncome,
    ReimbursementIncome,
    FuelExpense,
    MaintenanceExpense,
    AirportFeesExpense,
    OtherOperatingExpense,
    InsuranceExpense,
    StorageExpense,
    InterestExpense,
    AircraftAsset,
    LoanPayable
}

public sealed record LedgerPosting(
    LedgerAccountCode Account,
    decimal Debit,
    decimal Credit,
    string Memo)
{
    public static LedgerPosting DebitTo(
        LedgerAccountCode account,
        decimal amount,
        string memo) =>
        new(account, Money.Normalize(amount), 0m, memo);

    public static LedgerPosting CreditTo(
        LedgerAccountCode account,
        decimal amount,
        string memo) =>
        new(account, 0m, Money.Normalize(amount), memo);

    public void Validate()
    {
        if (!Enum.IsDefined(Account))
            throw new ArgumentOutOfRangeException(nameof(Account));

        Money.Validate(Debit, nameof(Debit));
        Money.Validate(Credit, nameof(Credit));
        ArgumentException.ThrowIfNullOrWhiteSpace(Memo);

        bool hasDebit = Debit > 0m;
        bool hasCredit = Credit > 0m;
        if (hasDebit == hasCredit)
            throw new ArgumentException(
                "A ledger posting must contain exactly one positive debit or credit.");
    }
}

public sealed record EconomyLedgerTransaction(
    Guid TransactionId,
    string IdempotencyKey,
    DateTimeOffset OccurredAt,
    string Description,
    string ReferenceType,
    string ReferenceId,
    IReadOnlyList<LedgerPosting> Postings)
{
    public decimal TotalDebits => Postings.Sum(posting => posting.Debit);
    public decimal TotalCredits => Postings.Sum(posting => posting.Credit);

    public decimal CashChange =>
        Postings
            .Where(posting => posting.Account == LedgerAccountCode.Cash)
            .Sum(posting => posting.Debit - posting.Credit);

    public void Validate()
    {
        if (TransactionId == Guid.Empty)
            throw new ArgumentException("Ledger transaction ID is required.", nameof(TransactionId));

        ArgumentException.ThrowIfNullOrWhiteSpace(IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(Description);
        ArgumentException.ThrowIfNullOrWhiteSpace(ReferenceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(ReferenceId);
        ArgumentNullException.ThrowIfNull(Postings);

        if (IdempotencyKey.Length > 200)
            throw new ArgumentException("Ledger idempotency key is too long.", nameof(IdempotencyKey));

        foreach (var posting in Postings)
            posting.Validate();

        decimal debits = Money.Normalize(TotalDebits);
        decimal credits = Money.Normalize(TotalCredits);
        if (debits != credits)
        {
            throw new ArgumentException(
                $"Ledger transaction is not balanced. Debits={debits:0.00}, credits={credits:0.00}.");
        }
    }
}

public sealed record ContractSettlementCosts(
    decimal FuelCost,
    decimal MaintenanceReserveCost,
    decimal AirportFees,
    decimal OtherOperatingCosts = 0m)
{
    public decimal Total =>
        Money.Normalize(
            FuelCost
            + MaintenanceReserveCost
            + AirportFees
            + OtherOperatingCosts);

    public void Validate()
    {
        Money.Validate(FuelCost, nameof(FuelCost));
        Money.Validate(MaintenanceReserveCost, nameof(MaintenanceReserveCost));
        Money.Validate(AirportFees, nameof(AirportFees));
        Money.Validate(OtherOperatingCosts, nameof(OtherOperatingCosts));
    }
}

public sealed record ContractSettlementSummary(
    Guid ContractId,
    decimal GrossCashReceipt,
    decimal PlayerOperatingCosts,
    decimal NetCashChange,
    EconomyLedgerTransaction Transaction)
{
    public void Validate()
    {
        if (ContractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(ContractId));

        Money.Validate(GrossCashReceipt, nameof(GrossCashReceipt));
        Money.Validate(PlayerOperatingCosts, nameof(PlayerOperatingCosts));

        if (Money.Normalize(GrossCashReceipt - PlayerOperatingCosts) != NetCashChange)
            throw new ArgumentException("Settlement cash summary does not reconcile.");

        ArgumentNullException.ThrowIfNull(Transaction);
        Transaction.Validate();

        if (Transaction.TransactionId != ContractId
            || Transaction.CashChange != NetCashChange)
        {
            throw new ArgumentException("Settlement transaction does not reconcile to the contract summary.");
        }
    }
}

public static class ContractSettlementEngine
{
    public static ContractSettlementSummary Create(
        JobContract contract,
        ContractSettlementCosts actualCosts,
        DateTimeOffset settledAt)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(actualCosts);

        contract.Validate();
        actualCosts.Validate();

        if (contract.Status != ContractStatus.Completed
            || contract.CompletedAt is not { } completedAt)
        {
            throw new InvalidOperationException(
                "Only a completed contract can be settled.");
        }

        if (settledAt < completedAt)
            throw new ArgumentException(
                "Settlement cannot precede verified contract completion.",
                nameof(settledAt));

        ContractCompensation compensation = contract.Compensation;

        decimal grossReceipt = Money.Normalize(
            compensation.Model switch
            {
                CompensationModel.CompanyRevenue => compensation.GrossCustomerRevenue,
                CompensationModel.MissionFee =>
                    compensation.GrossCustomerRevenue > 0m
                        ? compensation.GrossCustomerRevenue
                        : compensation.PilotCompensation,
                CompensationModel.PilotWage
                    or CompensationModel.SalaryDuty
                    or CompensationModel.Reimbursement =>
                    compensation.PilotCompensation,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(contract),
                    "Unsupported compensation model.")
            });

        decimal fuelCost = compensation.EmployerCoversFuel
            ? 0m
            : Money.Normalize(actualCosts.FuelCost);
        decimal maintenanceCost = compensation.EmployerCoversMaintenance
            ? 0m
            : Money.Normalize(actualCosts.MaintenanceReserveCost);
        decimal airportFees = compensation.EmployerCoversAirportFees
            ? 0m
            : Money.Normalize(actualCosts.AirportFees);
        decimal otherCosts = Money.Normalize(actualCosts.OtherOperatingCosts);

        decimal playerCosts = Money.Normalize(
            fuelCost
            + maintenanceCost
            + airportFees
            + otherCosts);

        var postings = new List<LedgerPosting>();

        if (grossReceipt > 0m)
        {
            postings.Add(
                LedgerPosting.DebitTo(
                    LedgerAccountCode.Cash,
                    grossReceipt,
                    "Contract cash receipt"));

            postings.Add(
                LedgerPosting.CreditTo(
                    IncomeAccountFor(compensation.Model),
                    grossReceipt,
                    "Contract income"));
        }

        AddExpense(
            postings,
            LedgerAccountCode.FuelExpense,
            fuelCost,
            "Fuel cost");
        AddExpense(
            postings,
            LedgerAccountCode.MaintenanceExpense,
            maintenanceCost,
            "Maintenance reserve");
        AddExpense(
            postings,
            LedgerAccountCode.AirportFeesExpense,
            airportFees,
            "Airport and navigation fees");
        AddExpense(
            postings,
            LedgerAccountCode.OtherOperatingExpense,
            otherCosts,
            "Other operating costs");

        var transaction = new EconomyLedgerTransaction(
            TransactionId: contract.ContractId,
            IdempotencyKey: $"contract:{contract.ContractId:D}:settlement-v1",
            OccurredAt: settledAt,
            Description: $"{contract.Kind} contract settlement {contract.OriginIcao}-{contract.DestinationIcao}",
            ReferenceType: nameof(JobContract),
            ReferenceId: contract.ContractId.ToString("D"),
            Postings: postings);

        transaction.Validate();

        var summary = new ContractSettlementSummary(
            contract.ContractId,
            grossReceipt,
            playerCosts,
            Money.Normalize(grossReceipt - playerCosts),
            transaction);

        summary.Validate();
        return summary;
    }

    private static LedgerAccountCode IncomeAccountFor(
        CompensationModel model) =>
        model switch
        {
            CompensationModel.PilotWage or CompensationModel.SalaryDuty =>
                LedgerAccountCode.WageIncome,
            CompensationModel.Reimbursement =>
                LedgerAccountCode.ReimbursementIncome,
            CompensationModel.MissionFee or CompensationModel.CompanyRevenue =>
                LedgerAccountCode.ContractRevenue,
            _ => throw new ArgumentOutOfRangeException(nameof(model))
        };

    private static void AddExpense(
        ICollection<LedgerPosting> postings,
        LedgerAccountCode expenseAccount,
        decimal amount,
        string memo)
    {
        if (amount <= 0m)
            return;

        postings.Add(
            LedgerPosting.DebitTo(
                expenseAccount,
                amount,
                memo));
        postings.Add(
            LedgerPosting.CreditTo(
                LedgerAccountCode.Cash,
                amount,
                memo));
    }
}

internal static class Money
{
    internal static decimal Normalize(decimal value) =>
        decimal.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);

    internal static void Validate(
        decimal value,
        string parameterName)
    {
        if (value < 0m)
            throw new ArgumentOutOfRangeException(parameterName);

        if (Normalize(value) != value)
            throw new ArgumentException(
                "Money values must be expressed to whole cents.",
                parameterName);
    }
}
