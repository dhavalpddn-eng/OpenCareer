namespace OpenCareer.Domain.Economy;

public enum LedgerAccountCode
{
    Cash = 0,
    ContractRevenue = 1,
    WageIncome = 2,
    ReimbursementIncome = 3,
    FuelExpense = 4,
    MaintenanceExpense = 5,
    AirportFeesExpense = 6,
    OtherOperatingExpense = 7,
    InsuranceExpense = 8,
    InterestExpense = 9,
    AircraftAsset = 10,
    LoanPayable = 11,
    StorageExpense = 12,
    OpeningEquity = 13
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
        new(
            account,
            LedgerMoney.Normalize(amount),
            0m,
            memo);

    public static LedgerPosting CreditTo(
        LedgerAccountCode account,
        decimal amount,
        string memo) =>
        new(
            account,
            0m,
            LedgerMoney.Normalize(amount),
            memo);

    public void Validate()
    {
        if (!Enum.IsDefined(Account))
            throw new ArgumentOutOfRangeException(nameof(Account));

        LedgerMoney.Validate(Debit, nameof(Debit));
        LedgerMoney.Validate(Credit, nameof(Credit));
        ArgumentException.ThrowIfNullOrWhiteSpace(Memo);

        bool hasDebit = Debit > 0m;
        bool hasCredit = Credit > 0m;

        if (hasDebit == hasCredit)
        {
            throw new ArgumentException(
                "A ledger posting must contain exactly one positive debit or credit.");
        }
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
    public decimal TotalDebits =>
        Postings.Sum(posting => posting.Debit);

    public decimal TotalCredits =>
        Postings.Sum(posting => posting.Credit);

    public decimal CashChange =>
        Postings
            .Where(
                posting =>
                    posting.Account
                    == LedgerAccountCode.Cash)
            .Sum(
                posting =>
                    posting.Debit
                    - posting.Credit);

    public void Validate()
    {
        if (TransactionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Ledger transaction ID is required.",
                nameof(TransactionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(Description);
        ArgumentException.ThrowIfNullOrWhiteSpace(ReferenceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(ReferenceId);
        ArgumentNullException.ThrowIfNull(Postings);

        if (IdempotencyKey.Length > 200)
        {
            throw new ArgumentException(
                "Ledger idempotency key is too long.",
                nameof(IdempotencyKey));
        }

        foreach (LedgerPosting posting in Postings)
            posting.Validate();

        decimal debits =
            LedgerMoney.Normalize(TotalDebits);

        decimal credits =
            LedgerMoney.Normalize(TotalCredits);

        if (debits != credits)
        {
            throw new ArgumentException(
                $"Ledger transaction is not balanced. Debits={debits:0.00}, credits={credits:0.00}.");
        }
    }
}

internal static class LedgerMoney
{
    internal static decimal Normalize(
        decimal value) =>
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
        {
            throw new ArgumentException(
                "Money values must be expressed to whole cents.",
                parameterName);
        }
    }
}
