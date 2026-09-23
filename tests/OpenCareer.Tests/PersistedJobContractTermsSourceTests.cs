using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class PersistedJobContractTermsSourceTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SupportedPersistedEnvelopeProjectsEveryTermExactly()
    {
        Guid employerId =
            Guid.Parse(
                "a8000000-0000-0000-0000-000000000001");

        var requirements =
            new AircraftMissionRequirements(
                RequiredCapabilities:
                    AircraftCapability.Cargo,
                AllowedAccess:
                    AircraftAccess.Civilian,
                MinimumPayloadPounds:
                    0,
                MinimumRangeNauticalMiles:
                    150,
                MinimumSeats:
                    1,
                RequiresIfr:
                    true);

        var providerAircraft =
            new ProviderAircraftAssignment(
                Guid.Parse(
                    "a8000000-0000-0000-0000-000000000003"),
                "provider-aircraft",
                "Provider Aircraft",
                "KRME");

        var envelope =
            new JobMarketContractTermsEnvelope(
                PersistedJobContractTermsSource.AuthorityId,
                requirements,
                EstimatedFlightHours:
                    1.5,
                PayloadPounds:
                    0,
                DemandAttractiveness:
                    1.25,
                Urgency:
                    0.4,
                Difficulty:
                    0.6,
                EstimatedPlayerOperatingCosts:
                    125.50m,
                EmployerId:
                    employerId,
                MustStartBy:
                    OfferedAt.AddHours(1),
                MustCompleteBy:
                    OfferedAt.AddHours(4),
                ReputationReward:
                    3.5,
                ReputationPenalty:
                    5.0,
                MarketId:
                    "market:krme",
                WorldEventId:
                    "event:fixture",
                GovernmentAuthorizationRequired:
                    true,
                RequiredPilotQualifications:
                    new PilotQualificationState(
                        PilotLicenseLevel.Private,
                        System.Collections.Immutable.ImmutableHashSet.Create(
                            PilotRating.AirplaneSingleEngineLand)),
                AuthorizedAircraftAccess:
                    AircraftAccess.Civilian,
                ProviderAircraft:
                    providerAircraft);

        JobMarketOfferDraft offer =
            Offer(
                envelope);

        var source =
            new PersistedJobContractTermsSource();

        CareerJobContractTermsEvidence evidence =
            Assert.IsType<CareerJobContractTermsEvidence>(
                await source.ReadAsync(
                    offer,
                    Profile()));

        Assert.Equal(
            offer.OfferId,
            evidence.OfferId);
        Assert.Same(
            requirements,
            evidence.AircraftRequirements);
        Assert.Equal(
            envelope.EstimatedFlightHours,
            evidence.EstimatedFlightHours);
        Assert.Equal(
            envelope.PayloadPounds,
            evidence.PayloadPounds);
        Assert.Equal(
            envelope.DemandAttractiveness,
            evidence.DemandAttractiveness);
        Assert.Equal(
            envelope.Urgency,
            evidence.Urgency);
        Assert.Equal(
            envelope.Difficulty,
            evidence.Difficulty);
        Assert.Equal(
            envelope.EstimatedPlayerOperatingCosts,
            evidence.EstimatedPlayerOperatingCosts);
        Assert.Equal(
            employerId,
            evidence.EmployerId);
        Assert.Equal(
            envelope.MustStartBy,
            evidence.MustStartBy);
        Assert.Equal(
            envelope.MustCompleteBy,
            evidence.MustCompleteBy);
        Assert.Equal(
            envelope.ReputationReward,
            evidence.ReputationReward);
        Assert.Equal(
            envelope.ReputationPenalty,
            evidence.ReputationPenalty);
        Assert.Equal(
            envelope.MarketId,
            evidence.MarketId);
        Assert.Equal(
            envelope.WorldEventId,
            evidence.WorldEventId);
        Assert.True(
            evidence.GovernmentAuthorizationRequired);
        Assert.Equal(
            envelope.RequiredPilotQualifications,
            evidence.RequiredPilotQualifications);
        Assert.Equal(
            envelope.AuthorizedAircraftAccess,
            evidence.AuthorizedAircraftAccess);
        Assert.Equal(
            providerAircraft,
            evidence.ProviderAircraft);

        JobContract acceptedTerms =
            JobContractFactory.Create(
                new JobContractCreationRequest(
                    offer,
                    OfferedAt.AddMinutes(10),
                    evidence.AircraftRequirements,
                    evidence.EstimatedFlightHours,
                    evidence.PayloadPounds,
                    evidence.DemandAttractiveness,
                    evidence.Urgency,
                    evidence.Difficulty,
                    evidence.EstimatedPlayerOperatingCosts,
                    evidence.EmployerId,
                    evidence.MustStartBy,
                    evidence.MustCompleteBy,
                    evidence.ReputationReward,
                    evidence.ReputationPenalty,
                    evidence.MarketId,
                    evidence.WorldEventId,
                    evidence.GovernmentAuthorizationRequired,
                    evidence.ProviderAircraft));

        Assert.Equal(
            providerAircraft,
            acceptedTerms.ProviderAircraft);
    }

    [Fact]
    public async Task LegacyOfferWithoutEnvelopeIsNotClaimed()
    {
        var source =
            new PersistedJobContractTermsSource();

        Assert.Null(
            await source.ReadAsync(
                Offer(
                    contractTerms:
                        null),
                Profile()));
    }

    [Fact]
    public async Task UnknownEnvelopeAuthorityIsNotReinterpreted()
    {
        JobMarketContractTermsEnvelope envelope =
            StandardEnvelope() with
            {
                AuthorityId =
                    "job-market:future-point-to-point-v3"
            };

        var source =
            new PersistedJobContractTermsSource();

        Assert.Null(
            await source.ReadAsync(
                Offer(
                    envelope),
                Profile()));
    }

    [Fact]
    public async Task KnownEnvelopeIsNotClaimedForUnsupportedJobFamily()
    {
        JobMarketOfferDraft cargo =
            Offer(
                StandardEnvelope()) with
            {
                Kind =
                    ContractKind.Cargo
            };

        var source =
            new PersistedJobContractTermsSource();

        Assert.Null(
            await source.ReadAsync(
                cargo,
                Profile()));
    }

    [Fact]
    public async Task InvalidKnownEnvelopeFailsClosedInsteadOfRepairingTerms()
    {
        JobMarketContractTermsEnvelope envelope =
            StandardEnvelope() with
            {
                AircraftRequirements =
                    new AircraftMissionRequirements(
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumRangeNauticalMiles:
                            50,
                        MinimumSeats:
                            0)
            };

        var source =
            new PersistedJobContractTermsSource();

        await Assert.ThrowsAsync<ArgumentException>(
            () => source.ReadAsync(
                Offer(
                    envelope),
                Profile()));
    }

    private static JobMarketOfferDraft Offer(
        JobMarketContractTermsEnvelope? contractTerms) =>
        new(
            Guid.Parse(
                "a8000000-0000-0000-0000-000000000002"),
            ServiceTrack.CivilianEmployment,
            ContractKind.Ferry,
            JobScenarioKind.Standard,
            "KRME",
            "KSYR",
            DistanceNm:
                120,
            EstimatedFlightHours:
                1.5,
            OfferedAt,
            ExpiresAt:
                OfferedAt.AddHours(6),
            IsLockedPreview:
                false,
            RouteStrength:
                0.5,
            RelationshipStrength:
                0.25,
            MarketSelectionWeight:
                1.0,
            ContractTerms:
                contractTerms);

    private static JobMarketContractTermsEnvelope StandardEnvelope() =>
        new(
            PersistedJobContractTermsSource.AuthorityId,
            new AircraftMissionRequirements(
                AllowedAccess:
                    AircraftAccess.Civilian,
                MinimumRangeNauticalMiles:
                    120,
                MinimumSeats:
                    0),
            EstimatedFlightHours:
                1.5,
            PayloadPounds:
                0,
            DemandAttractiveness:
                1,
            Urgency:
                0,
            Difficulty:
                0,
            EstimatedPlayerOperatingCosts:
                0m,
            RequiredPilotQualifications:
                PilotQualificationState.Entry,
            AuthorizedAircraftAccess:
                AircraftAccess.Civilian);

    private static PlayerCareerProfile Profile() =>
        PlayerCareerProfile.Start(
            Guid.Parse(
                "a8000000-0000-0000-0000-000000000099"),
            "KRME",
            OfferedAt.AddDays(-30));
}
