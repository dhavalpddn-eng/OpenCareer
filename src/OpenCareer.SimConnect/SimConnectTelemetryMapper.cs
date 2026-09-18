using OpenCareer.Domain.Telemetry;

namespace OpenCareer.SimConnect;

internal static class SimConnectTelemetryMapper
{
    internal static AircraftTelemetrySnapshot? Map(
        IReadOnlyList<double>? values,
        DateTimeOffset timestamp,
        bool paused)
    {
        if (values is null || values.Count != SimConnectTelemetryDefinition.ValueCount)
            return null;

        for (int i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i]))
                return null;
        }

        double fuelPounds = Math.Max(0, Read(values, SimConnectTelemetryValue.FuelTotalWeight));
        double totalWeight = Math.Max(0, Read(values, SimConnectTelemetryValue.TotalWeight));
        double emptyWeight = Math.Max(0, Read(values, SimConnectTelemetryValue.EmptyWeight));
        double payloadPounds = Math.Max(0, totalWeight - emptyWeight - fuelPounds);

        bool gearRetractable = IsTrue(Read(values, SimConnectTelemetryValue.GearRetractable));
        double gearCenterPercent = NormalizePercentOver100(
            Read(values, SimConnectTelemetryValue.GearCenterPositionOver100));
        double gearLeftPercent = NormalizePercentOver100(
            Read(values, SimConnectTelemetryValue.GearLeftPositionOver100));
        double gearRightPercent = NormalizePercentOver100(
            Read(values, SimConnectTelemetryValue.GearRightPositionOver100));
        double gearTotalPercent = Math.Clamp(
            Read(values, SimConnectTelemetryValue.GearTotalPercent),
            0,
            100);
        bool gearDown = ResolveGearDown(
            gearRetractable,
            gearTotalPercent,
            gearCenterPercent,
            gearLeftPercent,
            gearRightPercent);

        int installedEngines = Math.Clamp(
            (int)Math.Round(Read(values, SimConnectTelemetryValue.NumberOfEngines), MidpointRounding.AwayFromZero),
            0,
            4);
        int enginesRunning = 0;
        for (int i = 0; i < installedEngines; i++)
        {
            var index = (SimConnectTelemetryValue)((int)SimConnectTelemetryValue.Engine1Combustion + i);
            if (IsTrue(Read(values, index)))
                enginesRunning++;
        }

        return new AircraftTelemetrySnapshot(
            timestamp,
            Read(values, SimConnectTelemetryValue.Latitude),
            Read(values, SimConnectTelemetryValue.Longitude),
            Read(values, SimConnectTelemetryValue.AltitudeMsl),
            Read(values, SimConnectTelemetryValue.AltitudeAgl),
            Math.Max(0, Read(values, SimConnectTelemetryValue.IndicatedAirspeed)),
            Math.Max(0, Read(values, SimConnectTelemetryValue.GroundSpeed)),
            Read(values, SimConnectTelemetryValue.VerticalSpeedFeetPerSecond) * 60.0,
            NormalizeHeading(Read(values, SimConnectTelemetryValue.HeadingTrue)),
            Read(values, SimConnectTelemetryValue.Pitch),
            Read(values, SimConnectTelemetryValue.Bank),
            Read(values, SimConnectTelemetryValue.NormalAcceleration),
            IsTrue(Read(values, SimConnectTelemetryValue.OnGround)),
            IsTrue(Read(values, SimConnectTelemetryValue.ParkingBrake)),
            enginesRunning,
            fuelPounds,
            payloadPounds,
            NormalizePercentOver100(
                Read(values, SimConnectTelemetryValue.FlapsHandlePercentOver100)),
            gearDown,
            paused,
            IsTrue(Read(values, SimConnectTelemetryValue.SlewActive)),
            gearRetractable,
            gearCenterPercent,
            gearLeftPercent,
            gearRightPercent);
    }

    private static double Read(IReadOnlyList<double> values, SimConnectTelemetryValue index) =>
        values[(int)index];

    private static bool IsTrue(double value) => value != 0;

    private static double NormalizePercentOver100(double value) =>
        Math.Clamp(value * 100.0, 0, 100);

    private static bool ResolveGearDown(
        bool retractable,
        double totalPercent,
        double centerPercent,
        double leftPercent,
        double rightPercent)
    {
        if (!retractable)
            return true;

        if (totalPercent >= 95.0)
            return true;

        int individuallyExtended = 0;
        if (centerPercent >= 95.0) individuallyExtended++;
        if (leftPercent >= 95.0) individuallyExtended++;
        if (rightPercent >= 95.0) individuallyExtended++;

        return individuallyExtended >= 2;
    }

    private static double NormalizeHeading(double degrees)
    {
        double normalized = degrees % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }
}
