using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Flights;

public interface IFlightStateEvidenceSource
{
    FlightStateEvidence? Current { get; }

    event Action<FlightStateEvidence?>? EvidenceChanged;
}
