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

public sealed record RunwayEndRecord(
    string Identifier,
    double TrueHeadingDegrees,
    bool IsClosed = false)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Identifier);

        if (!double.IsFinite(TrueHeadingDegrees)
            || TrueHeadingDegrees < 0
            || TrueHeadingDegrees >= 360)
        {
            throw new ArgumentOutOfRangeException(nameof(TrueHeadingDegrees));
        }
    }
}

public sealed record RunwayRecord(
    string Identifier,
    double? UsableLengthFeet,
    double? WidthFeet,
    RunwaySurface Surface,
    bool IsClosed = false,
    IReadOnlyList<RunwayEndRecord>? Ends = null)
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

        if (Ends is not null)
        {
            var endIdentifiers = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (RunwayEndRecord end in Ends)
            {
                ArgumentNullException.ThrowIfNull(end);
                end.Validate();

                if (!endIdentifiers.Add(end.Identifier))
                {
                    throw new ArgumentException(
                        "Runway contains duplicate end identifiers.",
                        nameof(Ends));
                }
            }
        }
    }
}

public sealed record AirportRecord(
    string Icao,
    string Name,
    IReadOnlyList<RunwayRecord> Runways,
    double? LatitudeDegrees = null,
    double? LongitudeDegrees = null)
{
    public bool HasPosition =>
        LatitudeDegrees is not null
        && LongitudeDegrees is not null;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Icao);
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentNullException.ThrowIfNull(Runways);

        if ((LatitudeDegrees is null) != (LongitudeDegrees is null))
        {
            throw new ArgumentException(
                "Airport latitude and longitude must be supplied together.");
        }

        if (LatitudeDegrees is { } latitude
            && (!double.IsFinite(latitude)
                || latitude is < -90 or > 90))
        {
            throw new ArgumentOutOfRangeException(nameof(LatitudeDegrees));
        }

        if (LongitudeDegrees is { } longitude
            && (!double.IsFinite(longitude)
                || longitude is < -180 or > 180))
        {
            throw new ArgumentOutOfRangeException(nameof(LongitudeDegrees));
        }

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
