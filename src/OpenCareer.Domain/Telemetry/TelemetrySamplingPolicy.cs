namespace OpenCareer.Domain.Telemetry;

public static class TelemetrySamplingPolicy
{
    public static double GetTargetHz(AircraftTelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Paused)
        {
            return 0.20;
        }

        if (snapshot.OnGround)
        {
            if (snapshot.GroundSpeedKnots < 1.0 && snapshot.EnginesRunning == 0)
            {
                return 0.25;
            }

            if (snapshot.GroundSpeedKnots < 1.0)
            {
                return 0.50;
            }

            return 2.0;
        }

        if (snapshot.AltitudeAglFeet <= 100)
        {
            return 20.0;
        }

        if (snapshot.AltitudeAglFeet <= 500)
        {
            return 10.0;
        }

        if (snapshot.AltitudeAglFeet <= 2_000)
        {
            return 5.0;
        }

        if (snapshot.VerticalSpeedFeetPerMinute < -300)
        {
            return 2.0;
        }

        return 1.0;
    }
}
