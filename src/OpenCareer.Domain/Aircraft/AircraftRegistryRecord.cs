namespace OpenCareer.Domain.Aircraft;

public enum AircraftDataConfidence
{
    Unknown = 0,
    Inferred = 1,
    Reference = 2,
    Verified = 3
}

[Flags]
public enum RunwaySurfaceSupport
{
    None = 0,
    Asphalt = 1 << 0,
    Concrete = 1 << 1,
    Grass = 1 << 2,
    Gravel = 1 << 3,
    Dirt = 1 << 4,
    Water = 1 << 5,
    SnowOrIce = 1 << 6,
    Other = 1 << 7,
    Any = Asphalt | Concrete | Grass | Gravel | Dirt | Water | SnowOrIce | Other
}

/// <summary>
/// Conservative reference minima for baseline runway compatibility.
/// This is not a weight/weather takeoff-performance calculator; later dispatch
/// planning may provide more restrictive requirements for the actual operation.
/// Null means the value is not known and must not be fabricated.
/// </summary>
public sealed record AircraftRunwayPerformanceProfile(
    double? MinimumTakeoffRunwayFeet,
    double? MinimumLandingRunwayFeet,
    double? MinimumRunwayWidthFeet,
    RunwaySurfaceSupport? SupportedSurfaces,
    AircraftDataConfidence Confidence,
    string? Source = null)
{
    public void Validate()
    {
        ValidateOptionalPositive(MinimumTakeoffRunwayFeet, nameof(MinimumTakeoffRunwayFeet));
        ValidateOptionalPositive(MinimumLandingRunwayFeet, nameof(MinimumLandingRunwayFeet));
        ValidateOptionalPositive(MinimumRunwayWidthFeet, nameof(MinimumRunwayWidthFeet));

        if (SupportedSurfaces is { } surfaces
            && (surfaces == RunwaySurfaceSupport.None
                || (surfaces & ~RunwaySurfaceSupport.Any) != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(SupportedSurfaces));
        }

        if (!Enum.IsDefined(Confidence))
            throw new ArgumentOutOfRangeException(nameof(Confidence));

        if (Source is not null && string.IsNullOrWhiteSpace(Source))
            throw new ArgumentException("Performance source must be non-empty when supplied.", nameof(Source));
    }

    private static void ValidateOptionalPositive(double? value, string name)
    {
        if (value is { } number && (!double.IsFinite(number) || number <= 0))
            throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>
/// Provider-neutral registry entry. Installed-aircraft discovery and external
/// reference adapters populate this record later; the domain does not depend on
/// SimConnect or a specific aviation-data provider.
/// </summary>
public sealed record AircraftRegistryRecord(
    AircraftCapabilityProfile Capabilities,
    bool IsInstalled,
    AircraftRunwayPerformanceProfile? RunwayPerformance)
{
    public string AircraftId => Capabilities.AircraftId;
    public string DisplayName => Capabilities.DisplayName;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Capabilities);
        Capabilities.Validate();
        RunwayPerformance?.Validate();
    }
}
