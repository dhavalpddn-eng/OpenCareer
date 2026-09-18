namespace OpenCareer.Domain.Military;

public enum SimulatedThreatCategory
{
    AirspaceHazard,
    AirborneOpposition,
    GroundBasedOpposition,
    ElectronicInterference,
    NavigationDisruption
}

public enum SimulatedThreatLevel
{
    None,
    Low,
    Moderate,
    High,
    Critical
}

public sealed record SimulatedThreatZone(
    string ThreatId,
    SimulatedThreatCategory Category,
    double LatitudeDegrees,
    double LongitudeDegrees,
    double RadiusNauticalMiles,
    double Severity,
    double Confidence,
    DateTimeOffset ActiveFrom,
    DateTimeOffset? ActiveUntil = null,
    double? MinimumAltitudeFeet = null,
    double? MaximumAltitudeFeet = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ThreatId);

        if (!Enum.IsDefined(Category))
            throw new ArgumentOutOfRangeException(nameof(Category));

        if (!double.IsFinite(LatitudeDegrees) || LatitudeDegrees is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(LatitudeDegrees));

        if (!double.IsFinite(LongitudeDegrees) || LongitudeDegrees is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(LongitudeDegrees));

        if (!double.IsFinite(RadiusNauticalMiles) || RadiusNauticalMiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(RadiusNauticalMiles));

        ValidateUnit(Severity, nameof(Severity));
        ValidateUnit(Confidence, nameof(Confidence));

        if (ActiveUntil is { } until && until <= ActiveFrom)
            throw new ArgumentException("Threat-zone end must follow its start.");

        if (MinimumAltitudeFeet is { } minimum && !double.IsFinite(minimum))
            throw new ArgumentOutOfRangeException(nameof(MinimumAltitudeFeet));

        if (MaximumAltitudeFeet is { } maximum && !double.IsFinite(maximum))
            throw new ArgumentOutOfRangeException(nameof(MaximumAltitudeFeet));

        if (MinimumAltitudeFeet is { } min
            && MaximumAltitudeFeet is { } max
            && max < min)
        {
            throw new ArgumentException("Threat-zone altitude maximum cannot be below its minimum.");
        }
    }

    public bool IsActive(DateTimeOffset time) =>
        time >= ActiveFrom && (ActiveUntil is null || time < ActiveUntil);

    public bool ContainsAltitude(double altitudeFeet)
    {
        if (!double.IsFinite(altitudeFeet))
            return false;

        return (MinimumAltitudeFeet is null || altitudeFeet >= MinimumAltitudeFeet)
            && (MaximumAltitudeFeet is null || altitudeFeet <= MaximumAltitudeFeet);
    }

    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record MilitaryThreatSample(
    DateTimeOffset Time,
    double LatitudeDegrees,
    double LongitudeDegrees,
    double AltitudeFeet)
{
    public void Validate()
    {
        if (!double.IsFinite(LatitudeDegrees) || LatitudeDegrees is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(LatitudeDegrees));
        if (!double.IsFinite(LongitudeDegrees) || LongitudeDegrees is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(LongitudeDegrees));
        if (!double.IsFinite(AltitudeFeet))
            throw new ArgumentOutOfRangeException(nameof(AltitudeFeet));
    }
}

public sealed record MilitaryThreatExposure(
    double Pressure,
    SimulatedThreatLevel Level,
    IReadOnlyList<string> ContributingThreatIds)
{
    public void Validate()
    {
        if (!double.IsFinite(Pressure) || Pressure is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Pressure));
        if (!Enum.IsDefined(Level))
            throw new ArgumentOutOfRangeException(nameof(Level));
    }
}

