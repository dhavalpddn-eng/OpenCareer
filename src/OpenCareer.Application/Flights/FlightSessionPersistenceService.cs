using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Flights;

public sealed class FlightSessionPersistenceService
{
    private readonly FlightSessionCoordinator _coordinator;
    private readonly IFlightSessionCheckpointStore _store;
    private readonly FlightSessionCheckpointPolicy _checkpointPolicy;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
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
        await _mutationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

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
            _mutationGate.Release();
        }
    }

    public async Task<FlightSession> AdvanceAsync(
        FlightSessionAdvance update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await _mutationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
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
            _mutationGate.Release();
        }
    }

    public async Task<FlightSession> CompleteAsync(
        Guid expectedSessionId,
        Guid? expectedContractId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default)
    {
        if (expectedSessionId == Guid.Empty)
            throw new ArgumentException("Session ID is required.", nameof(expectedSessionId));

        if (expectedContractId is { } contractId
            && contractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(expectedContractId));

        if (requestedAt == default)
            throw new ArgumentOutOfRangeException(nameof(requestedAt));

        await _mutationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            FlightSession current =
                _coordinator.Current
                ?? throw new InvalidOperationException(
                    "No flight session is active.");

            ValidateExpectedCompletionIdentity(
                current,
                expectedSessionId,
                expectedContractId);

            if (requestedAt < current.CreatedAt)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestedAt),
                    "Completion cannot predate the FlightSession.");
            }

            if (current.IsTerminal)
            {
                if (current.Status == FlightSessionStatus.Completed)
                    return current;

                throw new InvalidOperationException(
                    "Interrupted or cancelled flights cannot be completed.");
            }

            if (current.Tracking.State != FlightTrackingState.Parked)
            {
                throw new InvalidOperationException(
                    "FlightSession must be parked before completion.");
            }

            if (current.OperationState != FlightOperationState.Shutdown)
            {
                throw new InvalidOperationException(
                    "FlightSession must be shut down before completion.");
            }

            DateTimeOffset completedAt =
                requestedAt < current.UpdatedAt
                    ? current.UpdatedAt
                    : requestedAt;

            FlightSession completed =
                FlightSessionEngine.Advance(
                    current,
                    new FlightSessionAdvance(
                        new FlightStateEvidence(
                            completedAt,
                            Connected: true,
                            StableTelemetry: true,
                            ContinuityPlausible: true,
                            OperationCompleteConfirmed: true),
                        ShutdownConfirmed: true));

            await _store
                .SaveAsync(completed, cancellationToken)
                .ConfigureAwait(false);

            _lastPersisted = completed;
            _coordinator.CommitPersisted(completed);
            return completed;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<FlightSession> CompleteCurrentLegAsync(
        Guid expectedSessionId,
        Guid? expectedContractId,
        DateTimeOffset requestedAt,
        FlightLegTerminalEvidence terminalEvidence,
        FlightLegTerminalPolicy terminalPolicy,
        CancellationToken cancellationToken = default)
    {
        if (expectedSessionId == Guid.Empty)
            throw new ArgumentException("Session ID is required.", nameof(expectedSessionId));

        if (expectedContractId is { } contractId && contractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(expectedContractId));

        if (requestedAt == default)
            throw new ArgumentOutOfRangeException(nameof(requestedAt));

        ArgumentNullException.ThrowIfNull(terminalEvidence);
        ArgumentNullException.ThrowIfNull(terminalPolicy);

        await _mutationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            FlightSession current =
                _coordinator.Current
                ?? throw new InvalidOperationException(
                    "No flight session is active.");

            ValidateExpectedCompletionIdentity(
                current,
                expectedSessionId,
                expectedContractId);

            if (current.IsTerminal)
            {
                throw new InvalidOperationException(
                    "A terminal FlightSession cannot complete an intermediate flight leg.");
            }

            if (requestedAt < current.CreatedAt)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestedAt),
                    "Leg completion cannot predate the FlightSession.");
            }

            current.ValidateLegs();
            FlightLeg[] legs = current.EffectiveLegs.ToArray();
            FlightLeg activeLeg = legs[^1];

            if (activeLeg.Status == FlightLegStatus.Completed)
                return current;

            if (!terminalPolicy.IsSatisfied(terminalEvidence))
            {
                throw new InvalidOperationException(
                    "Trusted terminal evidence does not satisfy the flight-leg terminal policy.");
            }

            DateTimeOffset completedAt =
                requestedAt < current.UpdatedAt
                    ? current.UpdatedAt
                    : requestedAt;

            legs[^1] = activeLeg.Complete(completedAt);

            FlightSession completedLeg =
                current with
                {
                    UpdatedAt = completedAt,
                    Legs = legs
                };

            completedLeg.ValidateLegs();

            await _store
                .SaveAsync(completedLeg, cancellationToken)
                .ConfigureAwait(false);

            _lastPersisted = completedLeg;
            _coordinator.CommitPersisted(completedLeg);
            return completedLeg;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<FlightSession> StartNextLegAsync(
        Guid expectedSessionId,
        Guid? expectedContractId,
        Guid nextLegId,
        FlightSessionPlan nextPlan,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default)
    {
        if (expectedSessionId == Guid.Empty)
            throw new ArgumentException("Session ID is required.", nameof(expectedSessionId));

        if (expectedContractId is { } contractId && contractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(expectedContractId));

        ArgumentNullException.ThrowIfNull(nextPlan);

        await _mutationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            FlightSession current =
                _coordinator.Current
                ?? throw new InvalidOperationException(
                    "No flight session is active.");

            ValidateExpectedCompletionIdentity(
                current,
                expectedSessionId,
                expectedContractId);

            FlightSession next =
                FlightSessionEngine.StartNextLeg(
                    current,
                    nextLegId,
                    nextPlan,
                    requestedAt);

            if (ReferenceEquals(next, current))
                return current;

            await _store
                .SaveAsync(next, cancellationToken)
                .ConfigureAwait(false);

            _lastPersisted = next;
            _coordinator.CommitPersisted(next);
            return next;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<FlightSession> CancelAsync(
        Guid expectedSessionId,
        Guid expectedContractId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default)
    {
        if (expectedSessionId == Guid.Empty)
            throw new ArgumentException("Session ID is required.", nameof(expectedSessionId));

        if (expectedContractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(expectedContractId));

        await _mutationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            FlightSession current =
                _coordinator.Current
                ?? throw new InvalidOperationException(
                    "No flight session is active.");

            ValidateExpectedIdentity(
                current,
                expectedSessionId,
                expectedContractId);

            if (current.Status == FlightSessionStatus.Cancelled)
            {
                await _store
                    .SaveAsync(current, cancellationToken)
                    .ConfigureAwait(false);

                _lastPersisted = current;
                return current;
            }

            if (current.IsTerminal)
            {
                throw new InvalidOperationException(
                    "A completed or interrupted FlightSession cannot be abandoned.");
            }

            DateTimeOffset timestamp =
                requestedAt < current.UpdatedAt
                    ? current.UpdatedAt
                    : requestedAt;

            FlightSession cancelled =
                FlightSessionEngine.Advance(
                    current,
                    new FlightSessionAdvance(
                        new FlightStateEvidence(
                            timestamp,
                            Connected: false),
                        CancelRequested: true));

            await _store
                .SaveAsync(cancelled, cancellationToken)
                .ConfigureAwait(false);

            _lastPersisted = cancelled;
            _coordinator.CommitPersisted(cancelled);
            return cancelled;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<FlightSession?> RecoverAsync(
        CancellationToken cancellationToken = default)
    {
        await _mutationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

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
            _mutationGate.Release();
        }
    }

    public async Task FlushAsync(
        CancellationToken cancellationToken = default)
    {
        await _mutationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

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
            _mutationGate.Release();
        }
    }

    public async Task ClearTerminalAsync(
        CancellationToken cancellationToken = default)
    {
        await ClearTerminalCoreAsync(
                expectedSessionId: null,
                expectedContractId: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ClearTerminalAsync(
        Guid expectedSessionId,
        Guid expectedContractId,
        CancellationToken cancellationToken = default)
    {
        if (expectedSessionId == Guid.Empty)
            throw new ArgumentException("Session ID is required.", nameof(expectedSessionId));

        if (expectedContractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(expectedContractId));

        await ClearTerminalCoreAsync(
                expectedSessionId,
                expectedContractId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ClearTerminalCoreAsync(
        Guid? expectedSessionId,
        Guid? expectedContractId,
        CancellationToken cancellationToken)
    {
        await _mutationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            FlightSession current =
                _coordinator.Current
                ?? throw new InvalidOperationException(
                    "No flight session exists.");

            if (expectedSessionId is { } sessionId
                && expectedContractId is { } contractId)
            {
                ValidateExpectedIdentity(
                    current,
                    sessionId,
                    contractId);
            }

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
            _mutationGate.Release();
        }
    }

    private static void ValidateExpectedIdentity(
        FlightSession current,
        Guid expectedSessionId,
        Guid expectedContractId)
    {
        if (current.SessionId != expectedSessionId
            || current.ContractId != expectedContractId)
        {
            throw new InvalidOperationException(
                "Current FlightSession identity does not match the requested career-flight operation.");
        }
    }

    private static void ValidateExpectedCompletionIdentity(
        FlightSession current,
        Guid expectedSessionId,
        Guid? expectedContractId)
    {
        if (current.SessionId != expectedSessionId
            || current.ContractId != expectedContractId)
        {
            throw new InvalidOperationException(
                "Current FlightSession identity does not match the requested completion operation.");
        }
    }
}
