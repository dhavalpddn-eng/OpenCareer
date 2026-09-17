using OpenCareer.Domain.Simulation;

namespace OpenCareer.Domain.Economy;

public static class EconomicCycleEngine
{
    private const double DaysPerYear = 365.2425;
    private const double Epsilon = 1e-9;

    public static EconomicCycleState Advance(
        EconomicCycleState state,
        EconomicCycleParameters parameters,
        DeterministicRandom random,
        double elapsedDays)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(random);

        Validate(state, parameters, elapsedDays);

        var current = state;
        var remaining = elapsedDays;

        while (remaining > Epsilon)
        {
            var dt = Math.Min(parameters.MaximumStepDays, remaining);
            current = AdvanceStep(current, parameters, random, dt);
            remaining -= dt;
        }

        return current;
    }

    public static void Validate(EconomicCycleState state, EconomicCycleParameters parameters, double elapsedDays)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(state.RegionId)
            || !double.IsFinite(state.LogDemandFactor)
            || !double.IsFinite(state.LogOperatingCostFactor))
        {
            throw new ArgumentException("Economic cycle state is invalid.", nameof(state));
        }

        if (!double.IsFinite(elapsedDays) || elapsedDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedDays));
        }

        if (!double.IsFinite(parameters.DemandHalfLifeDays)
            || !double.IsFinite(parameters.OperatingCostHalfLifeDays)
            || !double.IsFinite(parameters.DemandAnnualVolatility)
            || !double.IsFinite(parameters.OperatingCostAnnualVolatility)
            || !double.IsFinite(parameters.MaximumAbsoluteLogDemandFactor)
            || !double.IsFinite(parameters.MaximumAbsoluteLogOperatingCostFactor)
            || !double.IsFinite(parameters.MaximumStepDays)
            || parameters.DemandHalfLifeDays <= 0
            || parameters.OperatingCostHalfLifeDays <= 0
            || parameters.DemandAnnualVolatility < 0
            || parameters.OperatingCostAnnualVolatility < 0
            || parameters.MaximumAbsoluteLogDemandFactor <= 0
            || parameters.MaximumAbsoluteLogOperatingCostFactor <= 0
            || parameters.MaximumStepDays is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters));
        }

    }

    private static EconomicCycleState AdvanceStep(
        EconomicCycleState state,
        EconomicCycleParameters parameters,
        DeterministicRandom random,
        double dt)
    {
        var demandRetention = Math.Pow(0.5, dt / parameters.DemandHalfLifeDays);
        var costRetention = Math.Pow(0.5, dt / parameters.OperatingCostHalfLifeDays);
        var volatilityScale = Math.Sqrt(dt / DaysPerYear);

        var demandNoise = random.NextNormal(
            0.0,
            parameters.DemandAnnualVolatility * volatilityScale);

        var costNoise = random.NextNormal(
            0.0,
            parameters.OperatingCostAnnualVolatility * volatilityScale);

        var nextDemand = Math.Clamp(
            demandRetention * state.LogDemandFactor + demandNoise,
            -parameters.MaximumAbsoluteLogDemandFactor,
            parameters.MaximumAbsoluteLogDemandFactor);

        var nextCost = Math.Clamp(
            costRetention * state.LogOperatingCostFactor + costNoise,
            -parameters.MaximumAbsoluteLogOperatingCostFactor,
            parameters.MaximumAbsoluteLogOperatingCostFactor);

        return state with
        {
            LogDemandFactor = nextDemand,
            LogOperatingCostFactor = nextCost,
            UpdatedAt = state.UpdatedAt.AddDays(dt)
        };
    }
}
