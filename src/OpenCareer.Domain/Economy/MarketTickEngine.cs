namespace OpenCareer.Domain.Economy;

public sealed record MarketTickContext(
    double ElapsedDays = 1.0,
    double DemandMultiplier = 1.0,
    double AvailableCapacityMultiplier = 1.0,
    double OperatingCostMultiplier = 1.0,
    double FinanceLiquidityMultiplier = 1.0);

public sealed record MarketTickResult(
    MarketState State,
    double FreshDemandVolume,
    double ServedDemandVolume,
    double AverageUtilization,
    double OperatingMarkup,
    decimal EffectiveUnitCost,
    double BacklogUnits);

public static class MarketTickEngine
{
    private const double Epsilon = 1e-9;

    public static MarketTickResult Tick(
        MarketState state,
        MarketParameters parameters,
        MarketTickContext context) => Advance(state, parameters, context);

    public static MarketTickResult Advance(
        MarketState state,
        MarketParameters parameters,
        MarketTickContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);

        Validate(state, parameters, context);

        var remainingDays = context.ElapsedDays;
        var current = state;
        var freshDemandVolume = 0.0;
        var servedDemandVolume = 0.0;
        var utilizationDaySum = 0.0;
        var finalMarkup = 0.0;
        var finalEffectiveCost = (double)state.ReferenceUnitCost * context.OperatingCostMultiplier;

        while (remainingDays > 0)
        {
            var stepDays = Math.Min(parameters.MaximumStepDays, remainingDays);
            var step = TickStep(
                current,
                parameters,
                context with { ElapsedDays = stepDays });

            current = step.State;
            freshDemandVolume += step.FreshDemandVolume;
            servedDemandVolume += step.ServedDemandVolume;
            utilizationDaySum += step.Utilization * stepDays;
            finalMarkup = step.OperatingMarkup;
            finalEffectiveCost = step.EffectiveUnitCost;
            remainingDays -= stepDays;
        }

