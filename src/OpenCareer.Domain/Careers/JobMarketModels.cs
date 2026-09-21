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
