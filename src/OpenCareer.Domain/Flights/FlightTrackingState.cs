namespace OpenCareer.Domain.Flights;

public enum FlightTrackingState
{
    Observing = 0,
    Preflight = 1,
    EngineStart = 2,
    TaxiOut = 3,
    TakeoffRoll = 4,
    Airborne = 5,
    Approach = 6,
    LandingEpisode = 7,
    TaxiIn = 8,
    Parked = 9,
    Suspended = 10,
    Interrupted = 11,
    Complete = 12
}
