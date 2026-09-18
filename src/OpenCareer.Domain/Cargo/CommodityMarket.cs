using System.Collections.Immutable;

namespace OpenCareer.Domain.Cargo;

public enum CommodityCategory
{
    Agriculture,
    Food,
    ConsumerElectronics,
    Medical,
    Industrial,
    MailAndParcels,
    Horticulture
}

[Flags]
public enum CommodityHandling
{
    None = 0,
    Refrigerated = 1 << 0,
    Frozen = 1 << 1,
    Fragile = 1 << 2,
    TheftSensitive = 1 << 3,
    LiveOrganism = 1 << 4,
    TimeSensitive = 1 << 5,
    Hazardous = 1 << 6,
    HighSecurity = 1 << 7
}

public sealed record CommodityDefinition(
    string Id,
    string DisplayName,
    CommodityCategory Category,
    string TradeUnit,
    decimal ReferenceWholesaleValuePerUnit,
    double TypicalUnitMassPounds,
    double TypicalUnitVolumeCubicFeet,
    double Perishability,
    double Fragility,
    double TheftRisk,
    CommodityHandling Handling)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(TradeUnit);

        if (!Enum.IsDefined(Category)
            || ReferenceWholesaleValuePerUnit <= 0
            || !double.IsFinite(TypicalUnitMassPounds) || TypicalUnitMassPounds <= 0
            || !double.IsFinite(TypicalUnitVolumeCubicFeet) || TypicalUnitVolumeCubicFeet <= 0
            || !double.IsFinite(Perishability) || Perishability is < 0 or > 1
            || !double.IsFinite(Fragility) || Fragility is < 0 or > 1
            || !double.IsFinite(TheftRisk) || TheftRisk is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(CommodityDefinition));
        }
    }
}

public sealed record CommodityMarketContext(
    double EconomyPriceFactor,
    double SupplyPressure,
    double DemandPressure,
    double EventPriceFactor)
{
    public void Validate()
    {
        if (!double.IsFinite(EconomyPriceFactor) || EconomyPriceFactor <= 0
            || !double.IsFinite(SupplyPressure) || SupplyPressure <= 0
            || !double.IsFinite(DemandPressure) || DemandPressure <= 0
            || !double.IsFinite(EventPriceFactor) || EventPriceFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CommodityMarketContext));
        }
    }
}

public sealed record CommodityMarketSnapshot(
    string CommodityId,
    string MarketId,
    DateTimeOffset CapturedAt,
    decimal UnitPrice,
    double SupplyPressure,
    double DemandPressure,
    double EconomyPriceFactor,
    double EventPriceFactor)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CommodityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(MarketId);
        if (UnitPrice <= 0
            || !double.IsFinite(SupplyPressure) || SupplyPressure <= 0
            || !double.IsFinite(DemandPressure) || DemandPressure <= 0
            || !double.IsFinite(EconomyPriceFactor) || EconomyPriceFactor <= 0
            || !double.IsFinite(EventPriceFactor) || EventPriceFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CommodityMarketSnapshot));
        }
    }
}

public sealed record CommodityLot(
    string LotId,
    CommodityDefinition Commodity,
    decimal Quantity,
    double MassPounds,
    double VolumeCubicFeet,
    string OriginMarketId,
    string DestinationMarketId,
    decimal DeclaredValue,
    CommodityMarketSnapshot OriginMarket,
    CommodityMarketSnapshot DestinationMarket,
    DateTimeOffset AcceptedAt)
{
    public decimal OriginMarketValue => decimal.Round(OriginMarket.UnitPrice * Quantity, 2);
    public decimal DestinationMarketValue => decimal.Round(DestinationMarket.UnitPrice * Quantity, 2);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(LotId);
        ArgumentNullException.ThrowIfNull(Commodity);
        Commodity.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(OriginMarketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DestinationMarketId);
        ArgumentNullException.ThrowIfNull(OriginMarket);
        ArgumentNullException.ThrowIfNull(DestinationMarket);
        OriginMarket.Validate();
        DestinationMarket.Validate();

        if (Quantity <= 0 || !double.IsFinite(MassPounds) || MassPounds <= 0
            || !double.IsFinite(VolumeCubicFeet) || VolumeCubicFeet <= 0
            || DeclaredValue <= 0
            || OriginMarket.CommodityId != Commodity.Id
            || DestinationMarket.CommodityId != Commodity.Id
            || OriginMarket.MarketId != OriginMarketId
            || DestinationMarket.MarketId != DestinationMarketId)
        {
            throw new ArgumentException("Invalid commodity lot.");
        }
    }
}

public sealed record FreightQuote(
    decimal FreightPay,
    decimal DeclaredValue,
    decimal OriginMarketValue,
    decimal DestinationMarketValue,
    decimal MarketSpread,
    double MarketSpreadPercent,
    decimal TransportPay,
    decimal ValueRiskSurcharge);

