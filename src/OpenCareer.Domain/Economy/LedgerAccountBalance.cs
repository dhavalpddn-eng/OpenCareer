namespace OpenCareer.Domain.Economy;

public sealed record LedgerAccountBalance(
    LedgerAccountCode Account,
    decimal DebitTotal,
    decimal CreditTotal)
{
    public decimal NetDebit =>
        Math.Max(0m, DebitTotal - CreditTotal);

    public decimal NetCredit =>
        Math.Max(0m, CreditTotal - DebitTotal);

    public void Validate()
    {
        if (!Enum.IsDefined(Account))
            throw new ArgumentOutOfRangeException(nameof(Account));

        Money.Validate(DebitTotal, nameof(DebitTotal));
        Money.Validate(CreditTotal, nameof(CreditTotal));
    }
}
