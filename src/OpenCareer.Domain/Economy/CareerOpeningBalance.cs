namespace OpenCareer.Domain.Economy;

public sealed record CareerOpeningBalance(
    Guid CareerId,
    decimal OpeningCash,
    EconomyLedgerTransaction Transaction)
{
    public void Validate()
    {
        if (CareerId == Guid.Empty)
            throw new ArgumentException("Career ID is required.", nameof(CareerId));

        Money.Validate(OpeningCash, nameof(OpeningCash));
        ArgumentNullException.ThrowIfNull(Transaction);
        Transaction.Validate();

        if (Transaction.TransactionId != CareerId
            || Transaction.CashChange != OpeningCash)
        {
            throw new ArgumentException(
                "Opening-balance transaction does not reconcile to the career.");
        }
    }
}

public static class CareerOpeningBalanceEngine
{
    public static CareerOpeningBalance Create(
        Guid careerId,
        decimal openingCash,
        DateTimeOffset createdAt)
    {
        if (careerId == Guid.Empty)
            throw new ArgumentException("Career ID is required.", nameof(careerId));

        Money.Validate(openingCash, nameof(openingCash));

        IReadOnlyList<LedgerPosting> postings =
            openingCash == 0m
                ? Array.Empty<LedgerPosting>()
                :
                [
                    LedgerPosting.DebitTo(
                        LedgerAccountCode.Cash,
                        openingCash,
                        "Career opening cash"),
                    LedgerPosting.CreditTo(
                        LedgerAccountCode.OpeningEquity,
                        openingCash,
                        "Career opening equity")
                ];

        var transaction = new EconomyLedgerTransaction(
            TransactionId: careerId,
            IdempotencyKey: $"career:{careerId:D}:opening-balance-v1",
            OccurredAt: createdAt,
            Description: "Career opening balance",
            ReferenceType: "Career",
            ReferenceId: careerId.ToString("D"),
            Postings: postings);

        transaction.Validate();

        var result = new CareerOpeningBalance(
            careerId,
            openingCash,
            transaction);

        result.Validate();
        return result;
    }
}
