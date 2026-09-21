namespace OpenCareer.Domain.Airports;

public enum AirportDataAuthority
{
    Reference = 0,
    LocalSimulator = 100
}

public sealed record AirportDataProvenance(
    string SourceId,
    AirportDataAuthority Authority,
    DateTimeOffset ObservedAt,
    string? Revision = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceId);

        if (!Enum.IsDefined(Authority))
            throw new ArgumentOutOfRangeException(nameof(Authority));

        if (ObservedAt == default)
            throw new ArgumentOutOfRangeException(nameof(ObservedAt));

        if (Revision is not null && string.IsNullOrWhiteSpace(Revision))
            throw new ArgumentException("Revision must be non-empty when supplied.", nameof(Revision));
    }
}

public sealed record AirportDataObservation(
    AirportRecord Airport,
    AirportDataProvenance Provenance)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Airport);
        ArgumentNullException.ThrowIfNull(Provenance);

        Airport.Validate();
        Provenance.Validate();
    }
}
