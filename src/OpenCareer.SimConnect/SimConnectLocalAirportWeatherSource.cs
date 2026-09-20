using OpenCareer.Application.Planning;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Planning;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.SimConnect;

/// <summary>
/// Exposes only weather sampled by MSFS at the user-aircraft position.
/// The MSFS 2024 ambient wind SimVars are documented as user-position values,
/// so this source refuses to claim weather for a remote airport.
/// </summary>
public sealed class SimConnectLocalAirportWeatherSource(
    SimConnectConnection connection,
    TimeProvider? clock = null)
    : IAirportDispatchWeatherSource
{
    private const double EarthRadiusNauticalMiles = 3440.065;
    private const double MetersPerNauticalMile = 1852.0;
    private const double AirportLocalityMarginNauticalMiles = 0.5;
    private static readonly TimeSpan MaximumSampleAge = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<AirportDispatchWeatherObservation?> FindWeatherAsync(
        string icao,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);

        if (connection.Current.State != SimulatorConnectionState.Connected)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        string normalizedIcao = icao.Trim().ToUpperInvariant();
        SimConnectAirportFacilitySnapshot? airport = await connection
            .RequestAirportFacilityAsync(normalizedIcao, cancellationToken)
            .ConfigureAwait(false);

        if (airport is null)
            return null;

        SimConnectLocalWeatherSnapshot? local = connection.LocalWeather;
        if (local is null || !IsFresh(local.Timestamp) || !IsLocalToAirport(local, airport))
            return null;

        RunwayWindObservation[] winds = airport.Runways
            .Select(runway => MapRunwayWind(runway, local))
            .Where(static wind => wind is not null)
            .Cast<RunwayWindObservation>()
            .OrderBy(static wind => wind.RunwayIdentifier, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (winds
            .GroupBy(static wind => wind.RunwayIdentifier, StringComparer.OrdinalIgnoreCase)
            .Any(static group => group.Count() > 1))
        {
            return null;
        }

        var observation = new AirportDispatchWeatherObservation(
            airport.Icao,
            "msfs-simconnect-local-ambient",
            DispatchWeatherAuthority.LocalSimulator,
            local.Timestamp,
            winds,
            local.DensityAltitudeFeet);

        observation.Validate();
        return observation;
    }

    private bool IsFresh(DateTimeOffset observedAt)
    {
        TimeSpan age = _clock.GetUtcNow() - observedAt;
        return age >= TimeSpan.Zero && age <= MaximumSampleAge;
    }

    private static bool IsLocalToAirport(
        SimConnectLocalWeatherSnapshot local,
        SimConnectAirportFacilitySnapshot airport)
    {
        if (IsCoordinateValid(airport.LatitudeDegrees, airport.LongitudeDegrees))
        {
            double radius = AirportLocalityMarginNauticalMiles;

            foreach (SimConnectRunwayFacilityData runway in airport.Runways)
            {
                if (!IsCoordinateValid(
                        runway.CenterLatitudeDegrees,
                        runway.CenterLongitudeDegrees))
                {
                    continue;
                }

                double fromAirport = GreatCircleDistanceNauticalMiles(
                    airport.LatitudeDegrees,
                    airport.LongitudeDegrees,
                    runway.CenterLatitudeDegrees,
                    runway.CenterLongitudeDegrees);

                double halfRunway =
                    float.IsFinite(runway.LengthMeters) && runway.LengthMeters > 0
                        ? runway.LengthMeters / (2.0 * MetersPerNauticalMile)
                        : 0;

                radius = Math.Max(
                    radius,
                    fromAirport + halfRunway + AirportLocalityMarginNauticalMiles);
            }

            return GreatCircleDistanceNauticalMiles(
                    local.LatitudeDegrees,
                    local.LongitudeDegrees,
                    airport.LatitudeDegrees,
                    airport.LongitudeDegrees)
                <= radius;
        }

        foreach (SimConnectRunwayFacilityData runway in airport.Runways)
        {
            if (!IsCoordinateValid(
                    runway.CenterLatitudeDegrees,
                    runway.CenterLongitudeDegrees))
            {
                continue;
            }

            double halfRunway =
                float.IsFinite(runway.LengthMeters) && runway.LengthMeters > 0
                    ? runway.LengthMeters / (2.0 * MetersPerNauticalMile)
                    : 0;

            if (GreatCircleDistanceNauticalMiles(
                    local.LatitudeDegrees,
                    local.LongitudeDegrees,
                    runway.CenterLatitudeDegrees,
                    runway.CenterLongitudeDegrees)
                <= halfRunway + AirportLocalityMarginNauticalMiles)
            {
                return true;
            }
        }

        return false;
    }

    private static RunwayWindObservation? MapRunwayWind(
        SimConnectRunwayFacilityData runway,
        SimConnectLocalWeatherSnapshot weather)
    {
        string? identifier =
            SimConnectAirportDataObservationSource.GetRunwayIdentifier(runway);

        if (identifier is null || !float.IsFinite(runway.HeadingTrueDegrees))
            return null;

        double primaryHeading = NormalizeHeading(runway.HeadingTrueDegrees);
        var candidates = new List<(double Headwind, double Crosswind)>(2);

        string? primaryEnd =
            SimConnectAirportDataObservationSource.FormatRunwayEnd(
                runway.PrimaryNumber,
                runway.PrimaryDesignator);

        if (primaryEnd is not null && !runway.PrimaryClosed)
        {
            candidates.Add(CalculateComponents(
                weather.WindFromTrueDegrees,
                weather.WindVelocityKnots,
                primaryHeading));
        }

        string? secondaryEnd =
            SimConnectAirportDataObservationSource.FormatRunwayEnd(
                runway.SecondaryNumber,
                runway.SecondaryDesignator);

        if (secondaryEnd is not null && !runway.SecondaryClosed)
        {
            candidates.Add(CalculateComponents(
                weather.WindFromTrueDegrees,
                weather.WindVelocityKnots,
                NormalizeHeading(primaryHeading + 180.0)));
        }

        if (candidates.Count == 0)
            return null;

        (double headwind, double crosswind) = candidates
            .OrderByDescending(static candidate => candidate.Headwind)
            .ThenBy(static candidate => candidate.Crosswind)
            .First();

        // MSFS exposes current ambient wind through SimVars but no supported
        // SimConnect gust-observation API. Do not fabricate gust values.
        return new(
            identifier,
            headwind,
            crosswind,
            GustHeadwindKnots: null,
            GustCrosswindKnots: null);
    }

    private static (double Headwind, double Crosswind) CalculateComponents(
        double windFromTrueDegrees,
        double windVelocityKnots,
        double runwayHeadingTrueDegrees)
    {
        double deltaRadians =
            SignedHeadingDifference(windFromTrueDegrees, runwayHeadingTrueDegrees)
            * (Math.PI / 180.0);

        return (
            windVelocityKnots * Math.Cos(deltaRadians),
            Math.Abs(windVelocityKnots * Math.Sin(deltaRadians)));
    }

    private static double SignedHeadingDifference(double first, double second)
    {
        double delta = NormalizeHeading(first) - NormalizeHeading(second);
        if (delta > 180)
            delta -= 360;
        else if (delta < -180)
            delta += 360;

        return delta;
    }

    private static double NormalizeHeading(double heading)
    {
        double normalized = heading % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static bool IsCoordinateValid(double latitude, double longitude) =>
        double.IsFinite(latitude)
        && double.IsFinite(longitude)
        && latitude is >= -90 and <= 90
        && longitude is >= -180 and <= 180;

    private static double GreatCircleDistanceNauticalMiles(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        double lat1 = latitude1 * (Math.PI / 180.0);
        double lat2 = latitude2 * (Math.PI / 180.0);
        double deltaLat = (latitude2 - latitude1) * (Math.PI / 180.0);
        double deltaLon = (longitude2 - longitude1) * (Math.PI / 180.0);

        double a =
            Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
            + Math.Cos(lat1)
            * Math.Cos(lat2)
            * Math.Sin(deltaLon / 2)
            * Math.Sin(deltaLon / 2);

        double centralAngle = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusNauticalMiles * centralAngle;
    }
}
