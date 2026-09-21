namespace OpenCareer.SimConnect;

internal static class SimConnectLocalWeatherMapper
{
    internal static SimConnectLocalWeatherSnapshot? Map(
        IReadOnlyList<double>? values,
        DateTimeOffset timestamp)
    {
        if (values is null || values.Count != SimConnectLocalWeatherDefinition.ValueCount)
            return null;

        for (int i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i]))
                return null;
        }

        double latitude = Read(values, SimConnectLocalWeatherValue.Latitude);
        double longitude = Read(values, SimConnectLocalWeatherValue.Longitude);
        double windVelocity = Read(values, SimConnectLocalWeatherValue.WindVelocityKnots);

        if (latitude is < -90 or > 90
            || longitude is < -180 or > 180
            || windVelocity < 0)
        {
            return null;
        }

        return new(
            timestamp,
            latitude,
            longitude,
            NormalizeHeading(Read(values, SimConnectLocalWeatherValue.WindDirectionTrue)),
            windVelocity,
            Read(values, SimConnectLocalWeatherValue.DensityAltitudeFeet));
    }

    private static double Read(
        IReadOnlyList<double> values,
        SimConnectLocalWeatherValue index) =>
        values[(int)index];

    private static double NormalizeHeading(double degrees)
    {
        double normalized = degrees % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }
}
