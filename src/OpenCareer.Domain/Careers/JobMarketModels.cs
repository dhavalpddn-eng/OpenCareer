namespace OpenCareer.Domain.Careers;

public sealed record JobMarketDestination(
    string Icao,
    double DistanceNm,
    double RouteStrength = 0,
    double RelationshipStrength = 0,
    double MarketAttractiveness = 1)
{
    public string NormalizedIcao => JobMarketIcao.Normalize(Icao);

    public void Validate()
    {
        _ = NormalizedIcao;
        if (!double.IsFinite(DistanceNm) || DistanceNm < 0)
            throw new ArgumentOutOfRangeException(nameof(DistanceNm));
        ValidateUnit(RouteStrength, nameof(RouteStrength));
        ValidateUnit(RelationshipStrength, nameof(RelationshipStrength));
        if (!double.IsFinite(MarketAttractiveness) || MarketAttractiveness is <= 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(MarketAttractiveness));
    }

    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record JobMarketGenerationRequest(
    ulong CareerSeed,
    DateTimeOffset Time,
    AirportCareerProfile Origin,
    IReadOnlyList<JobMarketDestination> Destinations,
    JobMarketAccess Access,
    JobMarketPolicy? Policy = null)
{
    public JobMarketPolicy EffectivePolicy => Policy ?? JobMarketPolicy.Default;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Origin);
        ArgumentNullException.ThrowIfNull(Destinations);
        Origin.Validate();
        EffectivePolicy.Validate();
        if (Destinations.Count == 0)
            throw new ArgumentException("At least one destination is required.", nameof(Destinations));
        foreach (var destination in Destinations)
            destination.Validate();
    }
}

public sealed record JobMarketOfferDraft(
    Guid OfferId,
    ServiceTrack ServiceTrack,
    ContractKind Kind,
    string OriginIcao,
    string DestinationIcao,
    double DistanceNm,
    DateTimeOffset OfferedAt,
    DateTimeOffset ExpiresAt,
    bool IsLockedPreview,
    double RouteStrength,
    double RelationshipStrength,
    double MarketSelectionWeight)
{
    public bool IsLocalOperation => OriginIcao == DestinationIcao;
}

internal static class JobMarketIcao
{
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 4 || normalized.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("A four-letter ICAO airport identifier is required.", nameof(value));
        return normalized;
    }
}
