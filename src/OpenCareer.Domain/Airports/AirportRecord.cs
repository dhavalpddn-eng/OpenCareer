namespace OpenCareer.Domain.Airports;

public enum RunwaySurface
{
    Unknown = 0,
    Asphalt,
    Concrete,
    Grass,
    Gravel,
    Dirt,
    Water,
    SnowOrIce,
    Other
}

public sealed record RunwayRecord(
    string Identifier,
    double? UsableLengthFeet,
    double? WidthFeet,
    RunwaySurface Surface,
    bool IsClosed = false)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Identifier);

        if (UsableLengthFeet is { } length
            && (!double.IsFinite(length) || length <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(UsableLengthFeet));
        }

        if (WidthFeet is { } width
            && (!double.IsFinite(width) || width <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(WidthFeet));
        }

        if (!Enum.IsDefined(Surface))
            throw new ArgumentOutOfRangeException(nameof(Surface));
    }
}

public sealed record AirportRecord(
    string Icao,
    string Name,
    IReadOnlyList<RunwayRecord> Runways)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Icao);
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentNullException.ThrowIfNull(Runways);

        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (RunwayRecord runway in Runways)
        {
            ArgumentNullException.ThrowIfNull(runway);
            runway.Validate();

            if (!identifiers.Add(runway.Identifier))
                throw new ArgumentException("Airport contains duplicate runway identifiers.", nameof(Runways));
        }
    }
}
