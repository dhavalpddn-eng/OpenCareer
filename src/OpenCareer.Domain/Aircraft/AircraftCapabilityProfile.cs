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
    CarrierCapable = 1 << 19,
    BannerTow = 1 << 20,
    Trainer = 1 << 21,
    MaritimePatrol = 1 << 22,
    AerialRefuelingReceiver = 1 << 23
}

[Flags]
public enum AircraftAccess
{
    None = 0,
    Civilian = 1 << 0,
    Government = 1 << 1,
    Military = 1 << 2,
    Any = Civilian | Government | Military
}

public sealed record AircraftCapabilityProfile(
    string AircraftId,
    string DisplayName,
    AircraftCapability Capabilities,
    AircraftAccess Access,
    double MaximumPayloadPounds,
    double MaximumRangeNauticalMiles,
    double TypicalCruiseKnots,
    int Seats,
    int EngineCount,
    bool IfrCapable,
    bool Pressurized,
    bool RetractableGear)
{
    public bool Has(AircraftCapability capability) =>
        (Capabilities & capability) == capability;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AircraftId);
        if (!double.IsFinite(MaximumPayloadPounds) || MaximumPayloadPounds < 0
            || !double.IsFinite(MaximumRangeNauticalMiles) || MaximumRangeNauticalMiles < 0
            || !double.IsFinite(TypicalCruiseKnots) || TypicalCruiseKnots < 0
            || Seats < 0 || EngineCount < 0 || Access == AircraftAccess.None
            || (Access & ~AircraftAccess.Any) != 0)
            throw new ArgumentException("Invalid aircraft capability profile.");
    }

    public bool Satisfies(AircraftMissionRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        Validate();
        requirements.Validate();

        return MaximumPayloadPounds >= requirements.MinimumPayloadPounds
            && MaximumRangeNauticalMiles >= requirements.MinimumRangeNauticalMiles
            && TypicalCruiseKnots >= requirements.MinimumCruiseKnots
            && Seats >= requirements.MinimumSeats
            && (!requirements.RequiresIfr || IfrCapable)
            && (!requirements.RequiresPressurization || Pressurized)
            && Has(requirements.RequiredCapabilities)
            && (Access & requirements.AllowedAccess) != 0;
    }
}

public sealed record AircraftMissionRequirements(
    AircraftCapability RequiredCapabilities = AircraftCapability.None,
    AircraftAccess AllowedAccess = AircraftAccess.Civilian,
    double MinimumPayloadPounds = 0,
    double MinimumRangeNauticalMiles = 0,
    double MinimumCruiseKnots = 0,
    int MinimumSeats = 1,
    bool RequiresIfr = false,
    bool RequiresPressurization = false)
{
    public void Validate()
    {
        if (!double.IsFinite(MinimumPayloadPounds) || MinimumPayloadPounds < 0
            || !double.IsFinite(MinimumRangeNauticalMiles) || MinimumRangeNauticalMiles < 0
            || !double.IsFinite(MinimumCruiseKnots) || MinimumCruiseKnots < 0
            || MinimumSeats < 0 || AllowedAccess == AircraftAccess.None
            || (AllowedAccess & ~AircraftAccess.Any) != 0)
            throw new ArgumentException("Invalid mission requirements.");
    }
}
