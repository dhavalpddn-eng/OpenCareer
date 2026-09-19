namespace OpenCareer.Domain.Flights;

public sealed record FlightSessionAdvance(
    FlightStateEvidence Evidence,
    FlightTimeInterval? TimeInterval = null,
    bool ShutdownConfirmed = false,
    bool CancelRequested = false)
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
    }
}
