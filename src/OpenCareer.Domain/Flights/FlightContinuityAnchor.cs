namespace OpenCareer.Domain.Flights;

public sealed record FlightContinuityAnchor(
    DateTimeOffset Timestamp,
    double LatitudeDegrees,
    double LongitudeDegrees,
    double AltitudeMslFeet,
    bool OnGround)
{
    public void Validate()
    {
        if (!double.IsFinite(LatitudeDegrees)
            || LatitudeDegrees is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LatitudeDegrees));
        }

        if (!double.IsFinite(LongitudeDegrees)
            || LongitudeDegrees is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LongitudeDegrees));
        }

        if (!double.IsFinite(AltitudeMslFeet))
        {
            throw new ArgumentOutOfRangeException(
                nameof(AltitudeMslFeet));
        }
    }
}
