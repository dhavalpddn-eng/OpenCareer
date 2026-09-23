using System.Security.Cryptography;
using System.Text;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Domain.Careers;

public enum JobScenarioKind
{
    Standard,
    OrganTransport,
    TroopMovement,
    HumanitarianAirlift,
    ConflictReconnaissance,
    RecoverySupply,
    InfrastructureAssessment
}

public sealed record JobMarketDestination(
    string Icao,
    double DistanceNm,
    double RouteStrength = 0,
    double RelationshipStrength = 0,
    double MarketAttractiveness = 1,
    double? EstimatedFlightHours = null,
    RouteDemandProfile? DemandProfile = null)
{
    public string NormalizedIcao =>
        JobMarketIcao.Normalize(Icao);

    public void Validate()
    {
        string normalizedIcao = NormalizedIcao;

        if (!double.IsFinite(DistanceNm) || DistanceNm < 0)
            throw new ArgumentOutOfRangeException(nameof(DistanceNm));

        ValidateUnit(RouteStrength, nameof(RouteStrength));
        ValidateUnit(RelationshipStrength, nameof(RelationshipStrength));

        if (!double.IsFinite(MarketAttractiveness)
            || MarketAttractiveness is <= 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MarketAttractiveness));
        }

        if (EstimatedFlightHours is { } hours
            && (!double.IsFinite(hours) || hours < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(EstimatedFlightHours));
        }

        if (DemandProfile is { } demand)
        {
            demand.Validate();

            if (!string.Equals(
                    demand.DestinationIcao,
                    normalizedIcao,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Route demand destination must match the job-market destination.",
                    nameof(DemandProfile));
            }
        }
    }

    private static void ValidateUnit(
        double value,
        string name)
    {
        if (!double.IsFinite(value)
            || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}

public sealed record JobMarketGenerationRequest(
    ulong GenerationSeed,
    DateTimeOffset Time,
    AirportCareerProfile Origin,
    IReadOnlyList<JobMarketDestination> Destinations,
    JobMarketAccess Access,
    int CareerLevel = 1,
    JobMarketPolicy? Policy = null,
    AirportMarketCapacity? Capacity = null,
    IReadOnlySet<ContractKind>? AllowedContractKinds = null,
    ProviderAircraftType? ProviderAircraft = null)
{
    public JobMarketPolicy EffectivePolicy =>
        Policy ?? JobMarketPolicy.Default;

    public AirportMarketCapacity EffectiveCapacity =>
        Capacity
        ?? AirportMarketCapacity.ForScale(
            AirportMarketScale.Regional);

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Origin);
        ArgumentNullException.ThrowIfNull(Destinations);

        Origin.Validate();
        EffectivePolicy.Validate();
        EffectiveCapacity.Validate();
        ProviderAircraft?.Validate();

        if ((Access & ~JobMarketAccess.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(Access));

        if (AllowedContractKinds is not null
            && AllowedContractKinds.Any(
                static kind => !Enum.IsDefined(kind)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(AllowedContractKinds));
        }

        if (CareerLevel < 1
            || CareerLevel > EffectivePolicy.CareerLevelCap)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CareerLevel));
        }

        if (Destinations.Count == 0)
        {
            throw new ArgumentException(
                "At least one destination is required.",
                nameof(Destinations));
        }

        string originIcao =
            JobMarketIcao.Normalize(Origin.Icao);

        foreach (JobMarketDestination destination in Destinations)
        {
            destination.Validate();

            if (destination.DemandProfile is not { } demand)
                continue;

            if (!string.Equals(
                    demand.OriginIcao,
                    originIcao,
                    StringComparison.Ordinal)
                || !string.Equals(
                    demand.DestinationIcao,
                    destination.NormalizedIcao,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Route demand profile must match the job-market origin and destination.",
                    nameof(Destinations));
            }
        }
    }
}

public sealed record ProviderAircraftType(
    string AircraftId,
    string DisplayName)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
    }
}

