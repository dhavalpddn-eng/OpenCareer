namespace OpenCareer.SimConnect;

internal readonly record struct SimConnectTelemetryDatum(string Name, string Units);

internal enum SimConnectTelemetryValue
{
    Latitude,
    Longitude,
    AltitudeMsl,
    AltitudeAgl,
    IndicatedAirspeed,
    GroundSpeed,
    VerticalSpeedFeetPerSecond,
    HeadingTrue,
    Pitch,
    Bank,
    NormalAcceleration,
    OnGround,
    ParkingBrake,
    NumberOfEngines,
    Engine1Combustion,
    Engine2Combustion,
    Engine3Combustion,
    Engine4Combustion,
    FuelTotalWeight,
    TotalWeight,
    EmptyWeight,
    FlapsHandlePercentOver100,
    GearTotalPercent,
    SlewActive
}

internal static class SimConnectTelemetryDefinition
{
    internal const uint DefinitionId = 0x4F430001;
    internal const uint RequestId = 0x4F430002;
    internal const uint PauseEventId = 0x4F430003;

    internal static readonly SimConnectTelemetryDatum[] Data =
    [
        new("PLANE LATITUDE", "degrees"),
        new("PLANE LONGITUDE", "degrees"),
        new("PLANE ALTITUDE", "feet"),
        new("PLANE ALT ABOVE GROUND", "feet"),
        new("AIRSPEED INDICATED", "knots"),
        new("GROUND VELOCITY", "knots"),
        new("VERTICAL SPEED", "feet per second"),
        new("PLANE HEADING DEGREES TRUE", "degrees"),
        new("PLANE PITCH DEGREES", "degrees"),
        new("PLANE BANK DEGREES", "degrees"),
        new("G FORCE", "Gforce"),
        new("SIM ON GROUND", "bool"),
        new("BRAKE PARKING POSITION", "bool"),
        new("NUMBER OF ENGINES", "number"),
        new("GENERAL ENG COMBUSTION:1", "bool"),
        new("GENERAL ENG COMBUSTION:2", "bool"),
        new("GENERAL ENG COMBUSTION:3", "bool"),
        new("GENERAL ENG COMBUSTION:4", "bool"),
        new("FUEL TOTAL QUANTITY WEIGHT EX1", "pounds"),
        new("TOTAL WEIGHT", "pounds"),
        new("EMPTY WEIGHT", "pounds"),
        new("FLAPS HANDLE PERCENT", "percent over 100"),
        new("GEAR TOTAL PCT EXTENDED", "percent"),
        new("IS SLEW ACTIVE", "bool")
    ];

    internal static int ValueCount => Data.Length;
}
