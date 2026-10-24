using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class StandardCivilianPointToPointDispatchAuthoritySourceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EntryQualifiedCivilianFerryProducesNeutralDispatchAuthority()
    {
        JobMarketOfferDraft offer =
            Offer();

        CareerJobContractTermsEvidence terms =
            Terms(
                offer,
                PilotQualificationState.Entry);

        AircraftRegistryRecord aircraft =
            Aircraft();

        var source =
            new StandardCivilianPointToPointDispatchAuthoritySource();

        CareerJobDispatchAuthorityEvidence evidence =
            Assert.IsType<CareerJobDispatchAuthorityEvidence>(
                await source.ReadAsync(
                    offer,
                    Profile(
                        PilotQualificationState.Entry),
                    aircraft,
                    terms));

        Assert.True(
            evidence.QualificationsVerified);
        Assert.Equal(
            AircraftAccess.Civilian,
            evidence.AuthorizedAccess);
        Assert.Equal(
            offer.DistanceNm,
            evidence.Requirements.RequiredRangeNauticalMiles);
        Assert.Equal(
            0,
            evidence.Requirements.PayloadPounds);
        Assert.Equal(
            aircraft.AircraftId,
            evidence.AircraftId);
    }

    [Fact]
    public async Task MissingRequiredQualificationDoesNotVerifyPilot()
    {
        JobMarketOfferDraft offer =
            Offer();

        CareerJobContractTermsEvidence terms =
            Terms(
                offer,
                new PilotQualificationState(
                    PilotLicenseLevel.Private,
                    ImmutableHashSet.Create(
                        PilotRating.AirplaneSingleEngineLand)));

        var source =
            new StandardCivilianPointToPointDispatchAuthoritySource();

        CareerJobDispatchAuthorityEvidence evidence =
            Assert.IsType<CareerJobDispatchAuthorityEvidence>(
                await source.ReadAsync(
                    offer,
                    Profile(
                        PilotQualificationState.Entry),
                    Aircraft(),
                    terms));

        Assert.False(
            evidence.QualificationsVerified);
    }

    [Fact]
    public async Task WorldEventBoundOfferIsLeftForEventAwareAuthority()
    {
        JobMarketOfferDraft offer =
            Offer();

        CareerJobContractTermsEvidence terms =
            Terms(
                offer,
                PilotQualificationState.Entry) with
            {
                WorldEventId =
                    "event:active"
            };

        var source =
            new StandardCivilianPointToPointDispatchAuthoritySource();

        Assert.Null(
            await source.ReadAsync(
                offer,
                Profile(
                    PilotQualificationState.Entry),
                Aircraft(),
                terms));
    }

    private static JobMarketOfferDraft Offer() =>
        new(
            Guid.Parse(
                "a9000000-0000-0000-0000-000000000001"),
            ServiceTrack.CivilianEmployment,
            ContractKind.Ferry,
            JobScenarioKind.Standard,
            "KRME",
            "KSYR",
            DistanceNm:
                120,
            EstimatedFlightHours:
                1.5,
            OfferedAt:
                Epoch,
            ExpiresAt:
                Epoch.AddHours(6),
            IsLockedPreview:
                false,
            RouteStrength:
                0,
            RelationshipStrength:
                0,
            MarketSelectionWeight:
                1,
            ContractTerms:
                new JobMarketContractTermsEnvelope(
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
                    RequiredPilotQualifications:
                        PilotQualificationState.Entry,
                    AuthorizedAircraftAccess:
                        AircraftAccess.Civilian));

    private static CareerJobContractTermsEvidence Terms(
        JobMarketOfferDraft offer,
        PilotQualificationState qualifications) =>
        new(
            offer.OfferId,
            offer.ContractTerms!.AircraftRequirements,
            offer.ContractTerms.EstimatedFlightHours,
            offer.ContractTerms.PayloadPounds,
            offer.ContractTerms.DemandAttractiveness,
            offer.ContractTerms.Urgency,
            offer.ContractTerms.Difficulty,
            RequiredPilotQualifications:
                qualifications,
            AuthorizedAircraftAccess:
                AircraftAccess.Civilian);

    private static PlayerCareerProfile Profile(
        PilotQualificationState qualifications) =>
        PlayerCareerProfile.Start(
            Guid.Parse(
                "a9000000-0000-0000-0000-000000000099"),
            "KRME",
            Epoch.AddDays(-30)) with
        {
            Qualifications =
                qualifications
        };

    private static AircraftRegistryRecord Aircraft()
    {
        var profile =
            new AircraftCapabilityProfile(
                "fixture-aircraft",
                "Fixture Aircraft",
                AircraftCapability.None,
                AircraftAccess.Civilian,
                MaximumPayloadPounds:
                    1_000,
                MaximumRangeNauticalMiles:
                    500,
                TypicalCruiseKnots:
                    120,
                Seats:
                    4,
                EngineCount:
                    1,
                IfrCapable:
                    true,
                Pressurized:
                    false,
                RetractableGear:
                    false);

        return new(
            profile,
            IsInstalled:
                true,
            new AircraftRunwayPerformanceProfile(
                MinimumTakeoffRunwayFeet:
                    1_000,
                MinimumLandingRunwayFeet:
                    1_000,
                MinimumRunwayWidthFeet:
                    30,
                SupportedSurfaces:
                    RunwaySurfaceSupport.Asphalt,
                AircraftDataConfidence.Verified,
                Source:
                    "fixture"));
    }
}
