using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Flights;

public sealed class FlightSessionPersistenceService
{
    private readonly FlightSessionCoordinator _coordinator;
    private readonly IFlightSessionCheckpointStore _store;

    public FlightSessionPersistenceService(
        FlightSessionCoordinator coordinator,
        IFlightSessionCheckpointStore store)
    {
        _coordinator =
            coordinator
            ?? throw new ArgumentNullException(nameof(coordinator));

        _store =
            store
            ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<FlightSession> StartAsync(
        DateTimeOffset timestamp,
        Guid? contractId = null,
        Guid? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (_coordinator.Current is { IsTerminal: false })
        {
            throw new InvalidOperationException(
                "An active flight session already exists.");
        }

        FlightSession session =
            FlightSession.Start(
                timestamp,
                contractId,
                sessionId);

        await _store
            .SaveAsync(session, cancellationToken)
            .ConfigureAwait(false);

        _coordinator.CommitPersisted(session);
        return session;
    }

    public async Task<FlightSession> AdvanceAsync(
        FlightSessionAdvance update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        FlightSession current =
            _coordinator.Current
            ?? throw new InvalidOperationException(
                "No flight session is active.");

        FlightSession next =
            FlightSessionEngine.Advance(
                current,
                update);

        await _store
            .SaveAsync(next, cancellationToken)
            .ConfigureAwait(false);

        _coordinator.CommitPersisted(next);
        return next;
    }

    public async Task<FlightSession?> RecoverAsync(
        CancellationToken cancellationToken = default)
    {
        if (_coordinator.Current is { IsTerminal: false })
        {
            throw new InvalidOperationException(
                "Cannot recover a checkpoint over an active flight session.");
        }

        FlightSession? checkpoint =
            await _store
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);

        if (checkpoint is null)
            return null;

        if (_coordinator.Current is null)
        {
            _coordinator.Restore(checkpoint);
        }
        else
        {
            _coordinator.CommitPersisted(checkpoint);
        }

        return checkpoint;
    }

    public async Task ClearTerminalAsync(
        CancellationToken cancellationToken = default)
    {
        FlightSession current =
            _coordinator.Current
            ?? throw new InvalidOperationException(
                "No flight session exists.");

        if (!current.IsTerminal)
        {
            throw new InvalidOperationException(
                "An active flight session cannot be cleared.");
        }

        await _store
            .ClearAsync(cancellationToken)
            .ConfigureAwait(false);

        _coordinator.ClearTerminalSession();
    }
}
