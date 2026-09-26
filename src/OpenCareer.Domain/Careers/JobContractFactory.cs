using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Domain.Careers;

public sealed record JobContractCreationRequest(
    JobMarketOfferDraft Offer,
    DateTimeOffset AcceptanceTime,
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

        ValidateOffer(Offer);
        AircraftRequirements.Validate();

        if (Offer.IsLockedPreview)
        {
            throw new InvalidOperationException(
                "A locked job preview cannot become a contract.");
        }

        if (AcceptanceTime < Offer.OfferedAt
            || AcceptanceTime >= Offer.ExpiresAt)
        {
            throw new InvalidOperationException(
                "Job offer is not active at the acceptance time.");
        }

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
            throw new ArgumentOutOfRangeException(
                nameof(JobContractCreationRequest));
        }

        if (Offer.EstimatedFlightHours is { } offeredHours
            && Math.Abs(
                offeredHours - EstimatedFlightHours) > 1e-9)
        {
            throw new ArgumentException(
                "Accepted contract duration must match the market offer estimate.",
                nameof(EstimatedFlightHours));
        }

        ValidateMoney(
            EstimatedPlayerOperatingCosts,
            nameof(EstimatedPlayerOperatingCosts));

        if (MustStartBy is { } start
            && start < AcceptanceTime)
        {
            throw new ArgumentException(
                "Start deadline cannot precede acceptance.",
                nameof(MustStartBy));
        }

        if (MustCompleteBy is { } end
            && (end < AcceptanceTime
                || (MustStartBy is { } begin
                    && end < begin)))
        {
            throw new ArgumentException(
                "Completion deadline is invalid.",
                nameof(MustCompleteBy));
        }
    }

    private static void ValidateOffer(
        JobMarketOfferDraft offer)
    {
        string origin =
            JobMarketIcao.Normalize(
                offer.OriginIcao);

        string destination =
            JobMarketIcao.Normalize(
                offer.DestinationIcao);

        if (!string.Equals(
                origin,
                offer.OriginIcao,
                StringComparison.Ordinal)
            || !string.Equals(
                destination,
                offer.DestinationIcao,
                StringComparison.Ordinal)
            || offer.OfferId == Guid.Empty
            || !Enum.IsDefined(offer.ServiceTrack)
            || !Enum.IsDefined(offer.Kind)
            || !Enum.IsDefined(offer.Scenario)
            || offer.ExpiresAt <= offer.OfferedAt
            || !double.IsFinite(offer.DistanceNm)
            || offer.DistanceNm < 0
            || (offer.EstimatedFlightHours is { } hours
                && (!double.IsFinite(hours)
                    || hours < 0))
            || !double.IsFinite(offer.RouteStrength)
            || offer.RouteStrength is < 0 or > 1
            || !double.IsFinite(offer.RelationshipStrength)
            || offer.RelationshipStrength is < 0 or > 1
            || !double.IsFinite(offer.MarketSelectionWeight)
            || offer.MarketSelectionWeight < 0)
        {
            throw new ArgumentException(
                "Invalid job-market offer.",
                nameof(Offer));
        }

        offer.ContractTerms?.ValidateForOffer(offer);
    }

    private static void ValidateMoney(
        decimal value,
        string name)
    {
        if (value < 0m)
            throw new ArgumentOutOfRangeException(name);

        if (decimal.Round(
                value,
                2,
                MidpointRounding.AwayFromZero)
            != value)
        {
            throw new ArgumentException(
                "Money values must be expressed to whole cents.",
                name);
        }
    }
}

public static class JobContractFactory
{
    public static JobContract Create(
        JobContractCreationRequest request,
        ContractPayPolicy? payPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        JobMarketOfferDraft offer =
            request.Offer;

        bool isDevelopmentOffer =
            DevelopmentFlight.IsDevelopment(offer);

        bool isDevelopmentRequest =
            string.Equals(
                request.MarketId,
                DevelopmentFlight.MarketId,
                StringComparison.Ordinal);

        if (isDevelopmentOffer != isDevelopmentRequest)
        {
            throw new InvalidOperationException(
                "Development flight identity must match the persisted offer and accepted contract terms.");
        }

        if (isDevelopmentOffer)
        {
            if (!string.Equals(
                    offer.OriginIcao,
                    "KJFK",
                    StringComparison.Ordinal)
                || !string.Equals(
                    offer.DestinationIcao,
                    "KJFK",
                    StringComparison.Ordinal)
                || request.ReputationReward != 0
                || request.ReputationPenalty != 0
                || request.EstimatedPlayerOperatingCosts != 0)
            {
                throw new InvalidOperationException(
                    "Development flight terms do not match the persisted development offer.");
            }

            payPolicy =
                ContractPayPolicy.Default with
                {
                    TimeRatePerFlightHour = 0,
                    DistanceRatePerNauticalMile = 0,
                    PayloadRatePerPoundNauticalMile = 0
                };
        }

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

        var contract =
            new JobContract(
                ContractId:
                    offer.OfferId,
                EmployerId:
                    request.EmployerId,
                Kind:
                    offer.Kind,
                ServiceTrack:
                    offer.ServiceTrack,
                OriginIcao:
                    offer.OriginIcao,
                DestinationIcao:
                    offer.DestinationIcao,
                Compensation:
                    quote.Compensation,
                OfferedAt:
                    offer.OfferedAt,
                MustStartBy:
                    request.MustStartBy,
                MustCompleteBy:
                    request.MustCompleteBy,
                AircraftRequirements:
                    request.AircraftRequirements,
                Status:
                    ContractStatus.Offered,
                ReputationReward:
                    request.ReputationReward,
                ReputationPenalty:
                    request.ReputationPenalty,
                MarketId:
                    request.MarketId,
                WorldEventId:
                    request.WorldEventId,
                GovernmentAuthorizationRequired:
                    request.GovernmentAuthorizationRequired);

        contract.Validate();
        return contract;
    }
}