public static class NamedCargoMarket
{
    public static CommodityMarketSnapshot CreateSnapshot(
        CommodityDefinition commodity,
        string marketId,
        CommodityMarketContext context,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(commodity);
        commodity.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();

        var scarcity = Math.Clamp(context.DemandPressure / context.SupplyPressure, 0.25, 4.0);
        var price = (double)commodity.ReferenceWholesaleValuePerUnit
            * context.EconomyPriceFactor
            * context.EventPriceFactor
            * Math.Pow(scarcity, 0.35);

        if (!double.IsFinite(price) || price <= 0 || price > (double)decimal.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(context), "Commodity market price exceeded supported limits.");

        return new CommodityMarketSnapshot(
            commodity.Id,
            marketId,
            capturedAt,
            decimal.Round((decimal)price, 4),
            context.SupplyPressure,
            context.DemandPressure,
            context.EconomyPriceFactor,
            context.EventPriceFactor);
    }

    public static CommodityLot CreateLot(
        string lotId,
        CommodityDefinition commodity,
        decimal quantity,
        string originMarketId,
        string destinationMarketId,
        CommodityMarketSnapshot origin,
        CommodityMarketSnapshot destination,
        DateTimeOffset acceptedAt,
        decimal? declaredValue = null)
    {
        ArgumentNullException.ThrowIfNull(commodity);
        commodity.Validate();
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        var mass = (double)quantity * commodity.TypicalUnitMassPounds;
        var volume = (double)quantity * commodity.TypicalUnitVolumeCubicFeet;
        var value = declaredValue ?? decimal.Round(origin.UnitPrice * quantity, 2);

        var lot = new CommodityLot(
            lotId,
            commodity,
            quantity,
            mass,
            volume,
            originMarketId,
            destinationMarketId,
            value,
            origin,
            destination,
            acceptedAt);
        lot.Validate();
        return lot;
    }

    public static FreightQuote QuoteFreight(
        CommodityLot lot,
        double distanceNauticalMiles,
        double urgency,
        double handlingComplexity,
        double customerRelationship)
    {
        ArgumentNullException.ThrowIfNull(lot);
        lot.Validate();

        if (!double.IsFinite(distanceNauticalMiles) || distanceNauticalMiles <= 0
            || !double.IsFinite(urgency) || urgency is < 0 or > 1
            || !double.IsFinite(handlingComplexity) || handlingComplexity is < 0 or > 1
            || !double.IsFinite(customerRelationship) || customerRelationship is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceNauticalMiles));
        }

        var physicalBase = distanceNauticalMiles * 1.05
            + lot.MassPounds * 0.08
            + lot.VolumeCubicFeet * 0.75;

        var riskFactor = Math.Clamp(
            1.0
            + urgency * 0.75
            + handlingComplexity * 0.35
            + lot.Commodity.Perishability * 0.25
            + lot.Commodity.Fragility * 0.20
            + lot.Commodity.TheftRisk * 0.25
            + customerRelationship / 1000.0,
            1.0,
            1.75);

        var transportPay = physicalBase * riskFactor;
        var rawDeclaredValueSurcharge = (double)lot.DeclaredValue
            * (0.001 + 0.0015 * lot.Commodity.TheftRisk);

        // Cargo value can increase security/insurance exposure, but it must never dominate
        // the transport economics. This blocks tiny, high-value electronics from becoming
        // the obvious farmable cargo choice.
        var declaredValueSurcharge = Math.Min(
            rawDeclaredValueSurcharge,
            transportPay * 0.25);

        var freightPay = decimal.Round((decimal)(transportPay + declaredValueSurcharge), 2);
        var spread = lot.DestinationMarketValue - lot.OriginMarketValue;
        var spreadPercent = lot.OriginMarketValue == 0
            ? 0
            : (double)(spread / lot.OriginMarketValue * 100m);

        return new FreightQuote(
            freightPay,
            lot.DeclaredValue,
            lot.OriginMarketValue,
            lot.DestinationMarketValue,
            spread,
            spreadPercent,
            decimal.Round((decimal)transportPay, 2),
            decimal.Round((decimal)declaredValueSurcharge, 2));
    }
}

public static class InitialCommodityCatalog
{
    public static ImmutableArray<CommodityDefinition> All { get; } =
    [
        new("coffee-green", "Green coffee beans", CommodityCategory.Agriculture, "lb", 4.20m, 1.0, 0.025, 0.15, 0.10, 0.20, CommodityHandling.None),
        new("coffee-roasted", "Roasted coffee", CommodityCategory.Food, "lb", 7.40m, 1.0, 0.028, 0.20, 0.20, 0.30, CommodityHandling.TimeSensitive),
        new("smartphones", "Smartphones", CommodityCategory.ConsumerElectronics, "unit", 520m, 0.45, 0.018, 0.00, 0.55, 0.95, CommodityHandling.Fragile | CommodityHandling.TheftSensitive | CommodityHandling.HighSecurity),
        new("televisions", "Televisions", CommodityCategory.ConsumerElectronics, "unit", 440m, 35.0, 6.5, 0.00, 0.95, 0.55, CommodityHandling.Fragile),
        new("fresh-food", "Fresh food", CommodityCategory.Food, "lb", 3.60m, 1.0, 0.035, 0.90, 0.35, 0.15, CommodityHandling.Refrigerated | CommodityHandling.TimeSensitive),
        new("live-plants", "Live plants", CommodityCategory.Horticulture, "unit", 18m, 8.0, 1.8, 0.75, 0.70, 0.10, CommodityHandling.LiveOrganism | CommodityHandling.TimeSensitive | CommodityHandling.Fragile)
    ];
}
