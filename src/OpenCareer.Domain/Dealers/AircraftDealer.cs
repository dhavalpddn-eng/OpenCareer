using System.Collections.Immutable;
using System.Text.Json;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Finance;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Domain.Dealers;

public enum AircraftCondition { New, Used }
public sealed record DealerStock(string ListingId, AircraftCapabilityProfile Aircraft, AircraftCondition Condition,
    decimal AskingPrice, decimal AppraisedValue, decimal ConditionPercent, bool CivilianSaleAuthorized)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ListingId);
        ArgumentNullException.ThrowIfNull(Aircraft);
        Aircraft.Validate();
        if (!Enum.IsDefined(Condition) || AskingPrice <= 0 || AppraisedValue <= 0 ||
            ConditionPercent is <= 0 or > 100 || (Condition == AircraftCondition.New && ConditionPercent != 100))
            throw new ArgumentException("Invalid dealer stock.");
    }
}
public sealed record DealerProfile(string Id, string Name, string AirportIcao, bool SellsNew, bool SellsUsed,
    decimal MaximumListingPrice, decimal MarkupRate, decimal PromotionChance, decimal MaximumDiscountRate)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(AirportIcao);
        if ((!SellsNew && !SellsUsed) || MaximumListingPrice <= 0 || MarkupRate is < -0.5m or > 1m ||
            PromotionChance is < 0 or > 1 || MaximumDiscountRate is < 0 or > .25m)
            throw new ArgumentOutOfRangeException(nameof(DealerProfile));
    }
}
public sealed record DealerOffer(string DealerId, string ListingId, string AircraftId, AircraftCondition Condition,
    decimal ListPrice, decimal DiscountRate, decimal SalePrice, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

public static class AircraftDealer
{
    // Input is that dealer's inventory snapshot, not a global supported-aircraft whitelist.
    // Persist issued offers; refreshing a screen must reuse the same career/history snapshot and UTC period.
    public static ImmutableArray<DealerOffer> QuoteInventory(DealerProfile dealer, IEnumerable<DealerStock> inventory,
        CareerCreditHistory history, decimal dealerRelationship, ulong careerSeed, DateTimeOffset time)
    {
        dealer.Validate(); history.Validate(); ArgumentNullException.ThrowIfNull(inventory);
        if (dealerRelationship is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(dealerRelationship));
        var day = DateOnly.FromDateTime(time.UtcDateTime).DayNumber;
        var expires = new DateTimeOffset(time.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);
        var result = ImmutableArray.CreateBuilder<DealerOffer>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stock in inventory.OrderBy(x => x.ListingId, StringComparer.Ordinal))
        {
            stock.Validate();
            if (!ids.Add(stock.ListingId)) throw new ArgumentException("Duplicate dealer stock.");
            if (!stock.CivilianSaleAuthorized || !stock.Aircraft.Access.HasFlag(AircraftAccess.Civilian) ||
                stock.AskingPrice > dealer.MaximumListingPrice ||
                (stock.Condition == AircraftCondition.New ? !dealer.SellsNew : !dealer.SellsUsed)) continue;
            var random = DeterministicSeed.CreateStream(careerSeed, JsonSerializer.Serialize(new { dealer.Id, stock.ListingId, Day = day }));
            var standing = (history.CreditScore - 300m) / 550m;
            var chance = Math.Min(.75m, dealer.PromotionChance + .15m * standing + .10m * dealerRelationship / 100m);
            var discount = random.Chance((double)chance)
                ? dealer.MaximumDiscountRate * (.25m + .5m * standing + .25m * dealerRelationship / 100m) * (decimal)random.NextDouble(.5, 1)
                : 0m;
            var listPrice = decimal.Round(stock.AskingPrice * (1m + dealer.MarkupRate), 2);
            var salePrice = decimal.Round(listPrice * (1m - discount), 2);
            result.Add(new(dealer.Id, stock.ListingId, stock.Aircraft.AircraftId, stock.Condition, listPrice, discount, salePrice, new DateTimeOffset(time.UtcDateTime.Date, TimeSpan.Zero), expires));
        }
        return result.ToImmutable();
    }

    public static LoanDecision QuoteFinancing(DealerOffer offer, DealerStock currentStock, CareerCreditHistory history,
        LenderProfile lender, decimal deposit, int months, DateTimeOffset time, decimal marketRateAdjustment = 0m)
    {
        ValidateCurrentOffer(offer, currentStock, time);
        return CareerCredit.Evaluate(history, lender, new(offer.SalePrice, currentStock.AppraisedValue, deposit, months,
            currentStock.CivilianSaleAuthorized && currentStock.Aircraft.Access.HasFlag(AircraftAccess.Civilian)), marketRateAdjustment);
    }
    public static bool CanBuyWithCash(DealerOffer offer, DealerStock currentStock, CareerCreditHistory history, DateTimeOffset time)
    {
        history.Validate();
        ValidateCurrentOffer(offer, currentStock, time);
        return currentStock.CivilianSaleAuthorized && currentStock.Aircraft.Access.HasFlag(AircraftAccess.Civilian) &&
            offer.SalePrice <= Math.Max(0m, history.AvailableCash - history.RequiredOperatingReserve);
    }

    private static void ValidateCurrentOffer(DealerOffer offer, DealerStock currentStock, DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(currentStock);
        currentStock.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(offer.DealerId);
        if (offer.SalePrice <= 0 || offer.ListPrice < offer.SalePrice || offer.DiscountRate is < 0 or > .25m ||
            offer.ExpiresAt <= offer.IssuedAt || offer.Condition != currentStock.Condition ||
            offer.SalePrice != decimal.Round(offer.ListPrice * (1m - offer.DiscountRate), 2))
            throw new ArgumentException("Invalid dealer offer.");
        if (time < offer.IssuedAt || time >= offer.ExpiresAt || offer.ListingId != currentStock.ListingId || offer.AircraftId != currentStock.Aircraft.AircraftId)
            throw new InvalidOperationException("Offer expired or inventory does not match.");
    }

}

public static class InitialDealers
{
    // Fictional dealers. Actual inventory must be supplied from persisted assets and installed aircraft.
    public static DealerProfile UsedLocal { get; } = new("griffiss-used", "Griffiss Used Aircraft", "KRME", false, true, 250_000m, 0m, .20m, .08m);
    public static DealerProfile Factory { get; } = new("factory-direct", "Factory Aircraft Sales", "KRME", true, false, 100_000_000m, .04m, .10m, .05m);
    public static DealerProfile FleetBroker { get; } = new("fleet-broker", "Regional Fleet Exchange", "KRME", true, true, 20_000_000m, .02m, .25m, .12m);
}
