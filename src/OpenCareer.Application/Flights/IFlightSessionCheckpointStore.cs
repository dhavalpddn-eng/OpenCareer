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

    Task ClearAsync(
        Guid expectedSessionId,
        CancellationToken cancellationToken = default)
    {
        if (expectedSessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Session ID is required.",
                nameof(expectedSessionId));
        }

        return ClearAsync(cancellationToken);
    }
}