public sealed record MilitaryThreatExposureSummary(
    double MeanPressure,
    double PeakPressure,
    double ExposedSeconds,
    double TotalSeconds,
    double MissionRiskIndex)
{
    public void Validate()
    {
        ValidateUnit(MeanPressure, nameof(MeanPressure));
        ValidateUnit(PeakPressure, nameof(PeakPressure));
        ValidateUnit(MissionRiskIndex, nameof(MissionRiskIndex));

        if (!double.IsFinite(ExposedSeconds) || ExposedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(ExposedSeconds));
        if (!double.IsFinite(TotalSeconds) || TotalSeconds < 0 || ExposedSeconds > TotalSeconds)
            throw new ArgumentOutOfRangeException(nameof(TotalSeconds));
    }

    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public static class MilitaryThreatEvaluator
{
    private const double EarthRadiusNauticalMiles = 3440.065;

    public static MilitaryThreatExposure Evaluate(
        MilitaryThreatSample sample,
        IReadOnlyList<SimulatedThreatZone> zones)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(zones);

        sample.Validate();

        var complement = 1.0;
        var contributors = new List<string>();

        foreach (var zone in zones)
        {
            zone.Validate();

            if (!zone.IsActive(sample.Time) || !zone.ContainsAltitude(sample.AltitudeFeet))
                continue;

            var distance = DistanceNauticalMiles(
                sample.LatitudeDegrees,
                sample.LongitudeDegrees,
                zone.LatitudeDegrees,
                zone.LongitudeDegrees);

            if (distance >= zone.RadiusNauticalMiles)
                continue;

            var radialFactor = 1 - (distance / zone.RadiusNauticalMiles);
            var pressure = Math.Clamp(
                zone.Severity * zone.Confidence * radialFactor,
                0,
                1);

            if (pressure <= 0)
                continue;

            complement *= 1 - pressure;
            contributors.Add(zone.ThreatId);
        }

        var combined = Math.Clamp(1 - complement, 0, 1);
        var result = new MilitaryThreatExposure(
            combined,
            LevelFor(combined),
            contributors);

        result.Validate();
        return result;
    }

    public static MilitaryThreatExposureSummary Summarize(
        IReadOnlyList<(MilitaryThreatExposure Exposure, double Seconds)> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count == 0)
            return new MilitaryThreatExposureSummary(0, 0, 0, 0, 0);

        var totalSeconds = 0.0;
        var weightedPressure = 0.0;
        var exposedSeconds = 0.0;
        var peak = 0.0;

        foreach (var (exposure, seconds) in samples)
        {
            ArgumentNullException.ThrowIfNull(exposure);
            exposure.Validate();

            if (!double.IsFinite(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(samples));

            totalSeconds += seconds;
            weightedPressure += exposure.Pressure * seconds;
            if (exposure.Pressure > 0.05)
                exposedSeconds += seconds;
            peak = Math.Max(peak, exposure.Pressure);
        }

        var mean = totalSeconds <= 0 ? 0 : weightedPressure / totalSeconds;
        var risk = Math.Clamp((0.65 * mean) + (0.35 * peak), 0, 1);

        var result = new MilitaryThreatExposureSummary(
            mean,
            peak,
            exposedSeconds,
            totalSeconds,
            risk);

        result.Validate();
        return result;
    }

    private static SimulatedThreatLevel LevelFor(double pressure) =>
        pressure switch
        {
            < 0.05 => SimulatedThreatLevel.None,
            < 0.25 => SimulatedThreatLevel.Low,
            < 0.50 => SimulatedThreatLevel.Moderate,
            < 0.75 => SimulatedThreatLevel.High,
            _ => SimulatedThreatLevel.Critical
        };

    private static double DistanceNauticalMiles(
        double latitudeA,
        double longitudeA,
        double latitudeB,
        double longitudeB)
    {
        var lat1 = DegreesToRadians(latitudeA);
        var lat2 = DegreesToRadians(latitudeB);
        var deltaLat = DegreesToRadians(latitudeB - latitudeA);
        var deltaLon = DegreesToRadians(longitudeB - longitudeA);

        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
            + Math.Cos(lat1) * Math.Cos(lat2)
            * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        var centralAngle = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusNauticalMiles * centralAngle;
    }

    private static double DegreesToRadians(double degrees) =>
        degrees * Math.PI / 180.0;
}
