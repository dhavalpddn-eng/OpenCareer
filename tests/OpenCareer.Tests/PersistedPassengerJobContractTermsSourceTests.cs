using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class PersistedPassengerJobContractTermsSourceTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuthoritativePassengerEnvelopeProjectsExactTerms()
    {
        JobMarketOfferDraft offer = Offer(Envelope());

        CareerJobContractTermsEvidence evidence =
            Assert.IsType<CareerJobContractTermsEvidence>(
                await new PersistedPassengerJobContractTermsSource()
                    .ReadAsync(offer, Profile()));

        Assert.Equal(offer.OfferId, evidence.OfferId);
        Assert.Equal(4, evidence.AircraftRequirements.MinimumPassengerCapacity);
        Assert.Equal(720, evidence.PayloadPounds);
        Assert.Equal(
            AircraftCapability.Passenger,
            evidence.AircraftRequirements.RequiredCapabilities);
    }

    [Fact]
    public async Task UnknownAuthorityOrUnsupportedKindIsNotClaimed()
    {
        var source = new PersistedPassengerJobContractTermsSource();

        Assert.Null(await source.ReadAsync(
            Offer(Envelope() with { AuthorityId = "future-passenger-v2" }),
            Profile()));

        Assert.Null(await source.ReadAsync(
            Offer(Envelope()) with { Kind = ContractKind.Ferry },
            Profile()));
    }

    [Theory]
    [InlineData(false, 4, 720, 720)]
    [InlineData(true, 0, 720, 720)]
    [InlineData(true, 4, 0, 0)]
    [InlineData(true, 4, 700, 720)]
    public async Task IncompleteOrInconsistentPassengerTermsFailClosed(
        bool passengerCapability,
        int passengerCapacity,
        double minimumPayload,
        double payload)
    {
        JobMarketContractTermsEnvelope envelope = Envelope() with
        {
            AircraftRequirements =
                Envelope().AircraftRequirements with
                {
                    RequiredCapabilities = passengerCapability
                        ? AircraftCapability.Passenger
                        : AircraftCapability.None,
                    MinimumPassengerCapacity = passengerCapacity,
                    MinimumPayloadPounds = minimumPayload
                },
            PayloadPounds = payload
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => new PersistedPassengerJobContractTermsSource()
                .ReadAsync(Offer(envelope), Profile()));
    }

    private static JobMarketOfferDraft Offer(
        JobMarketContractTermsEnvelope envelope) =>
        new(
            Guid.Parse("96000000-0000-0000-0000-000000000001"),
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
            MarketSelectionWeight: 1.0,
            ContractTerms: envelope);

    private static JobMarketContractTermsEnvelope Envelope() =>
        new(
            PersistedPassengerJobContractTermsSource.AuthorityId,
            new AircraftMissionRequirements(
                RequiredCapabilities: AircraftCapability.Passenger,
                AllowedAccess: AircraftAccess.Civilian,
                MinimumPayloadPounds: 720,
                MinimumRangeNauticalMiles: 150,
                MinimumSeats: 1,
                MinimumPassengerCapacity: 4),
            EstimatedFlightHours: 1.5,
            PayloadPounds: 720,
            DemandAttractiveness: 1,
            Urgency: 0,
            Difficulty: 0,
            RequiredPilotQualifications: PilotQualificationState.Entry,
            AuthorizedAircraftAccess: AircraftAccess.Civilian);

    private static PlayerCareerProfile Profile() =>
        PlayerCareerProfile.Start(
            Guid.Parse("96000000-0000-0000-0000-000000000099"),
            "KRME",
            OfferedAt.AddDays(-30));
}
