namespace OpenCareer.Domain.Aircraft;

[Flags]
public enum AircraftCapability
{
    None = 0,
    Passenger = 1 << 0,
    Cargo = 1 << 1,
    Medical = 1 << 2,
    ShortField = 1 << 3,
    RoughField = 1 << 4,
    Amphibious = 1 << 5,
    Helicopter = 1 << 6,
    Agriculture = 1 << 7,
    Firefighting = 1 << 8,
    GliderTow = 1 << 9,
    Skydiving = 1 << 10,
    Survey = 1 << 11,
    Training = 1 << 12,
    Supersonic = 1 << 13,
    Military = 1 << 14,
    Fighter = 1 << 15,
    Tanker = 1 << 16,
    Surveillance = 1 << 17,
    StrategicTransport = 1 << 18,
    CarrierCapable = 1 << 19
}

public sealed record AircraftCapabilityProfile(
    string AircraftId,
    string DisplayName,
    AircraftCapability Capabilities,
    double MaximumPayloadPounds,
    double MaximumRangeNauticalMiles,
    double TypicalCruiseKnots,
    int Seats,
    int EngineCount,
    bool IfrCapable,
    bool Pressurized,
    bool RetractableGear,
    bool IsGovernmentOnly = false)
{
    public bool Has(AircraftCapability capability) =>
        (Capabilities & capability) == capability;

    public bool Satisfies(AircraftMissionRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        return MaximumPayloadPounds >= requirements.MinimumPayloadPounds
            && MaximumRangeNauticalMiles >= requirements.MinimumRangeNauticalMiles
            && TypicalCruiseKnots >= requirements.MinimumCruiseKnots
            && Seats >= requirements.MinimumSeats
            && (!requirements.RequiresIfr || IfrCapable)
            && (!requirements.RequiresPressurization || Pressurized)
            && Has(requirements.RequiredCapabilities)
            && (requirements.AllowGovernmentOnlyAircraft || !IsGovernmentOnly);
    }
}

public sealed record AircraftMissionRequirements(
    AircraftCapability RequiredCapabilities = AircraftCapability.None,
    double MinimumPayloadPounds = 0,
    double MinimumRangeNauticalMiles = 0,
    double MinimumCruiseKnots = 0,
    int MinimumSeats = 1,
    bool RequiresIfr = false,
    bool RequiresPressurization = false,
    bool AllowGovernmentOnlyAircraft = false);
