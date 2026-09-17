namespace OpenCareer.Domain.Flights;

public enum FlightOperationState
{
    Accepted = 0,
    Preparation = 1,
    Servicing = 2,
    Loading = 3,
    ReadyForStart = 4,
    EngineStart = 5,
    Ramp = 6,
    TaxiOut = 7,
    DepartureReady = 8,
    Airborne = 9,
    Landed = 10,
    TaxiIn = 11,
    Parked = 12,
    Unloading = 13,
    Shutdown = 14,
    Complete = 15,
    Failed = 16,
    Cancelled = 17
}
