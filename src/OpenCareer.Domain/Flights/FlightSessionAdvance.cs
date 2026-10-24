namespace OpenCareer.Domain.Flights;

public sealed record FlightSessionTouchdownMetrics(
    double VerticalSpeedFeetPerMinute,
    double NormalAccelerationG,
    double IndicatedAirspeedKnots,
    double PitchDegrees,
    double BankDegrees)
{
    public void Validate()
    {
        if (!double.IsFinite(VerticalSpeedFeetPerMinute)
            || !double.IsFinite(NormalAccelerationG)
            || !double.IsFinite(IndicatedAirspeedKnots)
            || !double.IsFinite(PitchDegrees)
            || !double.IsFinite(BankDegrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(FlightSessionTouchdownMetrics),
                "Touchdown metrics must be finite.");
        }

        if (IndicatedAirspeedKnots < 0)
            throw new ArgumentOutOfRangeException(nameof(IndicatedAirspeedKnots));
    }
}

public sealed record FlightSessionAdvance(
    FlightStateEvidence Evidence,
    FlightTimeInterval? TimeInterval = null,
    bool ShutdownConfirmed = false,
    bool CancelRequested = false,
    FlightContinuityAnchor? ContinuityAnchor = null,
    FlightSessionObservation? Observation = null,
    FlightSessionTouchdownMetrics? TouchdownMetrics = null)
{
    public void Validate(DateTimeOffset previousUpdate)
    {
        ArgumentNullException.ThrowIfNull(Evidence);

        if (Evidence.Timestamp < previousUpdate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Evidence),
                "Flight-session evidence cannot move backward in time.");
        }

        if (CancelRequested && ShutdownConfirmed)
        {
            throw new ArgumentException(
                "A flight-session update cannot confirm shutdown and cancellation simultaneously.");
        }

        Observation?.Validate();
        TouchdownMetrics?.Validate();

        if (Observation is not null
            && Observation.Timestamp > Evidence.Timestamp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Observation),
                "A flight-session observation cannot be newer than its evidence.");
        }

        if (ContinuityAnchor is not null)
        {
            ContinuityAnchor.Validate();

            if (ContinuityAnchor.Timestamp > Evidence.Timestamp)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ContinuityAnchor),
                    "A continuity anchor cannot be newer than its flight evidence.");
            }
        }
    }
}
