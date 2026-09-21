using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Flights;

public interface IFlightStateEvidenceSource
{
    FlightStateEvidence? Current { get; }

    event EventHandler<FlightStateEvidenceChangedEventArgs>? EvidenceChanged;
}

public sealed class FlightStateEvidenceChangedEventArgs(
    FlightStateEvidence evidence) : EventArgs
{
    public FlightStateEvidence Evidence { get; } =
        evidence
        ?? throw new ArgumentNullException(nameof(evidence));
}
