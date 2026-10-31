using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class RouteDemandTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DemandPressureRaisesAttractivenessWhenDemandOutrunsCapacity()
    {
        var scarce =
            new DemandPressure(
                DemandIndex: 2.8,
                AvailableCapacityIndex: 0.55,
                Trend: 0.6,
                Urgency: 0.4);

        var loose =
            new DemandPressure(
                DemandIndex: 0.45,
                AvailableCapacityIndex: 1.8,
                Trend: -0.4,
                Urgency: 0.1);

        Assert.True(
            scarce.Attractiveness
            > loose.Attractiveness);
    }

    [Fact]
    public void CargoAttractivenessCanTargetOneCommodityCategory()
    {
        RouteDemandProfile profile =
            Profile(
                "KALB",
                [
                    new(
                        CargoCommodityCategory.GeneralFreight,
                        new DemandPressure(1, 1, 0, 0)),
                    new(
                        CargoCommodityCategory.AircraftAogParts,
                        new DemandPressure(3, 0.5, 0.5, 0.8),
                        0.7)
                ]);

        double general =
            profile.CargoAttractiveness(
                CargoCommodityCategory.GeneralFreight);
        double aog =
            profile.CargoAttractiveness(
                CargoCommodityCategory.AircraftAogParts);
        double unavailable =
            profile.CargoAttractiveness(
                CargoCommodityCategory.Perishables);

        Assert.True(aog > general);
        Assert.Equal(0.25, unavailable);
    }

    [Fact]
    public void DuplicateCargoCategoriesAreRejected()
    {
        RouteDemandProfile profile =
            Profile(
                "KALB",
                [
                    new(
                        CargoCommodityCategory.GeneralFreight,
                        new DemandPressure(1, 1, 0, 0)),
                    new(
                        CargoCommodityCategory.GeneralFreight,
                        new DemandPressure(2, 1, 0, 0))
                ]);

        Assert.Throws<ArgumentException>(
            profile.Validate);
    }

    [Fact]
    public void JobMarketDestinationValidatesAttachedDemandDestination()
    {
        var valid =
            new JobMarketDestination(
                "kalb",
                DistanceNm: 80,
                RouteStrength: 0.4,
                RelationshipStrength: 0.2,
                EstimatedFlightHours: 0.8,
                DemandProfile: Profile(
                    "KALB",
                    [
                        new(
                            CargoCommodityCategory.GeneralFreight,
                            new DemandPressure(1, 1, 0, 0))
                    ]));

        valid.Validate();
        Assert.Equal("KALB", valid.NormalizedIcao);

        var mismatch =
            valid with
            {
                DemandProfile =
                    Profile(
                        "KBOS",
                        [
                            new(
                                CargoCommodityCategory.GeneralFreight,
                                new DemandPressure(1, 1, 0, 0))
                        ])
            };

        Assert.Throws<ArgumentException>(
            mismatch.Validate);
    }

    [Fact]
    public void InvalidRouteAndDestinationInputsFailFast()
    {
        Assert.Throws<ArgumentException>(
            () =>
                Profile(
                    "KRME",
                    [
                        new(
                            CargoCommodityCategory.GeneralFreight,
                            new DemandPressure(1, 1, 0, 0))
                    ]).Validate());

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new JobMarketDestination(
                    "KALB",
                    DistanceNm: -1)
                    .Validate());

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new DemandPressure(
                    DemandIndex: 1,
                    AvailableCapacityIndex: 1,
                    Trend: 0,
                    Urgency: 2)
                    .Validate());
    }

    private static RouteDemandProfile Profile(
        string destination,
        IReadOnlyList<CargoCommodityDemand> cargo) =>
        new(
            "KRME",
            destination,
            new PassengerRouteDemand(
                PassengerDemandPurpose.General,
                new DemandPressure(1, 1, 0, 0)),
            cargo,
            Epoch);
}
