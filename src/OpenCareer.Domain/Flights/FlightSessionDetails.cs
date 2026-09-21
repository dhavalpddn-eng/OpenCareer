namespace OpenCareer.Domain.Flights;

public sealed record FlightSessionPlan(
    string? PlannedOrigin,
    string? PlannedDestination,
    string? PlannedAlternate = null,
    string? PlannedRoute = null,
    string? SourceProvider = null,
    string? SourceReference = null)
{
    public void Validate()
    {
        ValidateText(PlannedOrigin, 32, nameof(PlannedOrigin));
        ValidateText(PlannedDestination, 32, nameof(PlannedDestination));
        ValidateText(PlannedAlternate, 32, nameof(PlannedAlternate));
        ValidateText(PlannedRoute, 8_000, nameof(PlannedRoute));
        ValidateText(SourceProvider, 100, nameof(SourceProvider));
        ValidateText(SourceReference, 500, nameof(SourceReference));
    }

    private static void ValidateText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (value is { Length: > 0 } && value.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

public sealed record FlightSessionTrackPoint(
    DateTimeOffset Timestamp,
    double LatitudeDegrees,
    double LongitudeDegrees,
    double AltitudeMslFeet)
{
    public void Validate()
    {
        if (!double.IsFinite(LatitudeDegrees)
            || LatitudeDegrees is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(LatitudeDegrees));
        }

        if (!double.IsFinite(LongitudeDegrees)
            || LongitudeDegrees is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(LongitudeDegrees));
        }

        if (!double.IsFinite(AltitudeMslFeet))
            throw new ArgumentOutOfRangeException(nameof(AltitudeMslFeet));
    }
}

public sealed record FlightSessionObservation(
    DateTimeOffset Timestamp,
    double LatitudeDegrees,
    double LongitudeDegrees,
    double AltitudeMslFeet,
    double IndicatedAirspeedKnots,
    double GroundSpeedKnots,
    double FuelTotalPounds,
    double PayloadPounds,
    bool CaptureTrackPoint)
{
    public void Validate()
    {
        new FlightSessionTrackPoint(
            Timestamp,
            LatitudeDegrees,
            LongitudeDegrees,
            AltitudeMslFeet)
            .Validate();

        ValidateNonNegativeFinite(
            IndicatedAirspeedKnots,
            nameof(IndicatedAirspeedKnots));

        ValidateNonNegativeFinite(
            GroundSpeedKnots,
            nameof(GroundSpeedKnots));

        ValidateNonNegativeFinite(
            FuelTotalPounds,
            nameof(FuelTotalPounds));

        ValidateNonNegativeFinite(
            PayloadPounds,
            nameof(PayloadPounds));
    }

    private static void ValidateNonNegativeFinite(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

public sealed record FlightSessionStatistics(
    double DistanceNauticalMiles,
    double MaximumAltitudeMslFeet,
    double MaximumIndicatedAirspeedKnots,
    double MaximumGroundSpeedKnots,
    double? StartFuelPounds,
    double? LastFuelPounds,
    double FuelBurnedPounds,
    double FuelAddedPounds,
    double? StartPayloadPounds,
    double? LastPayloadPounds,
    IReadOnlyList<FlightSessionTrackPoint> RouteTrack)
{
    public const int MaximumTrackPoints = 500;

    public static FlightSessionStatistics Empty { get; } =
        new(
            0,
            0,
            0,
            0,
            null,
            null,
            0,
            0,
            null,
            null,
            Array.Empty<FlightSessionTrackPoint>());

    public FlightSessionStatistics Observe(
        FlightSessionObservation observation,
        FlightContinuityAnchor? previousAnchor)
    {
        ArgumentNullException.ThrowIfNull(observation);
        observation.Validate();

        double distanceDelta = 0;

        if (previousAnchor is not null
            && observation.Timestamp >= previousAnchor.Timestamp)
        {
            previousAnchor.Validate();

            distanceDelta =
                GreatCircleNauticalMiles(
                    previousAnchor.LatitudeDegrees,
                    previousAnchor.LongitudeDegrees,
                    observation.LatitudeDegrees,
                    observation.LongitudeDegrees);
        }

        double burned =
            LastFuelPounds is { } previousFuel
                ? Math.Max(0, previousFuel - observation.FuelTotalPounds)
                : 0;

        double added =
            LastFuelPounds is { } priorFuel
                ? Math.Max(0, observation.FuelTotalPounds - priorFuel)
                : 0;

        IReadOnlyList<FlightSessionTrackPoint> track =
            RouteTrack;

        if (observation.CaptureTrackPoint)
        {
            var point =
                new FlightSessionTrackPoint(
                    observation.Timestamp,
                    observation.LatitudeDegrees,
                    observation.LongitudeDegrees,
                    observation.AltitudeMslFeet);

            if (track.Count < MaximumTrackPoints)
            {
                var next =
                    new FlightSessionTrackPoint[track.Count + 1];

                for (int index = 0; index < track.Count; index++)
                    next[index] = track[index];

                next[^1] = point;
                track = next;
            }
        }

        return this with
        {
            DistanceNauticalMiles =
                DistanceNauticalMiles + distanceDelta,
            MaximumAltitudeMslFeet =
                Math.Max(
                    MaximumAltitudeMslFeet,
                    observation.AltitudeMslFeet),
            MaximumIndicatedAirspeedKnots =
                Math.Max(
                    MaximumIndicatedAirspeedKnots,
                    observation.IndicatedAirspeedKnots),
            MaximumGroundSpeedKnots =
                Math.Max(
                    MaximumGroundSpeedKnots,
                    observation.GroundSpeedKnots),
            StartFuelPounds =
                StartFuelPounds
                ?? observation.FuelTotalPounds,
            LastFuelPounds =
                observation.FuelTotalPounds,
            FuelBurnedPounds =
                FuelBurnedPounds + burned,
            FuelAddedPounds =
                FuelAddedPounds + added,
            StartPayloadPounds =
                StartPayloadPounds
                ?? observation.PayloadPounds,
            LastPayloadPounds =
                observation.PayloadPounds,
            RouteTrack =
                track
        };
    }

    private static double GreatCircleNauticalMiles(
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
            DegreesToRadians(latitude2 - latitude1);

        double deltaLon =
            DegreesToRadians(longitude2 - longitude1);

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
                Math.Sqrt(Math.Max(0, 1 - a)));

        return earthRadiusNauticalMiles * c;
    }

    private static double DegreesToRadians(
        double degrees) =>
        degrees * Math.PI / 180;
}

public enum FlightSessionLandingKind
{
    Unknown = 0,
    TouchAndGo = 1,
    FullStop = 2
}

public sealed record FlightSessionLandingEpisode(
    int EpisodeNumber,
    DateTimeOffset TouchdownAt,
    FlightSessionLandingKind Kind,
    int BounceCount,
    DateTimeOffset? CompletedAt = null)
{
    public void Validate()
    {
        if (EpisodeNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(EpisodeNumber));

        if (BounceCount < 0)
            throw new ArgumentOutOfRangeException(nameof(BounceCount));

        if (CompletedAt is { } completed
            && completed < TouchdownAt)
        {
            throw new ArgumentOutOfRangeException(nameof(CompletedAt));
        }
    }
}
