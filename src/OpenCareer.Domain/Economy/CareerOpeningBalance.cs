namespace OpenCareer.Domain.Economy;

public sealed record CareerOpeningBalanceSettlement(
    string CareerId,
    decimal OpeningCash,
    EconomyLedgerTransaction Transaction)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CareerId);
        Money.Validate(OpeningCash, nameof(OpeningCash));
        ArgumentNullException.ThrowIfNull(Transaction);
        Transaction.Validate();

        if (Transaction.CashChange != OpeningCash)
            throw new ArgumentException("Opening-balance transaction does not reconcile to cash.");

        if (!string.Equals(Transaction.ReferenceType, "CareerOpeningBalance", StringComparison.Ordinal)
            || !string.Equals(Transaction.ReferenceId, CareerId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Opening-balance transaction reference does not match the career.");
        }
    }
}

public static class CareerOpeningBalanceEngine
{
    public static CareerOpeningBalanceSettlement Create(
        Guid transactionId,
        string careerId,
        decimal openingCash,
        DateTimeOffset openedAt)
    {
        if (transactionId == Guid.Empty)
            throw new ArgumentException("Opening-balance transaction ID is required.", nameof(transactionId));

        ArgumentException.ThrowIfNullOrWhiteSpace(careerId);
        Money.Validate(openingCash, nameof(openingCash));

        IReadOnlyList<LedgerPosting> postings =
            openingCash == 0m
                ? Array.Empty<LedgerPosting>()
                : [
                    LedgerPosting.DebitTo(
                        LedgerAccountCode.Cash,
                        openingCash,
                        "Career opening cash"),
                    LedgerPosting.CreditTo(
                        LedgerAccountCode.OpeningEquity,
                        openingCash,
                        "Career opening balance")
                ];

        var transaction = new EconomyLedgerTransaction(
            transactionId,
            $"career:{careerId}:opening-balance-v1",
            openedAt,
            $"Opening balance for career {careerId}",
            "CareerOpeningBalance",
            careerId,
            postings);
        transaction.Validate();

        var settlement = new CareerOpeningBalanceSettlement(
            careerId,
            openingCash,
            transaction);
        settlement.Validate();
        return settlement;
    }
}
