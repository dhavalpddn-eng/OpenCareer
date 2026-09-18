using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Domain.Military;

public sealed record MilitaryObjectiveAssessment(
    string ValidatorId,
    bool IsSatisfied,
    double Quality)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ValidatorId);

        if (!double.IsFinite(Quality) || Quality is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Quality));
    }
}

public sealed record EscortObjectiveProfile(
    double MaximumHorizontalSeparationNauticalMiles,
    double MaximumVerticalSeparationFeet,
    double MaximumGroundSpeedDifferenceKnots,
    double MinimumQualifiedSeconds,
    double MinimumQualifiedFraction,
    double MaximumTelemetrySkewSeconds)
{
    // Forgiving gameplay defaults, not real military formation doctrine.
    public static EscortObjectiveProfile Default { get; } = new(
        MaximumHorizontalSeparationNauticalMiles: 3.0,
        MaximumVerticalSeparationFeet: 2_000,
        MaximumGroundSpeedDifferenceKnots: 150,
        MinimumQualifiedSeconds: 120,
        MinimumQualifiedFraction: 0.70,
        MaximumTelemetrySkewSeconds: 5);

    public void Validate()
    {
        ValidatePositive(MaximumHorizontalSeparationNauticalMiles, nameof(MaximumHorizontalSeparationNauticalMiles));
        ValidatePositive(MaximumVerticalSeparationFeet, nameof(MaximumVerticalSeparationFeet));
        ValidatePositive(MaximumGroundSpeedDifferenceKnots, nameof(MaximumGroundSpeedDifferenceKnots));

        if (!double.IsFinite(MinimumQualifiedSeconds) || MinimumQualifiedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumQualifiedSeconds));

        if (!double.IsFinite(MinimumQualifiedFraction) || MinimumQualifiedFraction is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumQualifiedFraction));

        if (!double.IsFinite(MaximumTelemetrySkewSeconds) || MaximumTelemetrySkewSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumTelemetrySkewSeconds));
    }

    private static void ValidatePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record EscortObjectiveProgress(
    double QualifiedSeconds,
    double EvaluatedSeconds,
    double HorizontalSeparationNauticalMiles,
    double VerticalSeparationFeet,
    double GroundSpeedDifferenceKnots,
    bool InEnvelope,
    DateTimeOffset UpdatedAt)
{
    public static EscortObjectiveProgress Create(DateTimeOffset time) =>
        new(0, 0, 0, 0, 0, false, time);

    public MilitaryObjectiveAssessment Assess(EscortObjectiveProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        Validate();

        double fraction = EvaluatedSeconds <= 0
            ? 0
            : Math.Clamp(QualifiedSeconds / EvaluatedSeconds, 0, 1);

        var result = new MilitaryObjectiveAssessment(
            EscortObjectiveValidator.ValidatorId,
            QualifiedSeconds >= profile.MinimumQualifiedSeconds
                && fraction >= profile.MinimumQualifiedFraction,
            fraction);

        result.Validate();
        return result;
    }

    public void Validate()
    {
        ValidateNonNegative(QualifiedSeconds, nameof(QualifiedSeconds));
        ValidateNonNegative(EvaluatedSeconds, nameof(EvaluatedSeconds));

        if (QualifiedSeconds > EvaluatedSeconds)
            throw new ArgumentException("Escort qualified time cannot exceed evaluated time.");

        ValidateNonNegative(HorizontalSeparationNauticalMiles, nameof(HorizontalSeparationNauticalMiles));
        ValidateNonNegative(VerticalSeparationFeet, nameof(VerticalSeparationFeet));
        ValidateNonNegative(GroundSpeedDifferenceKnots, nameof(GroundSpeedDifferenceKnots));
    }

    private static void ValidateNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}

public static class EscortObjectiveValidator
{
    private const double EarthRadiusNauticalMiles = 3440.065;

    public const string ValidatorId = "escort-geometry-v1";

    public static EscortObjectiveProgress Advance(
        EscortObjectiveProfile profile,
        EscortObjectiveProgress progress,
        AircraftTelemetrySnapshot player,
        AircraftTelemetrySnapshot protectedAircraft,
        double deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(protectedAircraft);

        profile.Validate();
        progress.Validate();

        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        DateTimeOffset time = player.Timestamp >= protectedAircraft.Timestamp
            ? player.Timestamp
            : protectedAircraft.Timestamp;

        if (time < progress.UpdatedAt)
            throw new ArgumentException("Escort objective telemetry cannot move backward in time.");

        double horizontal = DistanceNauticalMiles(
            player.LatitudeDegrees,
            player.LongitudeDegrees,
            protectedAircraft.LatitudeDegrees,
            protectedAircraft.LongitudeDegrees);
        double vertical = Math.Abs(player.AltitudeMslFeet - protectedAircraft.AltitudeMslFeet);
        double speedDifference = Math.Abs(player.GroundSpeedKnots - protectedAircraft.GroundSpeedKnots);

        double telemetrySkew = Math.Abs((player.Timestamp - protectedAircraft.Timestamp).TotalSeconds);
        bool usable = telemetrySkew <= profile.MaximumTelemetrySkewSeconds
            && !player.Paused
            && !protectedAircraft.Paused
            && !player.SlewActive
            && !protectedAircraft.SlewActive
            && !player.OnGround
            && !protectedAircraft.OnGround;

        if (!usable)
        {
            return progress with
            {
                HorizontalSeparationNauticalMiles = horizontal,
                VerticalSeparationFeet = vertical,
                GroundSpeedDifferenceKnots = speedDifference,
                InEnvelope = false,
                UpdatedAt = time
            };
        }

        bool inEnvelope = horizontal <= profile.MaximumHorizontalSeparationNauticalMiles
            && vertical <= profile.MaximumVerticalSeparationFeet
            && speedDifference <= profile.MaximumGroundSpeedDifferenceKnots;

        return progress with
        {
            QualifiedSeconds = progress.QualifiedSeconds + (inEnvelope ? deltaSeconds : 0),
            EvaluatedSeconds = progress.EvaluatedSeconds + deltaSeconds,
            HorizontalSeparationNauticalMiles = horizontal,
            VerticalSeparationFeet = vertical,
            GroundSpeedDifferenceKnots = speedDifference,
            InEnvelope = inEnvelope,
            UpdatedAt = time
        };
    }

    private static double DistanceNauticalMiles(
        double latitudeA,
        double longitudeA,
        double latitudeB,
        double longitudeB)
    {
        double lat1 = DegreesToRadians(latitudeA);
        double lat2 = DegreesToRadians(latitudeB);
        double deltaLat = DegreesToRadians(latitudeB - latitudeA);
        double deltaLon = DegreesToRadians(longitudeB - longitudeA);

        double a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
            + Math.Cos(lat1) * Math.Cos(lat2)
            * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        double centralAngle = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusNauticalMiles * centralAngle;
    }

    private static double DegreesToRadians(double degrees) =>
        degrees * Math.PI / 180.0;
}
