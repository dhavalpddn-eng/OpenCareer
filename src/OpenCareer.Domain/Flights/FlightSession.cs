namespace OpenCareer.Domain.Flights;

public sealed record FlightSession(
    Guid SessionId,
    Guid? ContractId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    FlightSessionStatus Status,
    FlightOperationState OperationState,
    FlightTrackingSnapshot Tracking,
    FlightTimeLedger TimeLedger,
    FlightSessionMilestones Milestones,
    int SchemaVersion = 1,
    FlightContinuityAnchor? ContinuityAnchor = null)
{
    public static FlightSession Start(
        DateTimeOffset timestamp,
        Guid? contractId = null,
        Guid? sessionId = null)
    {
        Guid resolvedSessionId =
            sessionId ?? Guid.NewGuid();

        if (resolvedSessionId == Guid.Empty)
            throw new ArgumentException("Flight session ID cannot be empty.", nameof(sessionId));

        return new FlightSession(
            resolvedSessionId,
            contractId,
            timestamp,
            timestamp,
            FlightSessionStatus.Active,
            FlightOperationState.Accepted,
            FlightTrackingSnapshot.Start(timestamp),
            FlightTimeLedger.Empty,
            FlightSessionMilestones.Empty);
    }

    public bool IsTerminal =>
        Status is FlightSessionStatus.Interrupted
            or FlightSessionStatus.Completed
            or FlightSessionStatus.Cancelled;
}
