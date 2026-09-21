namespace OpenCareer.Domain.Economy;

public enum MarketSegment
{
    GeneralAviationServices,
    GeneralPassenger,
    BusinessCharter,
    LeisureCharter,
    GeneralCargo,
    ExpressCargo,
    MedicalLogistics,
    Training,
    Agriculture,
    Firefighting,
    Survey,
    Maintenance,
    GovernmentPriority,
    MilitarySupport
}

public sealed record MarketState(
    string MarketId,
    MarketSegment Segment,
    decimal ReferenceUnitCost,
    decimal ReferenceUnitPrice,
    decimal CurrentUnitPrice,
    double BaselineDemandPerDay,
    double CurrentDemandPerDay,
    double StructuralCapacityPerDay,
    double BacklogUnits,
    double SeasonalFactor,
    double RegionalEconomicFactor,
    double CapacityInvestmentSignal,
    DateTimeOffset UpdatedAt);

public sealed record MarketParameters(
    double PriceElasticity = 0.35,
    double BacklogRetentionPerDay = 0.82,
    double PriceHalfLifeDays = 7.0,
    double CapacityInvestmentSignalHalfLifeDays = 21.0,
    double BacklogPriceWeight = 0.35,
    double CapacityResponsePer30Days = 0.08,
    double TargetUtilization = 0.78,
    double TargetMarkup = 0.18,
    double MinimumPriceToCostRatio = 0.65,
    double MaximumPriceToCostRatio = 5.0,
    double MaximumDailyCapacityChangeFraction = 0.01,
    double MaximumStepDays = 1.0,
    double BacklogClearanceHorizonDays = 1.0);
