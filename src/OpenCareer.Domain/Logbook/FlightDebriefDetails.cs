using OpenCareer.Domain.Flights;

namespace OpenCareer.Domain.Logbook;

public sealed record FlightTrackPoint(
    DateTimeOffset Timestamp,
    double LatitudeDegrees,
    double LongitudeDegrees,
    double AltitudeMslFeet)
{
    public void Validate()
    {
        if (!double.IsFinite(LatitudeDegrees) ||
            LatitudeDegrees is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(LatitudeDegrees));
        }

        if (!double.IsFinite(LongitudeDegrees) ||
            LongitudeDegrees is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(LongitudeDegrees));
        }

        if (!double.IsFinite(AltitudeMslFeet))
            throw new ArgumentOutOfRangeException(nameof(AltitudeMslFeet));
    }
}

public sealed record FlightLegDebrief(
    Guid LegId,
    int Sequence,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    FlightRouteDebrief Route,
    FlightTimeLedger Time,
    IReadOnlyList<FlightTrackPoint> RouteTrack,
    IReadOnlyList<int> LandingEpisodeNumbers)
{
    public void Validate(
        DateTimeOffset sessionStartedAt,
        DateTimeOffset sessionEndedAt)
    {
        if (LegId == Guid.Empty)
            throw new ArgumentException("Flight leg id is required.", nameof(LegId));

        if (Sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(Sequence));

        if (EndedAt < StartedAt)
            throw new InvalidOperationException(
                "Flight leg end cannot precede flight leg start.");

        if (StartedAt < sessionStartedAt || EndedAt > sessionEndedAt)
            throw new InvalidOperationException(
                "Flight leg timestamps must fall within the parent session.");

        ArgumentNullException.ThrowIfNull(Route);
        ArgumentNullException.ThrowIfNull(Time);
        ArgumentNullException.ThrowIfNull(RouteTrack);
        ArgumentNullException.ThrowIfNull(LandingEpisodeNumbers);

        Route.Validate();

        DateTimeOffset? previousTimestamp = null;
        foreach (FlightTrackPoint point in RouteTrack)
        {
            ArgumentNullException.ThrowIfNull(point);
            point.Validate();

            if (point.Timestamp < StartedAt || point.Timestamp > EndedAt)
                throw new InvalidOperationException(
                    "Route-track point must fall within the flight leg.");

            if (previousTimestamp is { } previous &&
                point.Timestamp < previous)
            {
                throw new InvalidOperationException(
                    "Route-track points must be ordered by timestamp.");
            }

            previousTimestamp = point.Timestamp;
        }

        if (LandingEpisodeNumbers.Any(static episode => episode <= 0) ||
            LandingEpisodeNumbers.Distinct().Count() != LandingEpisodeNumbers.Count)
        {
            throw new InvalidOperationException(
                "Flight-leg landing episode references must be positive and unique.");
        }
    }
}

public sealed record FlightFuelDebrief(
    double? StartFuelPounds,
    double? EndFuelPounds,
    double? FuelUsedPounds,
    EvidenceQuality EvidenceQuality)
{
    public void Validate()
    {
        ValidateNonNegative(StartFuelPounds, nameof(StartFuelPounds));
        ValidateNonNegative(EndFuelPounds, nameof(EndFuelPounds));
        ValidateNonNegative(FuelUsedPounds, nameof(FuelUsedPounds));
    }

    private static void ValidateNonNegative(double? value, string parameterName)
    {
        if (value is { } number &&
            (!double.IsFinite(number) || number < 0))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public sealed record PayloadDebrief(
    int? PassengerCount,
    double? CargoMassPounds,
    string? CargoDescription,
    string? Outcome,
    EvidenceQuality EvidenceQuality)
{
    public void Validate()
    {
        if (PassengerCount is < 0)
            throw new ArgumentOutOfRangeException(nameof(PassengerCount));

        if (CargoMassPounds is { } mass &&
            (!double.IsFinite(mass) || mass < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(CargoMassPounds));
        }
    }
}

public sealed record FlightAssistanceDebrief(
    bool PauseObserved,
    bool TimeAccelerationObserved,
    bool SlewObserved,
    bool PositionJumpObserved,
    bool RouteEvidenceCompromised);
