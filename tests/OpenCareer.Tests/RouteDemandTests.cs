using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public class RouteDemandTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BalancedMarketHasNeutralAttractiveness()
    {
        var pressure = new DemandPressure(1, 1, 0, 0);
        pressure.Validate();
        Assert.Equal(1, pressure.Attractiveness, 10);
    }

    [Fact]
    public void ScarcityTrendAndUrgencyIncreaseAttractivenessButStayBounded()
    {
        var strong = new DemandPressure(2.5, 0.6, 0.6, 0.7);
        var weak = new DemandPressure(0.4, 1.8, -0.5, 0);
        var extreme = new DemandPressure(4, 0.1, 1, 1);

        Assert.True(strong.Attractiveness > 2);
        Assert.True(weak.Attractiveness < 1);
        Assert.Equal(4, extreme.Attractiveness, 10);
    }

    [Fact]
    public void CommoditySpecificDemandCanDifferOnSameRoute()
    {
        var route = new RouteDemandProfile(
            "KRME",
            "KJFK",
            new PassengerRouteDemand(
                PassengerDemandPurpose.Business,
                new DemandPressure(1.2, 1, 0.2, 0)),
            [
                new CargoCommodityDemand(
                    CargoCommodityCategory.GeneralFreight,
                    new DemandPressure(0.8, 1.2, -0.1, 0),
                    1.2),
                new CargoCommodityDemand(
                    CargoCommodityCategory.MedicalSupplies,
                    new DemandPressure(2.2, 0.7, 0.4, 0.9),
                    0.7)
            ],
            Epoch);

        route.Validate();
        Assert.True(route.CargoAttractiveness(CargoCommodityCategory.MedicalSupplies)
            > route.CargoAttractiveness(CargoCommodityCategory.GeneralFreight));
        Assert.True(route.PassengerAttractiveness > 1);
    }

    [Fact]
    public void MissingCommodityReturnsLowButNonZeroAttractiveness()
    {
        var route = new RouteDemandProfile(
            "KRME",
            "KJFK",
            new PassengerRouteDemand(PassengerDemandPurpose.General, new DemandPressure(1, 1, 0, 0)),
            Array.Empty<CargoCommodityDemand>(),
            Epoch);

        Assert.Equal(0.25, route.CargoAttractiveness(CargoCommodityCategory.Perishables), 10);
    }

    [Fact]
    public void InvalidDemandAndDuplicateCommodityFailFast()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemandPressure(double.NaN, 1, 0, 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemandPressure(1, 1, 2, 0).Validate());

        var duplicate = new RouteDemandProfile(
            "KRME",
            "KJFK",
            new PassengerRouteDemand(PassengerDemandPurpose.General, new DemandPressure(1, 1, 0, 0)),
            [
                new CargoCommodityDemand(CargoCommodityCategory.Mail, new DemandPressure(1, 1, 0, 0)),
                new CargoCommodityDemand(CargoCommodityCategory.Mail, new DemandPressure(1, 1, 0, 0))
            ],
            Epoch);
        Assert.Throws<ArgumentException>(duplicate.Validate);
    }
}
