namespace OpenCareer.Domain.Flights;

public sealed record FlightSessionMilestones(
    DateTimeOffset? AircraftReadyAt = null,
    DateTimeOffset? EngineStartAt = null,
    DateTimeOffset? TaxiOutAt = null,
    DateTimeOffset? TakeoffRollAt = null,
    DateTimeOffset? TakeoffAt = null,
    DateTimeOffset? ApproachAt = null,
    DateTimeOffset? FirstTouchdownAt = null,
    DateTimeOffset? LandingAt = null,
    DateTimeOffset? TaxiInAt = null,
    DateTimeOffset? ParkedAt = null,
    DateTimeOffset? ShutdownAt = null,
    DateTimeOffset? CompletedAt = null,
    DateTimeOffset? InterruptedAt = null)
{
    public static FlightSessionMilestones Empty { get; } = new();
}
