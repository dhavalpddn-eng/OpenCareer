namespace OpenCareer.Domain.Economy;

public sealed record MarketState(
    string MarketId,
    decimal BaseUnitCost,
    decimal CurrentUnitPrice,
    double BaselineDemand,
    double CurrentDemand,
    double Capacity,
    double Backlog,
    double SeasonalFactor,
    double RegionalEconomicFactor,
    double EventFactor,
    DateTimeOffset UpdatedAt);

public sealed record MarketParameters(
    double PriceElasticity = 0.35,
    double BacklogCarryover = 0.82,
    double PriceSmoothing = 0.91,
    double BacklogPriceWeight = 0.35,
    double CapacityResponse = 0.08,
    double TargetUtilization = 0.78,
    double TargetMargin = 0.18);
