namespace OpenCareer.Domain.Economy;

public enum DeadheadPayer
{
    Player,
    Employer
}

public sealed record DeadheadTravelQuote(
    Guid TravelId,
    string OriginIcao,
    string DestinationIcao,
    DeadheadPayer Payer,
    decimal Fare,
    DateTimeOffset QuotedAt,
    DateTimeOffset ExpiresAt)
{
    public void Validate()
    {
        if (TravelId == Guid.Empty)
            throw new ArgumentException("Travel ID is required.", nameof(TravelId));

        ValidateIcao(OriginIcao, nameof(OriginIcao));
        ValidateIcao(DestinationIcao, nameof(DestinationIcao));

        if (string.Equals(
                OriginIcao,
                DestinationIcao,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Deadhead travel requires distinct origin and destination airports.");
        }

        if (!Enum.IsDefined(Payer))
            throw new ArgumentOutOfRangeException(nameof(Payer));

        Money.Validate(Fare, nameof(Fare));

        if (Payer == DeadheadPayer.Player && Fare <= 0m)
            throw new ArgumentException(
                "Player-paid travel requires a positive fare.",
                nameof(Fare));

        if (Payer == DeadheadPayer.Employer && Fare != 0m)
            throw new ArgumentException(
                "Employer-paid travel does not charge player cash.",
                nameof(Fare));

        if (ExpiresAt <= QuotedAt)
            throw new ArgumentException(
                "Deadhead quote expiry must follow quote time.");
    }

    private static void ValidateIcao(
        string value,
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length != 4
            || value.Any(character =>
                character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "A normalized four-letter ICAO identifier is required.",
                name);
        }
    }
}

public sealed record DeadheadTravelSettlement(
    DeadheadTravelQuote Quote,
    EconomyLedgerTransaction Transaction)
{
    public decimal PlayerCashCost =>
        -Transaction.CashChange;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Quote);
        Quote.Validate();

        ArgumentNullException.ThrowIfNull(Transaction);
        Transaction.Validate();

        if (Transaction.TransactionId != Quote.TravelId
            || Transaction.ReferenceType != "DeadheadTravel"
            || Transaction.ReferenceId
                != Quote.TravelId.ToString("D"))
        {
            throw new ArgumentException(
                "Deadhead settlement transaction reference is invalid.");
        }

        decimal expected =
            Quote.Payer == DeadheadPayer.Player
                ? Quote.Fare
                : 0m;

        if (PlayerCashCost != expected)
        {
            throw new ArgumentException(
                "Deadhead settlement does not reconcile to the quote.");
        }
    }
}

public static class DeadheadTravelSettlementEngine
{
    public static DeadheadTravelSettlement Create(
        DeadheadTravelQuote quote,
        DateTimeOffset purchasedAt)
    {
        ArgumentNullException.ThrowIfNull(quote);
        quote.Validate();

        if (purchasedAt < quote.QuotedAt
            || purchasedAt >= quote.ExpiresAt)
        {
            throw new InvalidOperationException(
                "Deadhead travel quote is not active.");
        }

        IReadOnlyList<LedgerPosting> postings =
            quote.Payer == DeadheadPayer.Player
                ?
                [
                    LedgerPosting.DebitTo(
                        LedgerAccountCode.TravelExpense,
                        quote.Fare,
                        "Personal deadhead travel"),
                    LedgerPosting.CreditTo(
                        LedgerAccountCode.Cash,
                        quote.Fare,
                        "Personal deadhead travel")
                ]
                : Array.Empty<LedgerPosting>();

        var transaction =
            new EconomyLedgerTransaction(
                quote.TravelId,
                $"deadhead:{quote.TravelId:D}:purchase-v1",
                purchasedAt,
                $"Deadhead travel {quote.OriginIcao}-{quote.DestinationIcao}",
                "DeadheadTravel",
                quote.TravelId.ToString("D"),
                postings);

        transaction.Validate();

        var result =
            new DeadheadTravelSettlement(
                quote,
                transaction);

        result.Validate();
        return result;
    }
}