        return new MarketTickResult(
            current,
            freshDemandVolume,
            servedDemandVolume,
            utilizationDaySum / context.ElapsedDays,
            finalMarkup,
            decimal.Round((decimal)finalEffectiveCost, 4),
            current.BacklogUnits);
    }

    private static StepResult TickStep(
        MarketState state,
        MarketParameters parameters,
        MarketTickContext context)
    {
        var dt = context.ElapsedDays;
        var referencePrice = (double)state.ReferenceUnitPrice;
        var currentPrice = (double)state.CurrentUnitPrice;
        var effectiveUnitCost = (double)state.ReferenceUnitCost * context.OperatingCostMultiplier;

        var relativePrice = currentPrice / referencePrice;
        var priceDemandFactor = Math.Exp(
            -parameters.PriceElasticity * (relativePrice - 1.0));

        var freshDemandRate = state.BaselineDemandPerDay
            * state.SeasonalFactor
            * state.RegionalEconomicFactor
            * context.DemandMultiplier
            * priceDemandFactor;

        freshDemandRate = Math.Max(0.0, freshDemandRate);
        var freshDemandVolume = freshDemandRate * dt;

        var survivingBacklog = state.BacklogUnits
            * Math.Pow(parameters.BacklogRetentionPerDay, dt);

        var effectiveCapacityRate = state.StructuralCapacityPerDay
            * context.AvailableCapacityMultiplier;
        var effectiveCapacityVolume = Math.Max(0.0, effectiveCapacityRate * dt);

        var totalDemandVolume = freshDemandVolume + survivingBacklog;
        var servedVolume = Math.Min(totalDemandVolume, effectiveCapacityVolume);
        var unservedVolume = Math.Max(0.0, totalDemandVolume - servedVolume);

        var utilization = effectiveCapacityVolume > Epsilon
            ? Math.Clamp(servedVolume / effectiveCapacityVolume, 0.0, 1.0)
            : 0.0;

        var scarcityCapacityRate = Math.Max(effectiveCapacityRate, 0.001);
        var backlogRateEquivalent = survivingBacklog / parameters.BacklogClearanceHorizonDays;
        var scarcityRatio = (
            freshDemandRate + parameters.BacklogPriceWeight * backlogRateEquivalent)
            / scarcityCapacityRate;

        var scarcityPressure = scarcityRatio - parameters.TargetUtilization;
        var targetPriceToCostRatio = Math.Clamp(
            1.0
            + parameters.TargetMarkup
            + parameters.BacklogPriceWeight * scarcityPressure,
            parameters.MinimumPriceToCostRatio,
            parameters.MaximumPriceToCostRatio);

        var targetPrice = effectiveUnitCost * targetPriceToCostRatio;
        var priceRetention = Math.Pow(0.5, dt / parameters.PriceHalfLifeDays);
        var nextPrice = priceRetention * currentPrice
            + (1.0 - priceRetention) * targetPrice;

        var operatingMarkup = (nextPrice - effectiveUnitCost) / effectiveUnitCost;

        // Expansion follows realized contribution per unit of structural capacity.
        // A high quoted price during a closure is not revenue or investable profit.
        var structuralCapacityVolume = state.StructuralCapacityPerDay * dt;
        var investmentUtilization = structuralCapacityVolume > Epsilon
            ? Math.Clamp(servedVolume / structuralCapacityVolume, 0.0, 1.0)
            : 0.0;
        var realizedContribution = (currentPrice - effectiveUnitCost) / effectiveUnitCost
            * investmentUtilization;
        var rawInvestmentSignal =
            (investmentUtilization - parameters.TargetUtilization)
            + 0.5 * (realizedContribution - parameters.TargetMarkup * parameters.TargetUtilization);

        var investmentSignalRetention = Math.Pow(
            0.5,
            dt / parameters.CapacityInvestmentSignalHalfLifeDays);

        var nextInvestmentSignal =
            investmentSignalRetention * state.CapacityInvestmentSignal
            + (1.0 - investmentSignalRetention) * rawInvestmentSignal;

        var capacityGrowthExponent =
            (parameters.CapacityResponsePer30Days / 30.0)
            * nextInvestmentSignal
            * dt;

        if (capacityGrowthExponent > 0)
        {
            capacityGrowthExponent = servedVolume <= Epsilon || currentPrice <= effectiveUnitCost
                ? 0.0
                : capacityGrowthExponent * context.FinanceLiquidityMultiplier;
        }

        capacityGrowthExponent = Math.Clamp(
            capacityGrowthExponent,
            -parameters.MaximumDailyCapacityChangeFraction * dt,
            parameters.MaximumDailyCapacityChangeFraction * dt);

        var nextStructuralCapacity = Math.Max(
            0.001,
            state.StructuralCapacityPerDay * Math.Exp(capacityGrowthExponent));

        if (!double.IsFinite(freshDemandRate) || !double.IsFinite(unservedVolume)
            || !double.IsFinite(nextStructuralCapacity) || !double.IsFinite(nextInvestmentSignal)
            || !double.IsFinite(nextPrice) || nextPrice < 0.0001
            || nextPrice > (double)decimal.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Market calculation exceeded supported numeric limits.");
        }

        var nextState = state with
        {
            CurrentUnitPrice = decimal.Round((decimal)nextPrice, 4),
            CurrentDemandPerDay = freshDemandRate,
            StructuralCapacityPerDay = nextStructuralCapacity,
            BacklogUnits = unservedVolume,
            CapacityInvestmentSignal = nextInvestmentSignal,
            UpdatedAt = state.UpdatedAt.AddDays(dt)
        };

        return new StepResult(
            nextState,
            freshDemandVolume,
            servedVolume,
            utilization,
            operatingMarkup,
            effectiveUnitCost);
    }

    public static void Validate(
        MarketState state,
        MarketParameters parameters,
        MarketTickContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(state.MarketId))
        {
            throw new ArgumentException("MarketId is required.", nameof(state));
        }

        if (state.ReferenceUnitCost <= 0
            || state.ReferenceUnitPrice <= 0
            || state.CurrentUnitPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "Reference cost, reference price, and current price must be positive.");
        }

        if (!double.IsFinite(state.BaselineDemandPerDay)
            || !double.IsFinite(state.CurrentDemandPerDay)
            || !double.IsFinite(state.StructuralCapacityPerDay)
            || !double.IsFinite(state.BacklogUnits)
            || !double.IsFinite(state.SeasonalFactor)
            || !double.IsFinite(state.RegionalEconomicFactor)
            || !double.IsFinite(state.CapacityInvestmentSignal)
            || state.BaselineDemandPerDay < 0
            || state.CurrentDemandPerDay < 0
            || state.StructuralCapacityPerDay <= 0
            || state.BacklogUnits < 0
            || state.SeasonalFactor <= 0
            || state.RegionalEconomicFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Market state is invalid.");
        }

        if (!double.IsFinite(context.ElapsedDays)
            || !double.IsFinite(context.DemandMultiplier)
            || !double.IsFinite(context.AvailableCapacityMultiplier)
            || !double.IsFinite(context.OperatingCostMultiplier)
            || !double.IsFinite(context.FinanceLiquidityMultiplier)
            || context.ElapsedDays <= 0
            || context.DemandMultiplier < 0
            || context.AvailableCapacityMultiplier < 0
            || context.OperatingCostMultiplier <= 0
            || context.FinanceLiquidityMultiplier < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "Market tick context is invalid.");
        }

        if (!double.IsFinite(parameters.PriceElasticity)
            || !double.IsFinite(parameters.BacklogRetentionPerDay)
            || !double.IsFinite(parameters.PriceHalfLifeDays)
            || !double.IsFinite(parameters.CapacityInvestmentSignalHalfLifeDays)
            || !double.IsFinite(parameters.BacklogPriceWeight)
            || !double.IsFinite(parameters.CapacityResponsePer30Days)
            || !double.IsFinite(parameters.TargetUtilization)
            || !double.IsFinite(parameters.TargetMarkup)
            || !double.IsFinite(parameters.MinimumPriceToCostRatio)
            || !double.IsFinite(parameters.MaximumPriceToCostRatio)
            || !double.IsFinite(parameters.MaximumDailyCapacityChangeFraction)
            || !double.IsFinite(parameters.MaximumStepDays)
            || !double.IsFinite(parameters.BacklogClearanceHorizonDays)
            || parameters.BacklogClearanceHorizonDays <= 0
            || parameters.PriceElasticity < 0
            || parameters.BacklogRetentionPerDay is < 0 or > 1
            || parameters.PriceHalfLifeDays <= 0
            || parameters.CapacityInvestmentSignalHalfLifeDays <= 0
            || parameters.BacklogPriceWeight < 0
            || parameters.CapacityResponsePer30Days < 0
            || parameters.TargetUtilization is <= 0 or > 1
            || parameters.TargetMarkup < 0
            || parameters.MinimumPriceToCostRatio <= 0
            || parameters.MaximumPriceToCostRatio <= parameters.MinimumPriceToCostRatio
            || parameters.MaximumDailyCapacityChangeFraction <= 0
            || parameters.MaximumStepDays is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "Market parameters are outside valid ranges.");
        }
    }

    private sealed record StepResult(
        MarketState State,
        double FreshDemandVolume,
        double ServedDemandVolume,
        double Utilization,
        double OperatingMarkup,
        double EffectiveUnitCost);
}
