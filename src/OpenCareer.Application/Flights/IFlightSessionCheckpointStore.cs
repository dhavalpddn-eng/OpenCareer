using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Flights;

public interface IFlightSessionCheckpointStore
{
    Task SaveAsync(
        FlightSession session,
        CancellationToken cancellationToken = default);

    Task<FlightSession?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task ClearAsync(
        CancellationToken cancellationToken = default);
}
