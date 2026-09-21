using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class JobMarketGeneratorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JobMarketDestination[] Destinations =
    [
        new("KSYR", 45, 0.80, 0.60, EstimatedFlightHours: 0.5),
        new("KALB", 80, 0.45, 0.30, EstimatedFlightHours: 0.8),
        new("KBUF", 150, 0.10, 0.05, EstimatedFlightHours: 1.3),
        new("KBOS", 220, EstimatedFlightHours: 1.9),
        new("KPHL", 260, EstimatedFlightHours: 2.2),
        new("KIAD", 300, EstimatedFlightHours: 2.6),
        new("KORD", 550, EstimatedFlightHours: 4.0)
    ];

    [Fact]
    public void SameSeedAirportAndCycleProduceSameOffers()
    {
        JobMarketGenerationRequest request =
            Request(
                Epoch,
                JobMarketAccess.All);

        JobMarketOfferDraft[] first =
            JobMarketGenerator.Generate(request).ToArray();

        JobMarketOfferDraft[] second =
            JobMarketGenerator.Generate(request).ToArray();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);

        Assert.All(
            first,
            offer =>
            {
                Assert.Equal("KRME", offer.OriginIcao);
                Assert.Equal(
                    JobScenarioKind.Standard,
                    offer.Scenario);
                Assert.NotEqual(
                    ServiceTrack.MilitaryService,
                    offer.ServiceTrack);
                Assert.True(
                    offer.ExpiresAt > offer.OfferedAt);
            });
    }

    [Fact]
    public void NewCycleChangesDeterministicOfferIdentity()
    {
        JobMarketPolicy policy =
            JobMarketPolicy.Default;

        JobMarketOfferDraft[] first =
            JobMarketGenerator.Generate(
                Request(
                    Epoch,
                    JobMarketAccess.CivilianEmployment))
                .ToArray();

        JobMarketOfferDraft[] second =
            JobMarketGenerator.Generate(
                Request(
                    Epoch + policy.RefreshInterval,
                    JobMarketAccess.CivilianEmployment))
                .ToArray();

        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.NotEqual(
            first.Select(x => x.OfferId),
            second.Select(x => x.OfferId));
    }

    [Fact]
    public void AccessNeverCreatesMilitaryAuthority()
    {
        JobMarketOfferDraft[] offers =
            JobMarketGenerator.Generate(
                Request(
                    Epoch,
                    JobMarketAccess.All,
                    careerLevel: 30,
                    capacity:
                        AirportMarketCapacity.ForScale(
                            AirportMarketScale.MajorHub)))
                .ToArray();

        Assert.NotEmpty(offers);
        Assert.DoesNotContain(
            offers,
            offer =>
                offer.ServiceTrack
                == ServiceTrack.MilitaryService);
    }

    [Fact]
    public void RouteDemandMustMatchOriginAndDestination()
    {
        RouteDemandProfile wrong =
            DemandProfile(
                "KDFW",
                "KALB",
                demand: 1,
                capacity: 1);

        var request =
            new JobMarketGenerationRequest(
                GenerationSeed: 42,
                Time: Epoch,
                Origin:
                    InitialAirportProfiles
                        .GriffissInternational,
                Destinations:
                [
                    new(
                        "KALB",
                        80,
                        DemandProfile: wrong)
                ],
                Access:
                    JobMarketAccess.CivilianEmployment);

        Assert.Throws<ArgumentException>(
            request.Validate);
    }

    [Fact]
    public void CargoPressureBiasesEqualDistanceRouteSelection()
    {
        JobMarketPolicy policy =
            JobMarketPolicy.Default with
            {
                MinimumOffers = 1,
                MaximumOffers = 1,
                ShowLockedPreviews = false
            };

        RouteDemandProfile high =
            DemandProfile(
                "KRME",
                "KAAA",
                demand: 3,
                capacity: 0.5);

        RouteDemandProfile low =
            DemandProfile(
                "KRME",
                "KBBB",
                demand: 0.4,
                capacity: 2);

        JobMarketDestination[] destinations =
        [
            new(
                "KAAA",
                180,
                EstimatedFlightHours: 2.5,
                DemandProfile: high),
            new(
                "KBBB",
                180,
                EstimatedFlightHours: 2.5,
                DemandProfile: low)
        ];

        int highSelections = 0;
        int lowSelections = 0;

        for (int i = 0; i < 500; i++)
        {
            DateTimeOffset time =
                Epoch.AddTicks(
                    policy.RefreshInterval.Ticks * i);

            var request =
                new JobMarketGenerationRequest(
                    GenerationSeed: 777,
                    Time: time,
                    Origin:
                        InitialAirportProfiles
                            .GriffissInternational,
                    Destinations: destinations,
                    Access:
                        JobMarketAccess.CivilianEmployment,
                    CareerLevel: 1,
                    Policy: policy);

            JobMarketOfferDraft offer =
                Assert.Single(
                    JobMarketGenerator.Generate(request));

            if (offer.Kind is not (
                ContractKind.Cargo
                or ContractKind.ExpressCargo
                or ContractKind.AogPartsDelivery
                or ContractKind.Medical))
            {
                continue;
            }

            if (offer.DestinationIcao == "KAAA")
                highSelections++;
            else if (offer.DestinationIcao == "KBBB")
                lowSelections++;
        }

        Assert.True(
            highSelections > lowSelections,
            $"Expected cargo pressure to bias route selection; high={highSelections}, low={lowSelections}.");
    }

    private static JobMarketGenerationRequest Request(
        DateTimeOffset time,
        JobMarketAccess access,
        int careerLevel = 1,
        AirportMarketCapacity? capacity = null) =>
        new(
            GenerationSeed: 123456789,
            Time: time,
            Origin:
                InitialAirportProfiles
                    .GriffissInternational,
            Destinations: Destinations,
            Access: access,
            CareerLevel: careerLevel,
            Capacity: capacity);

    private static RouteDemandProfile DemandProfile(
        string origin,
        string destination,
        double demand,
        double capacity)
    {
        var cargoPressure =
            new DemandPressure(
                demand,
                capacity,
                Trend: 0,
                Urgency: 0.25);

        return new RouteDemandProfile(
            origin,
            destination,
            new PassengerRouteDemand(
                PassengerDemandPurpose.General,
                new DemandPressure(
                    1,
                    1,
                    0,
                    0)),
            [
                new(
                    CargoCommodityCategory.GeneralFreight,
                    cargoPressure),
                new(
                    CargoCommodityCategory.ExpressParcel,
                    cargoPressure),
                new(
                    CargoCommodityCategory.AircraftAogParts,
                    cargoPressure,
                    0.6),
                new(
                    CargoCommodityCategory.MedicalSupplies,
                    cargoPressure,
                    0.5)
            ],
            Epoch);
    }
}
