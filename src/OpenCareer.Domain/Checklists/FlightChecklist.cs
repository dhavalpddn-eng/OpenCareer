namespace OpenCareer.Domain.Checklists;

public enum FlightChecklistPhase
{
    Preflight = 0,
    EngineStart,
    TaxiOut,
    Takeoff,
    Airborne,
    Approach,
    Landing,
    TaxiIn,
    Shutdown,
    Complete
}

public enum FlightChecklistStepId
{
    AircraftReady = 0,
    EngineStarted,
    TaxiMovementEstablished,
    TakeoffRollEstablished,
    AirborneEstablished,
    ApproachEstablished,
    TouchdownConfirmed,
    LandingRolloutComplete,
    AircraftParked,
    ShutdownConfirmed
}

public enum FlightChecklistStepState
{
    Pending = 0,
    Verified = 1
}

public sealed record FlightChecklistStepSnapshot(
    FlightChecklistStepId Id,
    FlightChecklistPhase Phase,
    FlightChecklistStepState State,
    DateTimeOffset? VerifiedAt);

public sealed record FlightChecklistSnapshot(
    FlightChecklistPhase CurrentPhase,
    IReadOnlyList<FlightChecklistStepSnapshot> Steps)
{
    public bool IsComplete =>
        CurrentPhase == FlightChecklistPhase.Complete;
}
