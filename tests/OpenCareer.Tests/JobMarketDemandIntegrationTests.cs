using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public class JobMarketDemandIntegrationTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CargoScarcityMakesEqualDistanceRouteWinMoreCargoSelections()
    {
        var policy = JobMarketPolicy.Default with
        {
            MinimumOffers = 1,
            MaximumOffers = 1,
            ShowLockedPreviews = false
        };
        var highDemand = DemandProfile("KAAA", demand: 2.8, capacity: 0.55, trend: 0.6);
        var lowDemand = DemandProfile("KBBB", demand: 0.45, capacity: 1.8, trend: -0.4);
        var destinations = new[]
        {
            new JobMarketDestination("KAAA", 180, EstimatedFlightHours: 2.5, DemandProfile: highDemand),
            new JobMarketDestination("KBBB", 180, EstimatedFlightHours: 2.5, DemandProfile: lowDemand)
        };

        var high = 0;
        var low = 0;
        for (var i = 0; i < 2500; i++)
        {
            var request = new JobMarketGenerationRequest(
                987654,
                Epoch.AddTicks(policy.RefreshInterval.Ticks * i),
                InitialAirportProfiles.GriffissInternational,
                destinations,
                JobMarketAccess.CivilianEmployment,
                policy);
            var offer = Assert.Single(JobMarketGenerator.Generate(request));
            if (!IsCargoDriven(offer.Kind))
                continue;

            if (offer.DestinationIcao == "KAAA") high++;
            if (offer.DestinationIcao == "KBBB") low++;
        }

        Assert.True(high > low * 2,
            $"Expected cargo-demand pressure to materially affect route selection; high={high}, low={low}.");
    }

    [Fact]
    public void DemandProfileMustMatchItsJobMarketRoute()
    {
        var wrong = DemandProfile("KBBB", 1, 1, 0);
        var request = new JobMarketGenerationRequest(
            1,
            Epoch,
            InitialAirportProfiles.GriffissInternational,
            [new JobMarketDestination("KAAA", 100, DemandProfile: wrong)],
            JobMarketAccess.CivilianEmployment);

        Assert.Throws<ArgumentException>(request.Validate);
    }

    private static RouteDemandProfile DemandProfile(
        string destination,
        double demand,
        double capacity,
        double trend)
    {
        var cargoPressure = new DemandPressure(demand, capacity, trend, 0.25);
        return new RouteDemandProfile(
            "KRME",
            destination,
            new PassengerRouteDemand(
                PassengerDemandPurpose.General,
                new DemandPressure(1, 1, 0, 0)),
            [
                new CargoCommodityDemand(CargoCommodityCategory.GeneralFreight, cargoPressure, 1),
                new CargoCommodityDemand(CargoCommodityCategory.ExpressParcel, cargoPressure, 1),
                new CargoCommodityDemand(CargoCommodityCategory.AircraftAogParts, cargoPressure, 0.6),
                new CargoCommodityDemand(CargoCommodityCategory.MedicalSupplies, cargoPressure, 0.5)
            ],
            Epoch);
    }

    private static bool IsCargoDriven(ContractKind kind) => kind is
        ContractKind.Cargo or ContractKind.ExpressCargo or ContractKind.AogPartsDelivery or ContractKind.Medical;
}
