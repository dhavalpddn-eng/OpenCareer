using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class StandardCivilianCargoDispatchAuthoritySourceTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task QualifiedCargoAircraftProducesManifestBoundAuthority()
    {
        JobMarketOfferDraft offer = Offer();

        CareerJobDispatchAuthorityEvidence evidence =
            Assert.IsType<CareerJobDispatchAuthorityEvidence>(
                await new StandardCivilianCargoDispatchAuthoritySource()
                    .ReadAsync(
                        offer,
                        Profile(PilotLicenseLevel.Private),
                        Aircraft(AircraftCapability.Cargo, payloadPounds: 1200),
                        Terms(offer)));

        Assert.Equal(offer.OfferId, evidence.OfferId);
        Assert.Equal(800, evidence.Requirements.PayloadPounds);
        Assert.Equal(150, evidence.Requirements.RequiredRangeNauticalMiles);
        Assert.True(evidence.QualificationsVerified);
        Assert.Equal(AircraftAccess.Civilian, evidence.AuthorizedAccess);
    }

    [Fact]
    public async Task MissingCargoCapabilityOrPayloadFailsClosed()
    {
        JobMarketOfferDraft offer = Offer();
        var source = new StandardCivilianCargoDispatchAuthoritySource();

        Assert.Null(await source.ReadAsync(
            offer,
            Profile(PilotLicenseLevel.Private),
            Aircraft(AircraftCapability.None, payloadPounds: 1200),
            Terms(offer)));

        Assert.Null(await source.ReadAsync(
            offer,
            Profile(PilotLicenseLevel.Private),
            Aircraft(AircraftCapability.Cargo, payloadPounds: 700),
            Terms(offer)));
    }

    [Fact]
    public async Task MissingManifestWrongAuthorityOrPassengerKindIsNotClaimed()
    {
        JobMarketOfferDraft offer = Offer();
        var source = new StandardCivilianCargoDispatchAuthoritySource();

        Assert.Null(await source.ReadAsync(
            offer,
            Profile(PilotLicenseLevel.Private),
            Aircraft(AircraftCapability.Cargo, payloadPounds: 1200),
            Terms(offer) with { CargoLots = ImmutableArray<AcceptedCargoLot>.Empty }));

        Assert.Null(await source.ReadAsync(
            offer with
            {
                ContractTerms = offer.ContractTerms! with
                {
                    AuthorityId = PersistedJobContractTermsSource.AuthorityId
                }
            },
            Profile(PilotLicenseLevel.Private),
            Aircraft(AircraftCapability.Cargo, payloadPounds: 1200),
            Terms(offer)));

        Assert.Null(await source.ReadAsync(
            offer with { Kind = ContractKind.Passenger },
            Profile(PilotLicenseLevel.Private),
            Aircraft(AircraftCapability.Cargo, payloadPounds: 1200),
            Terms(offer)));
    }

    [Fact]
    public async Task InsufficientQualificationIsReportedWithoutGrantingIt()
    {
        JobMarketOfferDraft offer = Offer();

        CareerJobDispatchAuthorityEvidence evidence =
            Assert.IsType<CareerJobDispatchAuthorityEvidence>(
                await new StandardCivilianCargoDispatchAuthoritySource()
                    .ReadAsync(
                        offer,
                        Profile(PilotLicenseLevel.Student),
                        Aircraft(AircraftCapability.Cargo, payloadPounds: 1200),
                        Terms(offer)));

        Assert.False(evidence.QualificationsVerified);
    }

    private static JobMarketOfferDraft Offer()
    {
        ImmutableArray<AcceptedCargoLot> lots = [Lot()];
        var envelope = new JobMarketContractTermsEnvelope(
            PersistedCargoJobContractTermsSource.AuthorityId,
            CargoRequirements(),
            EstimatedFlightHours: 1.5,
            PayloadPounds: 800,
            DemandAttractiveness: 1,
            Urgency: 0,
            Difficulty: 0,
            RequiredPilotQualifications: new(
                PilotLicenseLevel.Private,
                ImmutableHashSet<PilotRating>.Empty),
            AuthorizedAircraftAccess: AircraftAccess.Civilian,
            CargoLots: lots);

        return new(
            Guid.Parse("98000000-0000-0000-0000-000000000001"),
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
            MarketSelectionWeight: 1,
            ContractTerms: envelope);
    }

    private static CareerJobContractTermsEvidence Terms(
        JobMarketOfferDraft offer) =>
        new(
            offer.OfferId,
            CargoRequirements(),
            EstimatedFlightHours: 1.5,
            PayloadPounds: 800,
            DemandAttractiveness: 1,
            Urgency: 0,
            Difficulty: 0,
            RequiredPilotQualifications: new(
                PilotLicenseLevel.Private,
                ImmutableHashSet<PilotRating>.Empty),
            AuthorizedAircraftAccess: AircraftAccess.Civilian,
            CargoLots: [Lot()]);

    private static AircraftMissionRequirements CargoRequirements() =>
        new(
            RequiredCapabilities: AircraftCapability.Cargo,
            AllowedAccess: AircraftAccess.Civilian,
            MinimumPayloadPounds: 800,
            MinimumRangeNauticalMiles: 150);

    private static AircraftRegistryRecord Aircraft(
        AircraftCapability capability,
        double payloadPounds) =>
        new(
            new(
                AircraftId: "cargo-fixture",
                DisplayName: "Cargo Fixture",
                Capabilities: capability,
                Access: AircraftAccess.Civilian,
                MaximumPayloadPounds: payloadPounds,
                MaximumRangeNauticalMiles: 500,
                TypicalCruiseKnots: 140,
                Seats: 2,
                EngineCount: 1,
                IfrCapable: true,
                Pressurized: false,
                RetractableGear: false),
            IsInstalled: true,
            RunwayPerformance: null);

    private static AcceptedCargoLot Lot() =>
        new(
            Guid.Parse("98000000-0000-0000-0000-000000000010"),
            "roasted-coffee",
            "Roasted coffee",
            CargoCommodityCategory.GeneralFreight,
            Quantity: 800,
            CargoUnitOfTrade.Pound,
            MassPounds: 800,
            VolumeCubicFeet: 100,
            "KRME",
            "KSYR",
            "Mohawk Valley Coffee",
            "Syracuse Distribution",
            DeclaredValue: 6000m,
            OriginMarketUnitValue: 7.40m,
            DestinationMarketUnitValue: 8.05m,
            CargoHandlingRequirement.None,
            AcceptedCondition: 1.0);

    private static PlayerCareerProfile Profile(
        PilotLicenseLevel license) =>
        PlayerCareerProfile.Start(
            Guid.Parse("98000000-0000-0000-0000-000000000099"),
            "KRME",
            OfferedAt.AddDays(-30)) with
        {
            Qualifications = new(
                license,
                ImmutableHashSet<PilotRating>.Empty)
        };
}
