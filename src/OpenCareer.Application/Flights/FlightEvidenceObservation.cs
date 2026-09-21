using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Application.Flights;

public sealed record FlightEvidenceObservation(
    SimulatorConnectionState ConnectionState,
    AircraftTelemetrySnapshot? Telemetry,
    bool ValidLoadedAircraft,
    bool ContinuityPlausible,
    bool AuthorizedAirborneStart = false,
    bool AuthorizedRunwayStart = false,
    bool CrashReported = false,
    bool OperationCompleteConfirmed = false);