public sealed record ProviderAircraftAssignment(
    Guid ProviderAircraftInstanceId,
    string AircraftId,
    string DisplayName,
    string OriginIcao)
{
    public static ProviderAircraftAssignment CreateForOffer(
        Guid offerId,
        ProviderAircraftType aircraft,
        string originIcao)
    {
        if (offerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Offer identity is required.",
                nameof(offerId));
        }

        ArgumentNullException.ThrowIfNull(aircraft);
        aircraft.Validate();

        byte[] identityBytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    $"opencareer:provider-aircraft:v1:{offerId:D}"));

        Guid instanceId =
            new(identityBytes.AsSpan(0, 16));

        if (instanceId == Guid.Empty
            || instanceId == offerId)
        {
            throw new InvalidOperationException(
                "Deterministic provider-aircraft identity collided with the contract identity.");
        }

        return new(
            instanceId,
            aircraft.AircraftId.Trim(),
            aircraft.DisplayName.Trim(),
            JobMarketIcao.Normalize(originIcao));
    }

    public void Validate(
        string expectedOriginIcao)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);

        string origin =
            JobMarketIcao.Normalize(OriginIcao);

        string expectedOrigin =
            JobMarketIcao.Normalize(expectedOriginIcao);

        if (ProviderAircraftInstanceId == Guid.Empty
            || !string.Equals(
                origin,
                OriginIcao,
                StringComparison.Ordinal)
            || !string.Equals(
                origin,
                expectedOrigin,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Provider-aircraft assignment identity or origin is invalid.");
        }
    }
}

public sealed record JobMarketContractTermsEnvelope(
    string AuthorityId,
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
    bool GovernmentAuthorizationRequired = false,
    PilotQualificationState? RequiredPilotQualifications = null,
    AircraftAccess AuthorizedAircraftAccess = AircraftAccess.None,
    ProviderAircraftAssignment? ProviderAircraft = null)
{
    public void ValidateForOffer(
        JobMarketOfferDraft offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentException.ThrowIfNullOrWhiteSpace(AuthorityId);
        ArgumentNullException.ThrowIfNull(AircraftRequirements);

        AircraftRequirements.Validate();
        RequiredPilotQualifications?.Validate();
        ProviderAircraft?.Validate(
            offer.OriginIcao);

        if (ProviderAircraft?.ProviderAircraftInstanceId
            == offer.OfferId)
        {
            throw new ArgumentException(
                "Provider-aircraft instance identity must be distinct from the offer identity.");
        }

        if ((AuthorizedAircraftAccess & ~AircraftAccess.Any) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AuthorizedAircraftAccess));
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
                nameof(JobMarketContractTermsEnvelope));
        }

        if (decimal.Round(
                EstimatedPlayerOperatingCosts,
                2,
                MidpointRounding.AwayFromZero)
                != EstimatedPlayerOperatingCosts
            || EstimatedPlayerOperatingCosts < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EstimatedPlayerOperatingCosts));
        }

        if (offer.EstimatedFlightHours is { } offeredHours
            && Math.Abs(
                offeredHours - EstimatedFlightHours) > 1e-9)
        {
            throw new ArgumentException(
                "Contract-term duration must match the persisted market offer.");
        }

        if (AircraftRequirements.MinimumRangeNauticalMiles + 1e-9
            < offer.DistanceNm)
        {
            throw new ArgumentException(
                "Contract-term aircraft range cannot understate the persisted offer route.");
        }

        if (MustStartBy is { } start
            && start < offer.OfferedAt)
        {
            throw new ArgumentException(
                "Contract-term start deadline cannot precede the offer.");
        }

        if (MustCompleteBy is { } complete
            && (complete < offer.OfferedAt
                || (MustStartBy is { } completionStart
                    && complete < completionStart)))
        {
            throw new ArgumentException(
                "Contract-term completion deadline is invalid.");
        }

        if (MarketId is not null
            && string.IsNullOrWhiteSpace(MarketId))
        {
            throw new ArgumentException(
                "Market identity must be non-empty when supplied.");
        }

        if (WorldEventId is not null
            && string.IsNullOrWhiteSpace(WorldEventId))
        {
            throw new ArgumentException(
                "World-event identity must be non-empty when supplied.");
        }
    }
}

public sealed record JobMarketOfferDraft(
    Guid OfferId,
    ServiceTrack ServiceTrack,
    ContractKind Kind,
    JobScenarioKind Scenario,
    string OriginIcao,
    string DestinationIcao,
    double DistanceNm,
    double? EstimatedFlightHours,
    DateTimeOffset OfferedAt,
    DateTimeOffset ExpiresAt,
    bool IsLockedPreview,
    double RouteStrength,
    double RelationshipStrength,
    double MarketSelectionWeight,
    JobMarketContractTermsEnvelope? ContractTerms = null)
{
    public bool IsLocalOperation =>
        string.Equals(OriginIcao, DestinationIcao, StringComparison.Ordinal);
}

internal static class JobMarketIcao
{
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 4
            || normalized.Any(c => c is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "A four-letter ICAO airport identifier is required.",
                nameof(value));
        }

        return normalized;
    }
}
