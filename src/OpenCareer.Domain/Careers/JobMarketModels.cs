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
    AirportMarketCapacity? Capacity = null)
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

        if ((Access & ~JobMarketAccess.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(Access));

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
    double MarketSelectionWeight)
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
