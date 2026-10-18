using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class PersistedCargoJobContractTermsSourceTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuthoritativeCargoEnvelopeProjectsImmutableNamedLots()
    {
        JobMarketOfferDraft offer = Offer(Envelope());

        CareerJobContractTermsEvidence evidence =
            Assert.IsType<CareerJobContractTermsEvidence>(
                await new PersistedCargoJobContractTermsSource()
                    .ReadAsync(offer, Profile()));

        Assert.Equal(offer.OfferId, evidence.OfferId);
        Assert.Equal(500, evidence.PayloadPounds);
        Assert.Equal(
            AircraftCapability.Cargo,
            evidence.AircraftRequirements.RequiredCapabilities);
        AcceptedCargoLot lot = Assert.Single(evidence.CargoLots);
        Assert.Equal("coffee.roasted", lot.CommodityId);
    }

    [Fact]
    public async Task UnknownAuthorityOrMissingNamedLotsIsNotUsable()
    {
        var source = new PersistedCargoJobContractTermsSource();

        Assert.Null(await source.ReadAsync(
            Offer(Envelope() with { AuthorityId = "future-cargo-v2" }),
            Profile()));

        await Assert.ThrowsAsync<ArgumentException>(
            () => source.ReadAsync(
                Offer(Envelope() with
                {
                    CargoLots = ImmutableArray<AcceptedCargoLot>.Empty
                }),
                Profile()));
    }

    [Fact]
    public void FactoryPersistsLotsAndManifestSurvivesStartTransition()
    {
        JobMarketOfferDraft offer = Offer(Envelope());
        JobMarketContractTermsEnvelope terms = offer.ContractTerms!;

        JobContract contract = JobContractFactory.Create(
            new JobContractCreationRequest(
                offer,
                OfferedAt.AddMinutes(10),
                terms.AircraftRequirements,
                terms.EstimatedFlightHours,
                terms.PayloadPounds,
                terms.DemandAttractiveness,
                terms.Urgency,
                terms.Difficulty,
                CargoLots: terms.CargoLots));

        JobContract accepted = contract with
        {
            Status = ContractStatus.Accepted,
            AcceptedAt = OfferedAt.AddMinutes(10)
        };
        accepted.Validate();

        AcceptedCargoManifest acceptedManifest =
            AcceptedCargoManifest.Create(accepted);

        JobContract inProgress = accepted with
        {
            Status = ContractStatus.InProgress,
            StartedAt = OfferedAt.AddMinutes(20)
        };
        inProgress.Validate();

        AcceptedCargoManifest recoveredManifest =
            AcceptedCargoManifest.Create(inProgress);

        Assert.Equal(500, acceptedManifest.TotalMassPounds);
        Assert.Equal(acceptedManifest, recoveredManifest);
    }

    private static JobMarketOfferDraft Offer(
        JobMarketContractTermsEnvelope envelope) =>
        new(
            Guid.Parse("97000000-0000-0000-0000-000000000001"),
            ServiceTrack.CivilianEmployment,
            ContractKind.Cargo,
            JobScenarioKind.Standard,
            "KRME",
            "KSYR",
            DistanceNm: 150,
            EstimatedFlightHours: 1.5,
            OfferedAt,
            ExpiresAt: OfferedAt.AddHours(6),
            IsLockedPreview: false,
            RouteStrength: 0.5,
            RelationshipStrength: 0.25,
            MarketSelectionWeight: 1.0,
            ContractTerms: envelope);

    private static JobMarketContractTermsEnvelope Envelope() =>
        new(
            PersistedCargoJobContractTermsSource.AuthorityId,
            new AircraftMissionRequirements(
                RequiredCapabilities: AircraftCapability.Cargo,
                AllowedAccess: AircraftAccess.Civilian,
                MinimumPayloadPounds: 500,
                MinimumRangeNauticalMiles: 150,
                MinimumSeats: 0),
            EstimatedFlightHours: 1.5,
            PayloadPounds: 500,
            DemandAttractiveness: 1,
            Urgency: 0,
            Difficulty: 0,
            RequiredPilotQualifications: PilotQualificationState.Entry,
            AuthorizedAircraftAccess: AircraftAccess.Civilian,
            CargoLots:
            [
                new AcceptedCargoLot(
                    Guid.Parse("97000000-0000-0000-0000-000000000010"),
                    "coffee.roasted",
                    "Roasted coffee",
                    CargoCommodityCategory.GeneralFreight,
                    Quantity: 500m,
                    CargoUnitOfTrade.Pound,
                    MassPounds: 500,
                    VolumeCubicFeet: null,
                    "KRME",
                    "KSYR",
                    "Mohawk Valley Roasters",
                    "Syracuse Coffee Supply",
                    DeclaredValue: 3_700m,
                    OriginMarketUnitValue: 7.40m,
                    DestinationMarketUnitValue: 8.05m,
                    CargoHandlingRequirement.None)
            ]);

    private static PlayerCareerProfile Profile() =>
        PlayerCareerProfile.Start(
            Guid.Parse("97000000-0000-0000-0000-000000000099"),
            "KRME",
            OfferedAt.AddDays(-30));
}
