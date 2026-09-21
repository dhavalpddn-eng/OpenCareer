namespace OpenCareer.SimConnect;

internal readonly record struct SimConnectLocalWeatherDatum(string Name, string Units);

internal enum SimConnectLocalWeatherValue
{
    Latitude,
    Longitude,
    WindDirectionTrue,
    WindVelocityKnots,
    DensityAltitudeFeet
}

internal static class SimConnectLocalWeatherDefinition
{
    internal const uint DefinitionId = 0x4F430030;
    internal const uint RequestId = 0x4F430031;

    internal static readonly SimConnectLocalWeatherDatum[] Data =
    [
        new("PLANE LATITUDE", "degrees"),
        new("PLANE LONGITUDE", "degrees"),
        new("AMBIENT WIND DIRECTION", "degrees"),
        new("AMBIENT WIND VELOCITY", "knots"),
        new("DENSITY ALTITUDE", "feet")
    ];

    internal static int ValueCount => Data.Length;
}

internal sealed record SimConnectLocalWeatherSnapshot(
    DateTimeOffset Timestamp,
    double LatitudeDegrees,
    double LongitudeDegrees,
    double WindFromTrueDegrees,
    double WindVelocityKnots,
    double DensityAltitudeFeet);
