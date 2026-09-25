namespace OpenCareer.Domain.Aircraft;

/// <summary>Immutable physical identity. Creation is explicit; simulator discovery never creates this record.</summary>
public sealed record Airframe
{
    public Airframe(AirframeId airframeId, string canonicalAircraftId, DateTimeOffset createdAt)
    {
        airframeId.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);

        if (canonicalAircraftId != canonicalAircraftId.Trim() || canonicalAircraftId.Any(char.IsControl))
            throw new ArgumentException("Canonical aircraft identity must be normalized and contain no control characters.", nameof(canonicalAircraftId));

        if (createdAt == default)
            throw new ArgumentOutOfRangeException(nameof(createdAt));

        AirframeId = airframeId;
        CanonicalAircraftId = canonicalAircraftId;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public AirframeId AirframeId { get; }
    public string CanonicalAircraftId { get; }
    public DateTimeOffset CreatedAt { get; }
}
