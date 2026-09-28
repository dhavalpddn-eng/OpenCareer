using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class CommodityMarketSnapshotProjectorTests
{
    [Fact]
    public void ProjectScalesCommodityValueFromExistingMarketState()
    {
        DateTimeOffset capturedAt =
            new(
                2026,
                9,
                21,
                18,
                0,
                0,
                TimeSpan.Zero);

        CommodityMarketSnapshot snapshot =
            CommodityMarketSnapshotProjector.Project(
                CommodityMarketScope.Airport,
                "KDFW",
                capturedAt,
                InitialCommodityCatalog.Default,
                new Dictionary<string, MarketState>
                {
                    ["coffee.roasted"] =
                        Market(
                            capturedAt,
                            referencePrice:
                                10m,
                            currentPrice:
                                12m,
                            baselineDemand:
                                100,
                            currentDemand:
                                150,
                            capacity:
                                100,
                            backlog:
                                10)
                });

        CommodityMarketEntry coffee =
            snapshot.GetRequired(
                "coffee.roasted");

        Assert.Equal(
            8.8800m,
            coffee.UnitPrice);
        Assert.Equal(
            1.5,
            coffee.DemandPressure,
            precision:
                10);
        Assert.Equal(
            1.0,
            coffee.SupplyPressure,
            precision:
                10);
        Assert.Equal(
            0.5,
            coffee.Trend,
            precision:
                10);
        Assert.Equal(
            CommodityMarketBalance.Scarce,
            coffee.Balance);
        Assert.Equal(
            1.0,
            coffee.AvailabilityConfidence);
        Assert.Empty(
            coffee.ActiveEventModifierIds);
    }

    [Fact]
    public void ProjectPreservesDeterministicCommodityOrdering()
    {
        DateTimeOffset capturedAt =
            DateTimeOffset.UnixEpoch;

        CommodityMarketSnapshot snapshot =
            CommodityMarketSnapshotProjector.Project(
                CommodityMarketScope.Region,
                "north-america",
                capturedAt,
                InitialCommodityCatalog.Default,
                new Dictionary<string, MarketState>
                {
                    ["electronics.smartphones"] =
                        Market(
                            capturedAt),
                    ["coffee.roasted"] =
                        Market(
                            capturedAt)
                });

        Assert.Equal(
            [
                "coffee.roasted",
                "electronics.smartphones"
            ],
            snapshot.Commodities
                .Select(
                    entry =>
                        entry.CommodityId)
                .ToArray());
    }

    [Fact]
    public void ProjectClassifiesSurplusBalancedAndBacklogScarcity()
    {
        DateTimeOffset capturedAt =
            DateTimeOffset.UnixEpoch;

        CommodityMarketSnapshot snapshot =
            CommodityMarketSnapshotProjector.Project(
                CommodityMarketScope.Region,
                "north-america",
                capturedAt,
                InitialCommodityCatalog.Default,
                new Dictionary<string, MarketState>
                {
                    ["coffee.roasted"] =
                        Market(
                            capturedAt,
                            currentDemand:
                                70,
                            capacity:
                                100),
                    ["electronics.smartphones"] =
                        Market(
                            capturedAt,
                            currentDemand:
                                100,
                            capacity:
                                100),
                    ["medical.supplies"] =
                        Market(
                            capturedAt,
                            currentDemand:
                                80,
                            capacity:
                                100,
                            backlog:
                                6)
                });

        Assert.Equal(
            CommodityMarketBalance.Surplus,
            snapshot
                .GetRequired(
                    "coffee.roasted")
                .Balance);

        Assert.Equal(
            CommodityMarketBalance.Balanced,
            snapshot
                .GetRequired(
                    "electronics.smartphones")
                .Balance);

        Assert.Equal(
            CommodityMarketBalance.Scarce,
            snapshot
                .GetRequired(
                    "medical.supplies")
                .Balance);
    }

    [Fact]
    public void ProjectRejectsUnknownCommodityAndMixedCaptureTimes()
    {
        DateTimeOffset capturedAt =
            DateTimeOffset.UnixEpoch;

        Assert.Throws<KeyNotFoundException>(
            () =>
                CommodityMarketSnapshotProjector.Project(
                    CommodityMarketScope.Region,
                    "north-america",
                    capturedAt,
                    InitialCommodityCatalog.Default,
                    new Dictionary<string, MarketState>
                    {
                        ["unknown.commodity"] =
                            Market(
                                capturedAt)
                    }));

        Assert.Throws<ArgumentException>(
            () =>
                CommodityMarketSnapshotProjector.Project(
                    CommodityMarketScope.Region,
                    "north-america",
                    capturedAt,
                    InitialCommodityCatalog.Default,
                    new Dictionary<string, MarketState>
                    {
                        ["coffee.roasted"] =
                            Market(
                                capturedAt.AddHours(
                                    -1))
                    }));
    }

    [Fact]
    public void ZeroBaselineUsesBoundedDeterministicPressures()
    {
        DateTimeOffset capturedAt =
            DateTimeOffset.UnixEpoch;

        CommodityMarketSnapshot snapshot =
            CommodityMarketSnapshotProjector.Project(
                CommodityMarketScope.Region,
                "north-america",
                capturedAt,
                InitialCommodityCatalog.Default,
                new Dictionary<string, MarketState>
                {
                    ["coffee.roasted"] =
                        Market(
                            capturedAt,
                            baselineDemand:
                                0,
                            currentDemand:
                                10,
                            capacity:
                                20)
                });

        CommodityMarketEntry coffee =
            snapshot.GetRequired(
                "coffee.roasted");

        Assert.Equal(
            4,
            coffee.DemandPressure);
        Assert.Equal(
            4,
            coffee.SupplyPressure);
        Assert.Equal(
            1,
            coffee.Trend);
    }

    private static MarketState Market(
        DateTimeOffset updatedAt,
        decimal referencePrice = 10m,
        decimal currentPrice = 10m,
        double baselineDemand = 100,
        double currentDemand = 100,
        double capacity = 100,
        double backlog = 0) =>
        new(
            MarketId:
                "test-market",
            Segment:
                MarketSegment.GeneralCargo,
            ReferenceUnitCost:
                5m,
            ReferenceUnitPrice:
                referencePrice,
            CurrentUnitPrice:
                currentPrice,
            BaselineDemandPerDay:
                baselineDemand,
            CurrentDemandPerDay:
                currentDemand,
            StructuralCapacityPerDay:
                capacity,
            BacklogUnits:
                backlog,
            SeasonalFactor:
                1,
            RegionalEconomicFactor:
                1,
            CapacityInvestmentSignal:
                0,
            UpdatedAt:
                updatedAt);
}
