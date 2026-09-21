namespace OpenCareer.Domain.Events;

// Identifiers are canonical (for example airport KDFW, nation US).
// A market can span two airports; region/nation/fleet coverage is extended by the
// future world coordinator rather than pretending every scope is the same region.
public sealed record WorldEventLocation(
    string RegionId,
    string NationId,
    string OriginAirport,
    string DestinationAirport,
    string? FleetTypeId = null)
{
    public IEnumerable<string?> Targets(WorldEventScope scope) => scope switch
    {
        WorldEventScope.Global => new string?[] { null },
        WorldEventScope.Region => new string?[] { RegionId },
        WorldEventScope.National => new string?[] { NationId },
        WorldEventScope.FleetType => FleetTypeId is null ? Array.Empty<string?>() : new string?[] { FleetTypeId },
        WorldEventScope.Airport => new string?[] { OriginAirport, DestinationAirport }.Distinct(StringComparer.Ordinal),
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    public bool Matches(WorldEventInstance instance) =>
        Targets(instance.Scope).Contains(instance.ScopeTarget, StringComparer.Ordinal);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RegionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(NationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(OriginAirport);
        ArgumentException.ThrowIfNullOrWhiteSpace(DestinationAirport);
        if (FleetTypeId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(FleetTypeId);
    }
}
