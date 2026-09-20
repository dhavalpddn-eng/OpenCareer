using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Application.Flights;

public sealed record FlightContinuityPolicyOptions(
    double GroundMaximumDistanceNauticalMiles = 5,
    double GroundMaximumAltitudeDeltaFeet = 5_000,
    double AirborneBaseDistanceNauticalMiles = 20,
    double AirborneMaximumTravelSpeedKnots = 800,
    double AirborneMaximumAltitudeDeltaFeet = 60_000)
{
    public void Validate()
    {
        ValidatePositiveFinite(
            GroundMaximumDistanceNauticalMiles,
            nameof(GroundMaximumDistanceNauticalMiles));

        ValidatePositiveFinite(
            GroundMaximumAltitudeDeltaFeet,
            nameof(GroundMaximumAltitudeDeltaFeet));

        ValidatePositiveFinite(
            AirborneBaseDistanceNauticalMiles,
            nameof(AirborneBaseDistanceNauticalMiles));

        ValidatePositiveFinite(
            AirborneMaximumTravelSpeedKnots,
            nameof(AirborneMaximumTravelSpeedKnots));

        ValidatePositiveFinite(
            AirborneMaximumAltitudeDeltaFeet,
            nameof(AirborneMaximumAltitudeDeltaFeet));
    }

    private static void ValidatePositiveFinite(
        double value,
        string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed class FlightContinuityPolicy
{
    private readonly FlightContinuityPolicyOptions _options;

    public FlightContinuityPolicy(
        FlightContinuityPolicyOptions? options = null)
    {
        _options =
            options
            ?? new FlightContinuityPolicyOptions();

        _options.Validate();
    }

    public bool IsPlausible(
        FlightSession session,
        AircraftTelemetrySnapshot telemetry)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(telemetry);

        if (!HasFinitePosition(telemetry))
            return false;

        FlightContinuityAnchor? anchor =
            session.ContinuityAnchor;

        if (anchor is null)
        {
            return IsGroundPhase(session)
                && telemetry.OnGround
                && telemetry.GroundSpeedKnots < 80;
        }

        try
        {
            anchor.Validate();
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        if (telemetry.Timestamp < anchor.Timestamp)
            return false;

        double distance =
            GreatCircleNauticalMiles(
                anchor.LatitudeDegrees,
                anchor.LongitudeDegrees,
                telemetry.LatitudeDegrees,
                telemetry.LongitudeDegrees);

        double altitudeDelta =
            Math.Abs(
                telemetry.AltitudeMslFeet
                - anchor.AltitudeMslFeet);

        if (anchor.OnGround)
        {
            if (!telemetry.OnGround)
                return false;

            return distance
                    <= _options.GroundMaximumDistanceNauticalMiles
                && altitudeDelta
                    <= _options.GroundMaximumAltitudeDeltaFeet;
        }

        if (WasAirborne(session))
        {
            double elapsedHours =
                Math.Max(
                    0,
                    (telemetry.Timestamp
                        - anchor.Timestamp)
                    .TotalHours);

            double allowedDistance =
                _options.AirborneBaseDistanceNauticalMiles
                + elapsedHours
                    * _options.AirborneMaximumTravelSpeedKnots;

            return distance <= allowedDistance
                && altitudeDelta
                    <= _options.AirborneMaximumAltitudeDeltaFeet;
        }

        return false;
    }

    public static FlightContinuityAnchor CreateAnchor(
        AircraftTelemetrySnapshot telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        var anchor =
            new FlightContinuityAnchor(
                telemetry.Timestamp,
                telemetry.LatitudeDegrees,
                telemetry.LongitudeDegrees,
                telemetry.AltitudeMslFeet,
                telemetry.OnGround);

        anchor.Validate();
        return anchor;
    }

    private static bool IsGroundPhase(
        FlightSession session) =>
        session.OperationState
            is FlightOperationState.Accepted
                or FlightOperationState.Preparation
                or FlightOperationState.Servicing
                or FlightOperationState.Loading
                or FlightOperationState.ReadyForStart
                or FlightOperationState.EngineStart
                or FlightOperationState.Ramp
                or FlightOperationState.TaxiOut
                or FlightOperationState.DepartureReady;

    private static bool WasAirborne(
        FlightSession session) =>
        session.OperationState
            is FlightOperationState.Airborne
                or FlightOperationState.Landed
                or FlightOperationState.TaxiIn
                or FlightOperationState.Parked
                or FlightOperationState.Unloading
                or FlightOperationState.Shutdown
                or FlightOperationState.Complete
        || session.Tracking.SuspendedFrom
            is FlightTrackingState.Airborne
                or FlightTrackingState.Approach
                or FlightTrackingState.LandingEpisode;

    private static bool HasFinitePosition(
        AircraftTelemetrySnapshot telemetry) =>
        double.IsFinite(telemetry.LatitudeDegrees)
        && telemetry.LatitudeDegrees is >= -90 and <= 90
        && double.IsFinite(telemetry.LongitudeDegrees)
        && telemetry.LongitudeDegrees is >= -180 and <= 180
        && double.IsFinite(telemetry.AltitudeMslFeet)
        && double.IsFinite(telemetry.GroundSpeedKnots);

    internal static double GreatCircleNauticalMiles(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        const double earthRadiusNauticalMiles =
            3_440.065;

        double lat1 =
            DegreesToRadians(latitude1);

        double lat2 =
            DegreesToRadians(latitude2);

        double deltaLat =
            DegreesToRadians(
                latitude2 - latitude1);

        double deltaLon =
            DegreesToRadians(
                longitude2 - longitude1);

        double sinLat =
            Math.Sin(deltaLat / 2);

        double sinLon =
            Math.Sin(deltaLon / 2);

        double a =
            sinLat * sinLat
            + Math.Cos(lat1)
                * Math.Cos(lat2)
                * sinLon
                * sinLon;

        double c =
            2
            * Math.Atan2(
                Math.Sqrt(a),
                Math.Sqrt(
                    Math.Max(0, 1 - a)));

        return earthRadiusNauticalMiles * c;
    }

    private static double DegreesToRadians(
        double degrees) =>
        degrees * Math.PI / 180;
}
