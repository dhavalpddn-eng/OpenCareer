using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Events;

namespace OpenCareer.Tests;

public sealed class JobContractEconomicIntegrationTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OfferBecomesContractWithImmutableEconomicSnapshot()
    {
        JobMarketOfferDraft offer =
            Offer();

        JobContract contract =
            JobContractFactory.Create(
                Request(offer));

        Assert.Equal(offer.OfferId, contract.ContractId);
        Assert.Equal(offer.ExpiresAt, contract.MustAcceptBy);
        Assert.NotNull(contract.EconomicSnapshot);
        Assert.Equal(
            JobContractFactory.EconomicQuoteVersion,
            contract.EconomicSnapshot!.QuoteVersion);
        Assert.Equal(
            offer.RelationshipStrength,
            contract.EconomicSnapshot.RelationshipStrength);
        Assert.Equal(
            contract.Compensation,
            contract.EconomicSnapshot.Quote.Compensation);
        Assert.True(
            contract.Compensation.PilotCompensation > 0m);
    }

    [Fact]
    public void LaterMarketPolicyChangesDoNotRewriteExistingContract()
    {
        JobMarketOfferDraft offer =
            Offer();

        JobContract original =
            JobContractFactory.Create(
                Request(offer));

        ContractPayPolicy richerPolicy =
            ContractPayPolicy.Default with
            {
                TimeRatePerFlightHour =
                    ContractPayPolicy.Default.TimeRatePerFlightHour * 2m
            };

        JobContract laterQuote =
            JobContractFactory.Create(
                Request(offer) with
                {
                    QuoteTime = OfferedAt.AddMinutes(10)
                },
                richerPolicy);

        Assert.NotEqual(
            original.Compensation.PilotCompensation,
            laterQuote.Compensation.PilotCompensation);

        Assert.Equal(
            original.Compensation,
            original.EconomicSnapshot!.Quote.Compensation);
    }

    [Fact]
    public void LockedPreviewCannotBecomePaidContract()
    {
        Assert.Throws<InvalidOperationException>(
            () => JobContractFactory.Create(
                Request(
                    Offer() with
                    {
                        IsLockedPreview = true
                    })));
    }

    [Fact]
    public void ExpiredOfferCannotBeQuotedIntoContract()
    {
        JobMarketOfferDraft offer =
            Offer();

        Assert.Throws<InvalidOperationException>(
            () => JobContractFactory.Create(
                Request(offer) with
                {
                    QuoteTime = offer.ExpiresAt
                }));
    }

    [Fact]
    public void ContractCannotBeAcceptedAfterOfferAcceptanceDeadline()
    {
        JobContract contract =
            JobContractFactory.Create(
                Request(Offer()));

        var aircraft =
            new AircraftCapabilityProfile(
                "cargo-fixture",
                "Cargo fixture",
                AircraftCapability.Cargo,
                AircraftAccess.Civilian,
                MaximumPayloadPounds: 2_000,
                MaximumRangeNauticalMiles: 1_000,
                TypicalCruiseKnots: 150,
                Seats: 4,
                EngineCount: 1,
                IfrCapable: true,
                Pressurized: false,
                RetractableGear: false);

        var context =
            new ContractDispatchContext(
                Time:
                    contract.MustAcceptBy!.Value
                        .AddMinutes(1),
                Aircraft: aircraft,
                AuthorizedAccess:
                    AircraftAccess.Civilian,
                Effects: new WorldEventEffects(),
                QualificationsVerified: true,
                DispatchFeasibilityVerified: true);

        Assert.Throws<InvalidOperationException>(
            () => contract.Accept(context));
    }

    [Fact]
    public void SnapshotCompensationMismatchIsRejected()
    {
        JobContract contract =
            JobContractFactory.Create(
                Request(Offer()));

        JobContract changed =
            contract with
            {
                Compensation =
                    contract.Compensation with
                    {
                        PilotCompensation =
                            contract.Compensation.PilotCompensation
                            + 1m
                    }
            };

        Assert.Throws<ArgumentException>(
            changed.Validate);
    }

    private static JobContractCreationRequest Request(
        JobMarketOfferDraft offer) =>
        new(
            Offer: offer,
            QuoteTime: OfferedAt.AddMinutes(5),
            AircraftRequirements:
                new AircraftMissionRequirements(
                    RequiredCapabilities:
                        AircraftCapability.Cargo,
                    AllowedAccess:
                        AircraftAccess.Civilian,
                    MinimumPayloadPounds: 500,
                    MinimumRangeNauticalMiles: 400,
                    MinimumSeats: 0,
                    RequiresIfr: true),
            EstimatedFlightHours: 3,
            PayloadPounds: 500,
            DemandAttractiveness: 1.2,
            Urgency: 0.15,
            Difficulty: 0.10,
            EstimatedPlayerOperatingCosts: 0m,
            MustStartBy: OfferedAt.AddHours(2),
            MustCompleteBy: OfferedAt.AddHours(6));

    private static JobMarketOfferDraft Offer() =>
        new(
            OfferId: Guid.NewGuid(),
            ServiceTrack:
                ServiceTrack.CivilianEmployment,
            Kind: ContractKind.Cargo,
            Scenario: JobScenarioKind.Standard,
            OriginIcao: "KRME",
            DestinationIcao: "KSYR",
            DistanceNm: 400,
            EstimatedFlightHours: 3,
            OfferedAt: OfferedAt,
            ExpiresAt: OfferedAt.AddHours(1),
            IsLockedPreview: false,
            RouteStrength: 0.20,
            RelationshipStrength: 0.30,
            MarketSelectionWeight: 1.0);
}
