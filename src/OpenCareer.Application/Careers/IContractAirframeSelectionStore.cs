using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Careers;

public interface IContractAirframeSelectionStore
{
    Task SaveAsync(Guid contractId, FlightSessionAircraftIdentity identity,
        CancellationToken cancellationToken = default);
    Task<FlightSessionAircraftIdentity?> ReadAsync(Guid contractId,
        CancellationToken cancellationToken = default);
}
