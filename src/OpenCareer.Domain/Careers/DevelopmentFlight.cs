using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Domain.Careers;

/// <summary>
/// Identifies the explicit KJFK development circuit without changing normal
/// job-market generation or the production flight lifecycle.
/// </summary>
public static class DevelopmentFlight
{
    public const string MarketId =
        "development:kjfk-local-calibration:v1";

    public const string Name =
        "TEST / DEVELOPMENT — KJFK Local Familiarization";

    public static bool IsDevelopment(
        JobContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return string.Equals(
            contract.MarketId,
            MarketId,
            StringComparison.Ordinal);
    }

    public static bool IsDevelopment(
        JobMarketOfferDraft offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return string.Equals(
            offer.ContractTerms?.MarketId,
            MarketId,
            StringComparison.Ordinal);
    }

    public static JobMarketOfferDraft CreateOffer(
        Guid offerId,
        DateTimeOffset offeredAt) =>
        new(
            offerId,
            ServiceTrack.CivilianEmployment,
            ContractKind.Reposition,
            JobScenarioKind.Standard,
            "KJFK",
            "KJFK",
            DistanceNm:
                0,
            EstimatedFlightHours:
                0.1,
            offeredAt,
            offeredAt.AddHours(24),
            IsLockedPreview:
                false,
            RouteStrength:
                0,
            RelationshipStrength:
                0,
            MarketSelectionWeight:
                0,
            new JobMarketContractTermsEnvelope(
                PersistedAuthorityId,
                new AircraftMissionRequirements(
                    AllowedAccess:
                        AircraftAccess.Civilian),
                EstimatedFlightHours:
                    0.1,
                PayloadPounds:
                    0,
                DemandAttractiveness:
                    1,
                Urgency:
                    0,
                Difficulty:
                    0,
                EstimatedPlayerOperatingCosts:
                    0,
                ReputationReward:
                    0,
                ReputationPenalty:
                    0,
                MarketId:
                    MarketId,
                RequiredPilotQualifications:
                    PilotQualificationState.Entry,
                AuthorizedAircraftAccess:
                    AircraftAccess.Civilian));

    private const string PersistedAuthorityId =
        "job-market:standard-civilian-point-to-point-v2";
}
