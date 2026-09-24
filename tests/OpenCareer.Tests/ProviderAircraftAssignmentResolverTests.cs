using OpenCareer.Application.Careers;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class ProviderAircraftAssignmentResolverTests
{
    private static readonly Guid OfferId = Guid.Parse("edd928eb-6c15-44b1-8f91-8abb183bd9e9");

    [Fact]
    public async Task ProviderCandidateIsStableAndOnlyOfferedForSupportedRequirements()
    {
        JobMarketOfferDraft offer = Offer();
        var resolver = new ProviderAircraftAssignmentResolver();
        var eligible = new AircraftMissionRequirements(
            MinimumRangeNauticalMiles: 100,
            MinimumSeats: 0);

        ProviderAircraftAssignment first = Assert.IsType<ProviderAircraftAssignment>(
            await resolver.ResolveAsync(offer, eligible));
        ProviderAircraftAssignment second = Assert.IsType<ProviderAircraftAssignment>(
            await new ProviderAircraftAssignmentResolver().ResolveAsync(offer, eligible));

        Assert.Equal(first, second);
        Assert.Equal(PlayableLoopAircraftRegistrySource.AircraftId, first.AircraftId);
        Assert.Equal("KRME", first.OriginIcao);
        Assert.Null(await resolver.ResolveAsync(offer,
            eligible with { MinimumRangeNauticalMiles = 500 }));
        Assert.Null(await resolver.ResolveAsync(offer,
            eligible with { MinimumSeats = 5 }));
    }

    [Fact]
    public async Task ExplicitLegacyAssignmentIsNeverRerolled()
    {
        ProviderAircraftAssignment legacy = new(
            Guid.Parse("adb1d36b-9432-4ba6-93b2-7fc0469ceca8"),
            "legacy-canonical-aircraft", "Legacy provider", "KRME");
        JobMarketOfferDraft offer = Offer() with
        {
            ContractTerms = new JobMarketContractTermsEnvelope(
                PersistedJobContractTermsSource.AuthorityId,
                new AircraftMissionRequirements(MinimumRangeNauticalMiles: 100,
                    MinimumSeats: 0),
                EstimatedFlightHours: 1,
                PayloadPounds: 0,
                DemandAttractiveness: 1,
                Urgency: 0,
                Difficulty: 0,
                ProviderAircraft: legacy)
        };

        Assert.Equal(legacy,
            await new ProviderAircraftAssignmentResolver().ResolveAsync(
                offer, offer.ContractTerms!.AircraftRequirements));
    }

    private static JobMarketOfferDraft Offer() => new(
        OfferId,
        ServiceTrack.CivilianEmployment,
        ContractKind.Ferry,
        JobScenarioKind.Standard,
        "KRME",
        "KSYR",
        DistanceNm: 100,
        EstimatedFlightHours: 1,
        OfferedAt: new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero),
        ExpiresAt: new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero),
        IsLockedPreview: false,
        RouteStrength: 1,
        RelationshipStrength: 1,
        MarketSelectionWeight: 1);
}
