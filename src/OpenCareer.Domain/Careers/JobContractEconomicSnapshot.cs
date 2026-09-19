using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Domain.Careers;

public sealed record JobContractEconomicSnapshot(
    string QuoteVersion,
    DateTimeOffset QuotedAt,
    double EstimatedFlightHours,
    double DistanceNauticalMiles,
    double PayloadPounds,
    double DemandAttractiveness,
    double Urgency,
    double Difficulty,
    double RelationshipStrength,
    decimal EstimatedPlayerOperatingCosts,
    ContractPayQuote Quote)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(QuoteVersion);

        if (!double.IsFinite(EstimatedFlightHours)
            || EstimatedFlightHours <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(EstimatedFlightHours));
        }

        if (!double.IsFinite(DistanceNauticalMiles)
            || DistanceNauticalMiles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DistanceNauticalMiles));
        }

        if (!double.IsFinite(PayloadPounds)
            || PayloadPounds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PayloadPounds));
        }

        if (!double.IsFinite(DemandAttractiveness)
            || DemandAttractiveness is < 0.25 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(DemandAttractiveness));
        }

        ValidateUnit(Urgency, nameof(Urgency));
        ValidateUnit(Difficulty, nameof(Difficulty));
        ValidateUnit(RelationshipStrength, nameof(RelationshipStrength));

        if (EstimatedPlayerOperatingCosts < 0m
            || decimal.Round(
                EstimatedPlayerOperatingCosts,
                2,
                MidpointRounding.AwayFromZero)
                != EstimatedPlayerOperatingCosts)
        {
            throw new ArgumentException(
                "Estimated operating costs must be non-negative whole cents.",
                nameof(EstimatedPlayerOperatingCosts));
        }

        ArgumentNullException.ThrowIfNull(Quote);
        Quote.Validate();
    }

    private static void ValidateUnit(
        double value,
        string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record JobContractCreationRequest(
    JobMarketOfferDraft Offer,
    DateTimeOffset QuoteTime,
    AircraftMissionRequirements AircraftRequirements,
    double EstimatedFlightHours,
    double PayloadPounds,
    double DemandAttractiveness,
    double Urgency,
    double Difficulty,
    decimal EstimatedPlayerOperatingCosts = 0m,
    Guid? EmployerId = null,
    DateTimeOffset? MustStartBy = null,
    DateTimeOffset? MustCompleteBy = null,
    double ReputationReward = 1.0,
    double ReputationPenalty = 2.0,
    string? MarketId = null,
    string? WorldEventId = null,
    bool GovernmentAuthorizationRequired = false)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Offer);
        ArgumentNullException.ThrowIfNull(AircraftRequirements);
        AircraftRequirements.Validate();

        if (Offer.OfferId == Guid.Empty)
            throw new ArgumentException("Job offer ID is required.", nameof(Offer));

        if (Offer.IsLockedPreview)
            throw new InvalidOperationException(
                "A locked dream-job preview cannot become a contract.");

        if (QuoteTime < Offer.OfferedAt || QuoteTime >= Offer.ExpiresAt)
            throw new InvalidOperationException(
                "Job offer is not active at the quote time.");

        if (!double.IsFinite(EstimatedFlightHours)
            || EstimatedFlightHours <= 0
            || !double.IsFinite(PayloadPounds)
            || PayloadPounds < 0
            || !double.IsFinite(DemandAttractiveness)
            || DemandAttractiveness is < 0.25 or > 4
            || !double.IsFinite(Urgency)
            || Urgency is < 0 or > 1
            || !double.IsFinite(Difficulty)
            || Difficulty is < 0 or > 1
            || !double.IsFinite(ReputationReward)
            || ReputationReward < 0
            || !double.IsFinite(ReputationPenalty)
            || ReputationPenalty < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(JobContractCreationRequest));
        }

        if (EstimatedPlayerOperatingCosts < 0m
            || decimal.Round(
                EstimatedPlayerOperatingCosts,
                2,
                MidpointRounding.AwayFromZero)
                != EstimatedPlayerOperatingCosts)
        {
            throw new ArgumentException(
                "Estimated operating costs must be non-negative whole cents.",
                nameof(EstimatedPlayerOperatingCosts));
        }

        if (MustStartBy is { } start && start < Offer.OfferedAt)
            throw new ArgumentException("Start deadline cannot precede the offer.");

        if (MustCompleteBy is { } end
            && (end < Offer.OfferedAt
                || (MustStartBy is { } begin && end < begin)))
        {
            throw new ArgumentException("Completion deadline is invalid.");
        }
    }
}

public static class JobContractFactory
{
    public const string EconomicQuoteVersion = "contract-pay-v1";

    public static JobContract Create(
        JobContractCreationRequest request,
        ContractPayPolicy? payPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        JobMarketOfferDraft offer = request.Offer;

        ContractPayQuote quote =
            ContractPayQuoteEngine.Quote(
                new ContractPayQuoteRequest(
                    offer.ServiceTrack,
                    offer.Kind,
                    request.EstimatedFlightHours,
                    offer.DistanceNm,
                    request.PayloadPounds,
                    request.DemandAttractiveness,
                    request.Urgency,
                    request.Difficulty,
                    offer.RelationshipStrength,
                    request.EstimatedPlayerOperatingCosts),
                payPolicy);

        var snapshot = new JobContractEconomicSnapshot(
            EconomicQuoteVersion,
            request.QuoteTime,
            request.EstimatedFlightHours,
            offer.DistanceNm,
            request.PayloadPounds,
            request.DemandAttractiveness,
            request.Urgency,
            request.Difficulty,
            offer.RelationshipStrength,
            request.EstimatedPlayerOperatingCosts,
            quote);
        snapshot.Validate();

        var contract = new JobContract(
            ContractId: offer.OfferId,
            EmployerId: request.EmployerId,
            Kind: offer.Kind,
            ServiceTrack: offer.ServiceTrack,
            OriginIcao: offer.OriginIcao,
            DestinationIcao: offer.DestinationIcao,
            Compensation: quote.Compensation,
            OfferedAt: offer.OfferedAt,
            MustStartBy: request.MustStartBy,
            MustCompleteBy: request.MustCompleteBy,
            AircraftRequirements: request.AircraftRequirements,
            Status: ContractStatus.Offered,
            ReputationReward: request.ReputationReward,
            ReputationPenalty: request.ReputationPenalty,
            MarketId: request.MarketId,
            WorldEventId: request.WorldEventId,
            GovernmentAuthorizationRequired:
                request.GovernmentAuthorizationRequired,
            EconomicSnapshot: snapshot,
            MustAcceptBy: offer.ExpiresAt);

        contract.Validate();
        return contract;
    }
}
