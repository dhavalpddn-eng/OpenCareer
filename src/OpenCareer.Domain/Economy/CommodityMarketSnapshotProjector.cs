namespace OpenCareer.Domain.Economy;

public static class CommodityMarketSnapshotProjector
{
    private const double BalanceTolerance = 0.10;
    private const double BacklogScarcityDays = 0.05;

    public static CommodityMarketSnapshot Project(
        CommodityMarketScope scope,
        string locationId,
        DateTimeOffset capturedAt,
        CommodityCatalog catalog,
        IReadOnlyDictionary<string, MarketState> marketStatesByCommodityId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(marketStatesByCommodityId);

        CommodityMarketEntry[] entries =
            marketStatesByCommodityId
                .Select(
                    pair =>
                        ProjectEntry(
                            catalog.GetRequired(
                                pair.Key),
                            pair.Value,
                            capturedAt))
                .ToArray();

        return CommodityMarketSnapshot.Create(
            scope,
            locationId,
            capturedAt,
            entries);
    }

    private static CommodityMarketEntry ProjectEntry(
        CommodityDefinition definition,
        MarketState state,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);

        ValidateState(
            state,
            capturedAt);

        double demandPressure =
            NormalizePressure(
                state.CurrentDemandPerDay,
                state.BaselineDemandPerDay);

        double supplyPressure =
            NormalizePressure(
                state.StructuralCapacityPerDay,
                state.BaselineDemandPerDay);

        double trend =
            DemandTrend(
                state.CurrentDemandPerDay,
                state.BaselineDemandPerDay);

        decimal priceRatio =
            state.CurrentUnitPrice
            / state.ReferenceUnitPrice;

        decimal localUnitPrice =
            decimal.Round(
                definition.ReferenceUnitValue
                * priceRatio,
                4,
                MidpointRounding.AwayFromZero);

        return new CommodityMarketEntry(
            CommodityId:
                definition.CommodityId,
            SupplyPressure:
                supplyPressure,
            DemandPressure:
                demandPressure,
            UnitPrice:
                localUnitPrice,
            Trend:
                trend,
            Balance:
                ClassifyBalance(state),
            ActiveEventModifierIds:
                Array.Empty<string>(),
            AvailabilityConfidence:
                1.0);
    }

    private static double NormalizePressure(
        double currentRate,
        double baselineRate)
    {
        if (baselineRate <= 0)
        {
            return currentRate <= 0
                ? 0
                : 4;
        }

        return Math.Clamp(
            currentRate / baselineRate,
            0,
            4);
    }

    private static double DemandTrend(
        double currentDemandPerDay,
        double baselineDemandPerDay)
    {
        if (baselineDemandPerDay <= 0)
        {
            return currentDemandPerDay <= 0
                ? 0
                : 1;
        }

        return Math.Clamp(
            (currentDemandPerDay
                - baselineDemandPerDay)
            / baselineDemandPerDay,
            -1,
            1);
    }

    private static CommodityMarketBalance ClassifyBalance(
        MarketState state)
    {
        double backlogDays =
            state.BacklogUnits
            / state.StructuralCapacityPerDay;

        if (backlogDays > BacklogScarcityDays
            || state.CurrentDemandPerDay
                > state.StructuralCapacityPerDay
                    * (1 + BalanceTolerance))
        {
            return CommodityMarketBalance.Scarce;
        }

        if (state.CurrentDemandPerDay
            < state.StructuralCapacityPerDay
                * (1 - BalanceTolerance))
        {
            return CommodityMarketBalance.Surplus;
        }

        return CommodityMarketBalance.Balanced;
    }

    private static void ValidateState(
        MarketState state,
        DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            state.MarketId);

        if (!Enum.IsDefined(state.Segment))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state));
        }

        if (state.ReferenceUnitPrice <= 0
            || state.CurrentUnitPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "Market reference and current unit prices must be positive.");
        }

        if (!double.IsFinite(
                state.BaselineDemandPerDay)
            || !double.IsFinite(
                state.CurrentDemandPerDay)
            || !double.IsFinite(
                state.StructuralCapacityPerDay)
            || !double.IsFinite(
                state.BacklogUnits)
            || state.BaselineDemandPerDay < 0
            || state.CurrentDemandPerDay < 0
            || state.StructuralCapacityPerDay <= 0
            || state.BacklogUnits < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "Market demand, capacity, and backlog values are invalid.");
        }

        if (state.UpdatedAt != capturedAt)
        {
            throw new ArgumentException(
                "Commodity market projection requires market states from the snapshot capture time.",
                nameof(state));
        }
    }
}
