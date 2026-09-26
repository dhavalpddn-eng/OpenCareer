using Microsoft.Extensions.Logging;
using OpenCareer.Application.Simulator;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.SimConnect;

/// <summary>
/// One connection's optional actuator. Configure/Receive/Tick/Close are worker-only.
/// Enqueue and Snapshot never call native code. The single slot bounds queued + active work.
/// </summary>
internal sealed class SimConnectEngineFailureSession(ISimConnectApi api, nint handle, TimeProvider clock, ILogger logger)
{
    // Official SimConnect event ABI: user object, highest priority, GroupID is a priority.
    internal const uint UserObjectId = 0;
    internal const uint HighestPriority = 1;
    internal const uint GroupIdIsPriority = 0x00000010;
    private readonly HashSet<uint> _setupSendIds = [];
    private PendingCommand? _pending;
    private Observation? _observation;
    private int _configured;
    private int _closed;
    private long _sequence;
    private uint? _commandSendId;
    private bool _unresolvedToggle;

    private sealed record Observation(SimulatorFailureStateSnapshot Snapshot, long ReceivedAt, long Sequence);
    private sealed class PendingCommand(CancellationToken cancellationToken)
    {
        internal CancellationToken CancellationToken { get; } = cancellationToken;
        internal TaskCompletionSource<SimulatorFailureActuationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Sent { get; set; }
        internal long SentAt { get; set; }
        internal long BeforeSendSequence { get; set; }
    }

    internal SimulatorFailureStateSnapshot Snapshot
    {
        get
        {
            var observed = Volatile.Read(ref _observation);
            return Volatile.Read(ref _closed) == 0 && Volatile.Read(ref _configured) != 0 && observed is not null
                && clock.GetElapsedTime(observed.ReceivedAt) <= SimConnectEngineFailureDefinition.MaximumObservationAge
                ? observed.Snapshot : SimulatorFailureStateSnapshot.Unavailable;
        }
    }

    internal void Configure()
    {
        try
        {
            if (!Setup(api.MapClientEventToSimEvent(handle, SimConnectEngineFailureDefinition.EventId,
                    SimConnectEngineFailureDefinition.EventName), "map engine failure event")) return;
            if (!Setup(api.AddToDataDefinition(handle, SimConnectEngineFailureDefinition.DefinitionId,
                    SimConnectEngineFailureDefinition.SimVar, SimConnectEngineFailureDefinition.Units), "define engine failure readback")) return;
            if (!Setup(api.RequestDataOnUserAircraft(handle, SimConnectEngineFailureDefinition.RequestId,
                    SimConnectEngineFailureDefinition.DefinitionId, SimConnectPeriod.Second), "request engine failure readback")) return;
            Volatile.Write(ref _configured, 1);
        }
        catch (EntryPointNotFoundException ex)
        {
            logger.LogWarning(ex, "Optional SimConnect engine failure control is unavailable in this runtime.");
        }
    }

    private bool Setup(int result, string operation)
    {
        if (result >= 0 && api.GetLastSentPacketId(handle, out uint id) >= 0 && id != 0)
        {
            _setupSendIds.Add(id);
            return true;
        }
        logger.LogWarning("Optional SimConnect failure-control setup failed: {Operation}, HRESULT {HResult:X8}.", operation, result);
        return false;
    }

