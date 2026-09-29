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
    FlightContinuityAnchor? ContinuityAnchor = null,
    FlightSessionPlan? Plan = null,
    FlightSessionStatistics? Statistics = null,
    IReadOnlyList<FlightSessionLandingEpisode>? LandingEpisodes = null,
    IReadOnlyList<FlightLeg>? Legs = null)
{
    public static FlightSession Start(
        DateTimeOffset timestamp,
        Guid? contractId = null,
        Guid? sessionId = null,
        FlightSessionPlan? plan = null)
    {
        Guid resolvedSessionId =
            sessionId ?? Guid.NewGuid();

        if (resolvedSessionId == Guid.Empty)
            throw new ArgumentException("Flight session ID cannot be empty.", nameof(sessionId));

        plan?.Validate();

        return new FlightSession(
            resolvedSessionId,
            contractId,
            timestamp,
            timestamp,
            FlightSessionStatus.Active,
            FlightOperationState.Accepted,
            FlightTrackingSnapshot.Start(timestamp),
            FlightTimeLedger.Empty,
            FlightSessionMilestones.Empty,
            Plan: plan,
            Statistics: FlightSessionStatistics.Empty,
            LandingEpisodes: Array.Empty<FlightSessionLandingEpisode>(),
            Legs:
            [
                FlightLeg.First(
                    resolvedSessionId,
                    timestamp,
                    plan)
            ]);
    }

    public FlightSessionStatistics EffectiveStatistics =>
        Statistics ?? FlightSessionStatistics.Empty;

    public IReadOnlyList<FlightSessionLandingEpisode> EffectiveLandingEpisodes =>
        LandingEpisodes ?? Array.Empty<FlightSessionLandingEpisode>();

    public IReadOnlyList<FlightLeg> EffectiveLegs =>
        Legs
        ??
        [
            FlightLeg.First(
                SessionId,
                CreatedAt,
                Plan)
        ];

    public FlightSession EnsureInitialLeg() =>
        Legs is not null
            ? this
            : this with
            {
                Legs = EffectiveLegs
            };

    public bool IsTerminal =>
        Status is FlightSessionStatus.Interrupted
            or FlightSessionStatus.Completed
            or FlightSessionStatus.Cancelled;
}
