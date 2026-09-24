using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Domain.Careers;

/// <summary>Explicit, persisted development identity; never emitted by the market generator.</summary>
public static class DevelopmentFlight
{
    public const string MarketId = "development:kjfk-local-calibration:v1";
    public const string Name = "TEST / DEVELOPMENT — KJFK Local Familiarization";

    public static bool IsDevelopment(JobContract contract) => contract.MarketId == MarketId;
    public static bool IsDevelopment(JobMarketOfferDraft offer) => offer.ContractTerms?.MarketId == MarketId;

    public static JobMarketOfferDraft CreateOffer(Guid id, DateTimeOffset now) => new(
        id, ServiceTrack.CivilianEmployment, ContractKind.Reposition,
        JobScenarioKind.Standard, "KJFK", "KJFK", 0, 0.1,
        now, now.AddHours(24), false, 0, 0, 0,
        new JobMarketContractTermsEnvelope(
            "job-market:standard-civilian-point-to-point-v2",
            new AircraftMissionRequirements(AllowedAccess: AircraftAccess.Civilian),
            0.1, 0, 1, 0, 0,
            ReputationReward: 0, ReputationPenalty: 0,
            MarketId: MarketId,
            RequiredPilotQualifications: PilotQualificationState.Entry,
            AuthorizedAircraftAccess: AircraftAccess.Civilian));
}
