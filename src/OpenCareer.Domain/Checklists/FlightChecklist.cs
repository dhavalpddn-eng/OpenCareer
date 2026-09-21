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

public enum FlightChecklistVerificationCapability
{
    AutoEvidence = 0,
    ManualOnly = 1,
    Unavailable = 2
}

public sealed record FlightChecklistProfileStep(
    FlightChecklistStepId Id,
    FlightChecklistVerificationCapability VerificationCapability)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Id))
            throw new ArgumentOutOfRangeException(nameof(Id));

        if (!Enum.IsDefined(VerificationCapability))
            throw new ArgumentOutOfRangeException(nameof(VerificationCapability));
    }
}

public sealed record FlightChecklistProfile(
    string Id,
    IReadOnlyList<FlightChecklistProfileStep> Steps)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentNullException.ThrowIfNull(Steps);

        if (Steps.Count == 0)
            throw new ArgumentException("Checklist profile must contain at least one step.", nameof(Steps));

        var seen = new HashSet<FlightChecklistStepId>();

        foreach (FlightChecklistProfileStep step in Steps)
        {
            ArgumentNullException.ThrowIfNull(step);
            step.Validate();

            if (!seen.Add(step.Id))
                throw new ArgumentException("Checklist profile steps must be unique.", nameof(Steps));
        }
    }
}

public static class FlightChecklistProfiles
{
    public static FlightChecklistProfile Standard { get; } =
        new(
            "standard",
            [
                Auto(FlightChecklistStepId.AircraftReady),
                Auto(FlightChecklistStepId.EngineStarted),
                Auto(FlightChecklistStepId.TaxiMovementEstablished),
                Auto(FlightChecklistStepId.TakeoffRollEstablished),
                Auto(FlightChecklistStepId.AirborneEstablished),
                Auto(FlightChecklistStepId.ApproachEstablished),
                Auto(FlightChecklistStepId.TouchdownConfirmed),
                Auto(FlightChecklistStepId.LandingRolloutComplete),
                Auto(FlightChecklistStepId.AircraftParked),
                Auto(FlightChecklistStepId.ShutdownConfirmed)
            ]);

    private static FlightChecklistProfileStep Auto(
        FlightChecklistStepId id) =>
        new(
            id,
            FlightChecklistVerificationCapability.AutoEvidence);
}

public sealed record FlightChecklistStepSnapshot(
    FlightChecklistStepId Id,
    FlightChecklistPhase Phase,
    FlightChecklistVerificationCapability VerificationCapability,
    FlightChecklistStepState State,
    DateTimeOffset? VerifiedAt);

public sealed record FlightChecklistSnapshot(
    FlightChecklistPhase CurrentPhase,
    IReadOnlyList<FlightChecklistStepSnapshot> Steps)
{
    public bool IsComplete =>
        CurrentPhase == FlightChecklistPhase.Complete;
}
