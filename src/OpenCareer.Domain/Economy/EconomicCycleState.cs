namespace OpenCareer.Domain.Economy;

public sealed record EconomicCycleState(
    string RegionId,
    double LogDemandFactor,
    double LogOperatingCostFactor,
    DateTimeOffset UpdatedAt)
{
    public double DemandFactor => Math.Exp(LogDemandFactor);

    public double OperatingCostFactor => Math.Exp(LogOperatingCostFactor);
}

public sealed record EconomicCycleParameters(
    double DemandHalfLifeDays = 730.0,
    double OperatingCostHalfLifeDays = 365.0,
    double DemandAnnualVolatility = 0.08,
    double OperatingCostAnnualVolatility = 0.10,
    double MaximumAbsoluteLogDemandFactor = 0.45,
    double MaximumAbsoluteLogOperatingCostFactor = 0.55,
    double MaximumStepDays = 1.0);
