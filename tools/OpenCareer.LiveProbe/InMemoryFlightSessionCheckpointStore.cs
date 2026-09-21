using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;

namespace OpenCareer.LiveProbe;

internal sealed class InMemoryFlightSessionCheckpointStore :
    IFlightSessionCheckpointStore
{
    private FlightSession? _session;

    public Task SaveAsync(
        FlightSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        _session = session;
        return Task.CompletedTask;
    }

    public Task<FlightSession?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_session);
    }

    public Task ClearAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _session = null;
        return Task.CompletedTask;
    }
}
