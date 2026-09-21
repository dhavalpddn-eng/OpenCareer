namespace OpenCareer.Domain.Flights;

public enum GroundProcedureKind
{
    PreflightInspection,
    Fueling,
    Deicing,
    CargoLoading,
    PassengerBoarding,
    DoorClosure,
    EngineStart,
    Pushback,
    RampDeparture,
    TaxiOut,
    HoldShort,
    RunwayEntry,
    RunwayExit,
    TaxiIn,
    ParkingBrake,
    EngineShutdown,
    CargoUnloading,
    PassengerDeplaning,
    TurnaroundComplete
}

public enum ProcedureOutcome
{
    Observed,
    Completed,
    Skipped,
    Deviation,
    NotRequired
}

public enum ProcedureObservationSource
{
    SimulatorTelemetry,
    Inferred,
    UserConfirmed,
    ExternalService
}

public sealed record GroundProcedureEvent(
    DateTimeOffset Timestamp,
    GroundProcedureKind Procedure,
    ProcedureOutcome Outcome,
    ProcedureObservationSource Source,
    double Confidence,
    string? Note = null)
{
    public GroundProcedureEvent Validate()
    {
        if (!double.IsFinite(Confidence) || Confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Confidence));
        }

        return this;
    }
}