    internal Task<SimulatorFailureActuationResult> Enqueue(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Snapshot.IsAvailable) return Task.FromResult(Result(SimulatorFailureActuationStatus.SimulatorUnavailable));
        var pending = new PendingCommand(token);
        if (Interlocked.CompareExchange(ref _pending, pending, null) is not null)
            return Task.FromResult(Result(SimulatorFailureActuationStatus.Busy));
        // Close can race the caller between its availability check and slot acquisition.
        if (Volatile.Read(ref _closed) != 0) Complete(pending, SimulatorFailureActuationStatus.SimulatorUnavailable);
        return pending.Completion.Task.WaitAsync(token);
    }

    internal bool Receive(SimConnectMessage message)
    {
        if (message.Kind == SimConnectMessageKind.Exception)
        {
            if (_commandSendId == message.SendId)
            {
                logger.LogWarning("SimConnect engine failure command rejected: exception {Code}, send {SendId}, parameter {Index}.",
                    message.ExceptionCode, message.SendId, message.ParameterIndex);
                _unresolvedToggle = false; // Explicit server rejection, not a missing acknowledgement.
                if (Volatile.Read(ref _pending) is { Sent: true } pending)
                    Complete(pending, SimulatorFailureActuationStatus.CommandRejected);
                return true;
            }
            if (_setupSendIds.Contains(message.SendId))
            {
                logger.LogWarning("Optional SimConnect failure control rejected: exception {Code}, send {SendId}, parameter {Index}.",
                    message.ExceptionCode, message.SendId, message.ParameterIndex);
                Volatile.Write(ref _configured, 0);
                Volatile.Write(ref _observation, null);
                if (Volatile.Read(ref _pending) is { } pending) Complete(pending, SimulatorFailureActuationStatus.SimulatorUnavailable);
                return true;
            }
            return false;
        }
        if (message.Kind != SimConnectMessageKind.SimObjectData
            || message.RequestId != SimConnectEngineFailureDefinition.RequestId
            || message.DefinitionId != SimConnectEngineFailureDefinition.DefinitionId) return false;
        if (Volatile.Read(ref _configured) == 0) return true;
        // Float64 is the existing native numeric transport. Only valid Bool encodings are evidence.
        if (message.Data is not [var value] || value is not (0 or 1 or -1))
        {
            Volatile.Write(ref _observation, null);
            return true;
        }
        var observation = new Observation(new(true, value != 0, clock.GetUtcNow()), clock.GetTimestamp(), ++_sequence);
        Volatile.Write(ref _observation, observation);
        if (value != 0) _unresolvedToggle = false;
        return true;
    }

    internal void Tick()
    {
        var pending = Volatile.Read(ref _pending);
        if (pending is null) return;
        if (pending.CancellationToken.IsCancellationRequested)
        {
            pending.Completion.TrySetCanceled(pending.CancellationToken);
            Interlocked.CompareExchange(ref _pending, null, pending);
            return; // Retain the unresolved-toggle guard if transmission already happened.
        }
        var snapshot = Snapshot;
        if (pending.Sent)
        {
            if (snapshot.Engine1Failed == true && _observation!.Sequence > pending.BeforeSendSequence
                && clock.GetElapsedTime(pending.SentAt, _observation.ReceivedAt) <= SimConnectEngineFailureDefinition.AcknowledgementWindow)
                Complete(pending, SimulatorFailureActuationStatus.Applied);
            else if (clock.GetElapsedTime(pending.SentAt) >= SimConnectEngineFailureDefinition.AcknowledgementWindow)
            {
                logger.LogWarning("SimConnect engine 1 failure acknowledgement timed out for send {SendId}; no retransmission.", _commandSendId);
                Complete(pending, SimulatorFailureActuationStatus.AcknowledgementTimeout);
            }
            return;
        }
        if (!snapshot.IsAvailable) { Complete(pending, SimulatorFailureActuationStatus.SimulatorUnavailable); return; }
        if (snapshot.Engine1Failed == true) { Complete(pending, SimulatorFailureActuationStatus.AlreadyFailed); return; }
        if (_unresolvedToggle)
        {
            Complete(pending, SimulatorFailureActuationStatus.AcknowledgementTimeout);
            return; // A delayed toggle could still take effect: a false sample does not prove rejection.
        }

        pending.BeforeSendSequence = _sequence;
        pending.SentAt = clock.GetTimestamp();
        pending.Sent = true;
        _unresolvedToggle = true;
        _commandSendId = null;
        int result;
        try
        {
            result = api.TransmitClientEvent(handle, UserObjectId, SimConnectEngineFailureDefinition.EventId,
                0, HighestPriority, GroupIdIsPriority);
        }
        catch (EntryPointNotFoundException ex)
        {
            Volatile.Write(ref _configured, 0);
            logger.LogWarning(ex, "Optional SimConnect engine failure transmission is unavailable.");
            Complete(pending, SimulatorFailureActuationStatus.SimulatorUnavailable);
            return;
        }
        if (result < 0)
        {
            _unresolvedToggle = false;
            logger.LogWarning("SimConnect engine failure command rejected with HRESULT {HResult:X8}.", result);
            Complete(pending, SimulatorFailureActuationStatus.CommandRejected);
            return;
        }
        if (api.GetLastSentPacketId(handle, out uint sendId) >= 0 && sendId != 0) _commandSendId = sendId;
        else logger.LogWarning("SimConnect engine failure command packet ID unavailable; waiting for readback without retransmission.");
        logger.LogInformation("SimConnect engine 1 failure command sent ({SendId}); awaiting authoritative readback.", _commandSendId);
    }

    internal void Close()
    {
        Volatile.Write(ref _closed, 1);
        Volatile.Write(ref _observation, null);
        if (Interlocked.Exchange(ref _pending, null) is { } pending)
            pending.Completion.TrySetResult(Result(SimulatorFailureActuationStatus.SimulatorUnavailable));
    }

    private SimulatorFailureActuationResult Result(SimulatorFailureActuationStatus status) => new(status, Snapshot);
    private void Complete(PendingCommand pending, SimulatorFailureActuationStatus status)
    {
        Interlocked.CompareExchange(ref _pending, null, pending);
        pending.Completion.TrySetResult(Result(status));
    }
}
