using System.Text.Json.Serialization;

namespace OpenCareer.Domain.Flights;

/// <summary>
/// The first trustworthy normalized air-to-ground sample for a confirmed contact.
/// These are observed values, not peak impact loads or an inferred damage classification.
/// Vertical speed retains its signed feet/minute value (negative is descent).
/// </summary>
public sealed record FlightLandingContactEvidence(
    [property: JsonRequired] DateTimeOffset Timestamp,
    [property: JsonRequired] double VerticalSpeedFeetPerMinute,
    [property: JsonRequired] double NormalAccelerationG,
    [property: JsonRequired] double IndicatedAirspeedKnots,
    [property: JsonRequired] double GroundSpeedKnots,
    [property: JsonRequired] double HeadingDegrees,
    [property: JsonRequired] double PitchDegrees,
    [property: JsonRequired] double BankDegrees,
    [property: JsonRequired] double FuelTotalPounds,
    [property: JsonRequired] double PayloadPounds)
{
    public void Validate()
    {
        if (Timestamp == default)
            throw new ArgumentOutOfRangeException(nameof(Timestamp));

        if (!double.IsFinite(VerticalSpeedFeetPerMinute)
            || !double.IsFinite(NormalAccelerationG)
            || !double.IsFinite(HeadingDegrees)
            || !double.IsFinite(PitchDegrees)
            || !double.IsFinite(BankDegrees))
            throw new ArgumentException("Landing contact evidence must contain finite normalized observations.");

        ValidateNonNegative(IndicatedAirspeedKnots, nameof(IndicatedAirspeedKnots));
        ValidateNonNegative(GroundSpeedKnots, nameof(GroundSpeedKnots));
        ValidateNonNegative(FuelTotalPounds, nameof(FuelTotalPounds));
        ValidateNonNegative(PayloadPounds, nameof(PayloadPounds));
    }

    private static void ValidateNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}
