using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Application.Simulator;

public interface IFlightCriticalTelemetrySource
{
    IReadOnlyList<AircraftTelemetrySnapshot> ReadAfter(
        DateTimeOffset? exclusiveTimestamp);

    void Clear();
}
