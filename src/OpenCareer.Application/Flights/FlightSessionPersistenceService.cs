using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Flights;

public sealed class FlightSessionPersistenceService
{
    private readonly FlightSessionCoordinator _coordinator;
    private readonly IFlightSessionCheckpointStore _store;
    private readonly FlightSessionCheckpointPolicy _checkpointPolicy;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private FlightSession? _lastPersisted;

    public bool RecoveryAttempted { get; private set; }

    public Guid? LastRecoveredSessionId { get; private set; }

    public FlightSessionPersistenceService(
        FlightSessionCoordinator coordinator,
        IFlightSessionCheckpointStore store,
        FlightSessionCheckpointPolicy? checkpointPolicy = null)
    {
        _coordinator =
            coordinator
            ?? throw new ArgumentNullException(nameof(coordinator));

        _store =
            store
            ?? throw new ArgumentNullException(nameof(store));

        _checkpointPolicy =
            checkpointPolicy
            ?? FlightSessionCheckpointPolicy.Default;

        _checkpointPolicy.Validate();
    }

    public async Task<FlightSession> StartAsync(
        DateTimeOffset timestamp,
        Guid? contractId = null,
        Guid? sessionId = null,
        FlightSessionPlan? plan = null,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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
                    sessionId,
                    plan);

            await _store
                .SaveAsync(session, cancellationToken)
                .ConfigureAwait(false);

            _lastPersisted = session;
            _coordinator.CommitPersisted(session);
            return session;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<FlightSession> AdvanceAsync(
        FlightSessionAdvance update,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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

            if (_checkpointPolicy.ShouldCheckpoint(
                    _lastPersisted,
                    next))
            {
                await _store
                    .SaveAsync(next, cancellationToken)
                    .ConfigureAwait(false);

                _lastPersisted = next;
            }

            _coordinator.CommitPersisted(next);
            return next;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<FlightSession?> RecoverAsync(
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RecoveryAttempted = true;
            LastRecoveredSessionId = null;
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

            if (!checkpoint.IsTerminal
                && checkpoint.Status == FlightSessionStatus.Active)
            {
                checkpoint =
                    FlightSessionEngine.Advance(
                        checkpoint,
                        new FlightSessionAdvance(
                            new FlightStateEvidence(
                                checkpoint.UpdatedAt,
                                Connected: false,
                                ContinuityPlausible: false)));

                await _store
                    .SaveAsync(checkpoint, cancellationToken)
                    .ConfigureAwait(false);
            }

            _lastPersisted = checkpoint;
            LastRecoveredSessionId = checkpoint.SessionId;

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
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task FlushAsync(
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSession? current =
                _coordinator.Current;

            if (current is null)
                return;

            await _store
                .SaveAsync(current, cancellationToken)
                .ConfigureAwait(false);

            _lastPersisted = current;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ClearTerminalAsync(
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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

            _lastPersisted = null;
            LastRecoveredSessionId = null;
            _coordinator.ClearTerminalSession();
        }
        finally
        {
            _operationGate.Release();
        }
    }
}
