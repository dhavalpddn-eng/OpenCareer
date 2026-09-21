using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Application.Simulator;

public interface ISimulatorTelemetrySource
{
    AircraftTelemetrySnapshot? Latest { get; }
}
