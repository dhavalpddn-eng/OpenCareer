using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class CargoMarketLotSelectorTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SameEvidenceProducesSameFrozenLot()
    {
        CargoMarketLotSelectionRequest request =
            Request(
                ContractKind.Cargo,
                105,
                OriginEntry("coffee.green-beans", 4.505m),
                DestinationEntry("coffee.green-beans", 5.257m));

        AcceptedCargoLot first =
            Assert.IsType<AcceptedCargoLot>(
                CargoMarketLotSelector.Select(request));
        AcceptedCargoLot second =
            Assert.IsType<AcceptedCargoLot>(
                CargoMarketLotSelector.Select(request));

        Assert.Equal(first, second);
        Assert.Equal("coffee.green-beans", first.CommodityId);
        Assert.Equal(105m, first.Quantity);
        Assert.Equal(105, first.MassPounds);
        Assert.Equal(4.51m, first.OriginMarketUnitValue);
        Assert.Equal(5.26m, first.DestinationMarketUnitValue);
        Assert.Equal(552.30m, first.DeclaredValue);
        Assert.Equal("KDFW", first.OriginIcao);
        Assert.Equal("KORD", first.DestinationIcao);
    }

    [Fact]
    public void MissingOrContradictoryMarketEvidenceProducesNoLot()
    {
        CargoMarketLotSelectionRequest request =
            Request(
                ContractKind.Cargo,
                500,
                OriginEntry(
                    "coffee.green-beans",
                    6m,
                    supply: 1,
                    demand: 2),
                DestinationEntry(
                    "coffee.green-beans",
                    5m,
                    supply: 2,
                    demand: 1));

        Assert.Null(CargoMarketLotSelector.Select(request));
    }

    [Fact]
    public void ExpressAndAogKindsRespectCatalogMissionContext()
    {
        CargoMarketLotSelectionRequest express =
            Request(
                ContractKind.ExpressCargo,
                500,
                OriginEntry("coffee.green-beans", 4m),
                DestinationEntry("coffee.green-beans", 5m));

        Assert.Null(CargoMarketLotSelector.Select(express));

        CargoMarketLotSelectionRequest aog =
            Request(
                ContractKind.AogPartsDelivery,
                500,
                OriginEntry("parts.aircraft-aog", 18_000m),
                DestinationEntry("parts.aircraft-aog", 21_000m));

        AcceptedCargoLot lot =
            Assert.IsType<AcceptedCargoLot>(
                CargoMarketLotSelector.Select(aog));

        Assert.Equal("parts.aircraft-aog", lot.CommodityId);
        Assert.Equal(2m, lot.Quantity);
        Assert.Equal(360, lot.MassPounds);
    }

    [Fact]
    public void RouteMustMatchAirportMarketEvidence()
    {
        CargoMarketLotSelectionRequest request =
            Request(
                ContractKind.Cargo,
                500,
                OriginEntry("coffee.green-beans", 4m),
                DestinationEntry("coffee.green-beans", 5m)) with
            {
                DestinationIcao = "KBOS"
            };

        Assert.Throws<ArgumentException>(
            () => CargoMarketLotSelector.Select(request));
    }

    private static CargoMarketLotSelectionRequest Request(
        ContractKind kind,
        double maximumPayloadPounds,
        CommodityMarketEntry origin,
        CommodityMarketEntry destination) =>
        new(
            GenerationSeed: 12345,
            ContractKind: kind,
            OriginIcao: "KDFW",
            DestinationIcao: "KORD",
            MaximumPayloadPounds: maximumPayloadPounds,
            Shipper: "North Texas Shipper",
            Consignee: "Chicago Consignee",
            Catalog: InitialCommodityCatalog.Default,
            OriginMarket:
                CommodityMarketSnapshot.Create(
                    CommodityMarketScope.Airport,
                    "KDFW",
                    CapturedAt,
                    [origin]),
            DestinationMarket:
                CommodityMarketSnapshot.Create(
                    CommodityMarketScope.Airport,
                    "KORD",
                    CapturedAt,
                    [destination]));

    private static CommodityMarketEntry OriginEntry(
        string commodityId,
        decimal price,
        double supply = 2,
        double demand = 1) =>
        Entry(commodityId, price, supply, demand);

    private static CommodityMarketEntry DestinationEntry(
        string commodityId,
        decimal price,
        double supply = 1,
        double demand = 2) =>
        Entry(commodityId, price, supply, demand);

    private static CommodityMarketEntry Entry(
        string commodityId,
        decimal price,
        double supply,
        double demand) =>
        new(
            commodityId,
            supply,
            demand,
            price,
            Trend: 0,
            demand > supply
                ? CommodityMarketBalance.Scarce
                : CommodityMarketBalance.Surplus,
            ActiveEventModifierIds: [],
            AvailabilityConfidence: 1);
}
