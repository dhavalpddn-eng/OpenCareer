namespace OpenCareer.Domain.Flights;

public sealed record FlightSessionAdvance(
    FlightStateEvidence Evidence,
    FlightTimeInterval? TimeInterval = null,
    bool ShutdownConfirmed = false,
    bool CancelRequested = false,
    FlightContinuityAnchor? ContinuityAnchor = null)
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
