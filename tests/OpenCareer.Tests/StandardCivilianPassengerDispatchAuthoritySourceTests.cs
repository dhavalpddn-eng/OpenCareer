using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class StandardCivilianPassengerDispatchAuthoritySourceTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task QualifiedPassengerAircraftProducesNeutralDispatchAuthority()
    {
        JobMarketOfferDraft offer = Offer();
        CareerJobContractTermsEvidence terms = Terms(offer);

        CareerJobDispatchAuthorityEvidence evidence =
            Assert.IsType<CareerJobDispatchAuthorityEvidence>(
                await new StandardCivilianPassengerDispatchAuthoritySource()
                    .ReadAsync(
                        offer,
                        Profile(PilotLicenseLevel.Private),
                        Aircraft(passengerCapacity: 4),
                        terms));

        Assert.Equal(offer.OfferId, evidence.OfferId);
        Assert.Equal(720, evidence.Requirements.PayloadPounds);
        Assert.Equal(150, evidence.Requirements.RequiredRangeNauticalMiles);
        Assert.True(evidence.QualificationsVerified);
        Assert.Equal(AircraftAccess.Civilian, evidence.AuthorizedAccess);
    }

    [Fact]
    public async Task MissingPassengerCapacityFailsClosed()
    {
        JobMarketOfferDraft offer = Offer();

        Assert.Null(
            await new StandardCivilianPassengerDispatchAuthoritySource()
                .ReadAsync(
                    offer,
                    Profile(PilotLicenseLevel.Private),
                    Aircraft(passengerCapacity: null),
                    Terms(offer)));
    }

    [Fact]
    public async Task WrongAuthorityOrJobKindIsNotClaimed()
    {
        JobMarketOfferDraft offer = Offer();
        var source = new StandardCivilianPassengerDispatchAuthoritySource();

        Assert.Null(await source.ReadAsync(
            offer with
            {
                ContractTerms = offer.ContractTerms! with
                {
                    AuthorityId = PersistedJobContractTermsSource.AuthorityId
                }
            },
            Profile(PilotLicenseLevel.Private),
            Aircraft(4),
            Terms(offer)));

        Assert.Null(await source.ReadAsync(
            offer with { Kind = ContractKind.Ferry },
            Profile(PilotLicenseLevel.Private),
            Aircraft(4),
            Terms(offer)));
    }

    [Fact]
    public async Task InsufficientQualificationIsReportedWithoutGrantingIt()
    {
        JobMarketOfferDraft offer = Offer();

        CareerJobDispatchAuthorityEvidence evidence =
            Assert.IsType<CareerJobDispatchAuthorityEvidence>(
                await new StandardCivilianPassengerDispatchAuthoritySource()
                    .ReadAsync(
                        offer,
                        Profile(PilotLicenseLevel.Student),
                        Aircraft(4),
                        Terms(offer)));

        Assert.False(evidence.QualificationsVerified);
    }

    private static JobMarketOfferDraft Offer()
    {
        var envelope = new JobMarketContractTermsEnvelope(
            PersistedPassengerJobContractTermsSource.AuthorityId,
            PassengerRequirements(),
            EstimatedFlightHours: 1.5,
            PayloadPounds: 720,
            DemandAttractiveness: 1,
            Urgency: 0,
            Difficulty: 0,
            RequiredPilotQualifications: new(
                PilotLicenseLevel.Private,
                ImmutableHashSet<PilotRating>.Empty),
            AuthorizedAircraftAccess: AircraftAccess.Civilian);

        return new(
            Guid.Parse("97000000-0000-0000-0000-000000000001"),
            ServiceTrack.CivilianEmployment,
            ContractKind.Passenger,
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
            PassengerRequirements(),
            EstimatedFlightHours: 1.5,
            PayloadPounds: 720,
            DemandAttractiveness: 1,
            Urgency: 0,
            Difficulty: 0,
            RequiredPilotQualifications: new(
                PilotLicenseLevel.Private,
                ImmutableHashSet<PilotRating>.Empty),
            AuthorizedAircraftAccess: AircraftAccess.Civilian);

    private static AircraftMissionRequirements PassengerRequirements() =>
        new(
            RequiredCapabilities: AircraftCapability.Passenger,
            AllowedAccess: AircraftAccess.Civilian,
            MinimumPayloadPounds: 720,
            MinimumRangeNauticalMiles: 150,
            MinimumSeats: 1,
            MinimumPassengerCapacity: 4);

    private static AircraftRegistryRecord Aircraft(
        int? passengerCapacity) =>
        new(
            new(
                AircraftId: "passenger-fixture",
                DisplayName: "Passenger Fixture",
                Capabilities: AircraftCapability.Passenger,
                Access: AircraftAccess.Civilian,
                MaximumPayloadPounds: 1000,
                MaximumRangeNauticalMiles: 500,
                TypicalCruiseKnots: 140,
                Seats: 6,
                EngineCount: 1,
                IfrCapable: true,
                Pressurized: false,
                RetractableGear: false,
                PassengerCapacity: passengerCapacity),
            IsInstalled: true,
            RunwayPerformance: null);

    private static PlayerCareerProfile Profile(
        PilotLicenseLevel license) =>
        PlayerCareerProfile.Start(
            Guid.Parse("97000000-0000-0000-0000-000000000099"),
            "KRME",
            OfferedAt.AddDays(-30)) with
        {
            Qualifications = new(
                license,
                ImmutableHashSet<PilotRating>.Empty)
        };
}
