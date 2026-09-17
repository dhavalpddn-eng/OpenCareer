namespace OpenCareer.Domain.Economy;

public sealed record MarketTickContext(
    double ElapsedDays = 1.0,
    double DemandShock = 1.0,
    double CapacityShock = 1.0);

public sealed record MarketTickResult(
    MarketState State,
    double ServedDemand,
    double Utilization,
    double Margin,
    double UnservedDemand);

public static class MarketTickEngine
{
    public static MarketTickResult Tick(
        MarketState state,
        MarketParameters parameters,
        MarketTickContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);

        Validate(state, parameters, context);

        var basePrice = Math.Max(0.01, (double)state.BaseUnitCost);
        var currentPrice = Math.Max(0.01, (double)state.CurrentUnitPrice);
        var relativePrice = currentPrice / basePrice;

        var priceDemandFactor = Math.Exp(
            -parameters.PriceElasticity * (relativePrice - 1.0));

        var freshDemand = state.BaselineDemand
            * state.SeasonalFactor
            * state.RegionalEconomicFactor
            * state.EventFactor
            * context.DemandShock
            * priceDemandFactor;

        freshDemand = Math.Max(0.0, freshDemand);

        var availableCapacity = Math.Max(
            0.001,
            state.Capacity * context.CapacityShock);

        var totalDemand = freshDemand + Math.Max(0.0, state.Backlog);
        var served = Math.Min(totalDemand, availableCapacity);
        var unserved = Math.Max(0.0, totalDemand - served);
        var utilization = Math.Clamp(served / availableCapacity, 0.0, 1.0);

        var nextBacklog = parameters.BacklogCarryover * unserved;

        var scarcityRatio = (
            freshDemand + parameters.BacklogPriceWeight * state.Backlog)
            / availableCapacity;

        var scarcityPressure = scarcityRatio - parameters.TargetUtilization;
        var targetPriceFactor = Math.Clamp(
            1.0 + parameters.TargetMargin + parameters.BacklogPriceWeight * scarcityPressure,
            0.50,
            5.00);

        var targetPrice = basePrice * targetPriceFactor;
        var nextPrice = parameters.PriceSmoothing * currentPrice
            + (1.0 - parameters.PriceSmoothing) * targetPrice;

        var margin = (nextPrice - basePrice) / basePrice;
        var monthFraction = context.ElapsedDays / 30.0;
        var capacitySignal =
            (utilization - parameters.TargetUtilization)
            + 0.5 * (margin - parameters.TargetMargin);

        var capacityChangeFraction = Math.Clamp(
            parameters.CapacityResponse * monthFraction * capacitySignal,
            -0.15,
            0.15);

        var nextCapacity = Math.Max(
            0.001,
            availableCapacity * (1.0 + capacityChangeFraction));

        var nextState = state with
        {
            CurrentUnitPrice = decimal.Round((decimal)nextPrice, 4),
            CurrentDemand = freshDemand,
            Capacity = nextCapacity,
            Backlog = nextBacklog,
            UpdatedAt = state.UpdatedAt.AddDays(context.ElapsedDays)
        };

        return new MarketTickResult(
            nextState,
            served,
            utilization,
            margin,
            unserved);
    }

    private static void Validate(
        MarketState state,
        MarketParameters parameters,
        MarketTickContext context)
    {
        if (string.IsNullOrWhiteSpace(state.MarketId))
        {
            throw new ArgumentException("MarketId is required.", nameof(state));
        }

        if (state.BaseUnitCost <= 0 || state.CurrentUnitPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "Market prices and costs must be positive.");
        }

        if (state.BaselineDemand < 0 || state.Capacity <= 0 || state.Backlog < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "Demand/backlog cannot be negative and capacity must be positive.");
        }

        if (state.SeasonalFactor <= 0
            || state.RegionalEconomicFactor <= 0
            || state.EventFactor <= 0
            || context.DemandShock <= 0
            || context.CapacityShock <= 0
            || context.ElapsedDays <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(context),
                "Economic factors and elapsed time must be positive.");
        }

        if (parameters.PriceElasticity < 0
            || parameters.BacklogCarryover is < 0 or > 1
            || parameters.PriceSmoothing is < 0 or > 1
            || parameters.BacklogPriceWeight < 0
            || parameters.CapacityResponse < 0
            || parameters.TargetUtilization is <= 0 or > 1
            || parameters.TargetMargin < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters),
                "Market parameters are outside valid ranges.");
        }
    }
}
