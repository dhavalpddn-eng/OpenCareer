namespace OpenCareer.Application.Simulator;

public enum SimulatorFailureActuationStatus
{
    Applied,
    AlreadyFailed,
    SimulatorUnavailable,
    UnsupportedEngine,
    CommandRejected,
    AcknowledgementTimeout,
    Busy
}

/// <summary>Observed simulator state only; never OpenCareer component health or condition.</summary>
public sealed record SimulatorFailureStateSnapshot(bool IsAvailable, bool? Engine1Failed, DateTimeOffset? ObservedAt)
{
    public static SimulatorFailureStateSnapshot Unavailable { get; } = new(false, null, null);
}

public sealed record SimulatorFailureActuationResult(
    SimulatorFailureActuationStatus Status, SimulatorFailureStateSnapshot Observation);

public interface ISimulatorFailureStateSource
{
    SimulatorFailureStateSnapshot FailureState { get; }
}

/// <summary>
/// Explicit simulator intent, not career authorization. Only engine 1 is supported.
/// Cancellation stops waiting; it cannot undo an already submitted command.
/// No caller may infer application without Applied/AlreadyFailed readback.
/// </summary>
public interface ISimulatorFailureActuator
{
    Task<SimulatorFailureActuationResult> EnsureEngineFailedAsync(int engineIndex, CancellationToken cancellationToken = default);
}
