using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Flights;

public interface IFlightAirframeConsequenceStore
{
    Task ApplyAsync(FlightAirframeConsequence consequence, CancellationToken cancellationToken = default);
    Task<FlightAirframeConsequence?> ReadAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
