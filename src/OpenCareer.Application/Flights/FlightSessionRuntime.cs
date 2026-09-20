using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Application.Flights;

public sealed class FlightSessionRuntime
{
    private readonly FlightSessionCoordinator _coordinator;
    private readonly FlightSessionPersistenceService _persistence;
    private readonly FlightTelemetryEvidenceProcessor _evidenceProcessor;
    private readonly FlightContinuityPolicy _continuityPolicy;
    private readonly ISimulatorConnection _connection;
    private readonly ISimulatorTelemetrySource _telemetrySource;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private Guid? _processorSessionId;
    private bool _processorWasSuspended;
    private DateTimeOffset? _lastTelemetryTimestamp;

    public FlightSessionRuntime(
        FlightSessionCoordinator coordinator,
        FlightSessionPersistenceService persistence,
        FlightTelemetryEvidenceProcessor evidenceProcessor,
        FlightContinuityPolicy continuityPolicy,
        ISimulatorConnection connection,
        ISimulatorTelemetrySource telemetrySource,
        TimeProvider? timeProvider = null)
    {
        _coordinator =
            coordinator
            ?? throw new ArgumentNullException(nameof(coordinator));

        _persistence =
            persistence
            ?? throw new ArgumentNullException(nameof(persistence));

        _evidenceProcessor =
            evidenceProcessor
            ?? throw new ArgumentNullException(nameof(evidenceProcessor));

        _continuityPolicy =
            continuityPolicy
            ?? throw new ArgumentNullException(nameof(continuityPolicy));

        _connection =
            connection
            ?? throw new ArgumentNullException(nameof(connection));

        _telemetrySource =
            telemetrySource
            ?? throw new ArgumentNullException(nameof(telemetrySource));

        _timeProvider =
            timeProvider
            ?? TimeProvider.System;
    }

    public async Task<bool> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate
                .WaitAsync(
                    millisecondsTimeout: 0,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            FlightSession? current =
                _coordinator.Current;

            if (current is null
                || current.IsTerminal)
            {
                ResetRuntimeContext();
                return false;
            }

            EnsureProcessorContext(current);

            SimulatorConnectionSnapshot connection =
                _connection.Current;

            if (connection.State
                != SimulatorConnectionState.Connected)
            {
                if (current.Status
                    == FlightSessionStatus.Suspended)
                {
                    return false;
                }

                DateTimeOffset timestamp =
                    Max(
                        _timeProvider.GetUtcNow(),
                        current.UpdatedAt);

                await _persistence
                    .AdvanceAsync(
                        new FlightSessionAdvance(
                            new FlightStateEvidence(
                                timestamp,
                                Connected: false,
                                ContinuityPlausible: false)),
                        cancellationToken)
                    .ConfigureAwait(false);

                return true;
            }

            AircraftTelemetrySnapshot? telemetry =
                _telemetrySource.Latest;

            if (telemetry is null)
                return false;

            if (_lastTelemetryTimestamp is { } last
                && telemetry.Timestamp <= last)
            {
                return false;
            }

            if (telemetry.Timestamp < current.UpdatedAt)
                return false;

            bool continuityPlausible =
                _continuityPolicy.IsPlausible(
                    current,
                    telemetry);

            if (current.Status
                    != FlightSessionStatus.Suspended
                && current.ContinuityAnchor is not null
                && !continuityPlausible)
            {
                await _persistence
                    .AdvanceAsync(
                        new FlightSessionAdvance(
                            new FlightStateEvidence(
                                telemetry.Timestamp,
                                Connected: false,
                                ContinuityPlausible: false)),
                        cancellationToken)
                    .ConfigureAwait(false);

                return true;
            }

            FlightStateEvidence evidence =
                _evidenceProcessor.Process(
                    new FlightEvidenceObservation(
                        connection.State,
                        telemetry,
                        ValidLoadedAircraft: true,
                        ContinuityPlausible:
                            continuityPlausible));

            FlightContinuityAnchor? anchor =
                evidence.StableTelemetry
                && continuityPlausible
                && !telemetry.SlewActive
                    ? FlightContinuityPolicy
                        .CreateAnchor(telemetry)
                    : null;

            bool shutdownConfirmed =
                evidence.ParkingConfirmed
                && telemetry.EnginesRunning == 0;

            await _persistence
                .AdvanceAsync(
                    new FlightSessionAdvance(
                        evidence,
                        ShutdownConfirmed:
                            shutdownConfirmed,
                        ContinuityAnchor:
                            anchor),
                    cancellationToken)
                .ConfigureAwait(false);

            _lastTelemetryTimestamp =
                telemetry.Timestamp;

            return true;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void EnsureProcessorContext(
        FlightSession session)
    {
        bool sessionChanged =
            _processorSessionId
            != session.SessionId;

        bool newlySuspended =
            session.Status
                == FlightSessionStatus.Suspended
            && !_processorWasSuspended;

        if (sessionChanged || newlySuspended)
        {
            _evidenceProcessor.RestoreContext(session);
            _processorSessionId = session.SessionId;

            if (sessionChanged)
                _lastTelemetryTimestamp = null;
        }

        _processorWasSuspended =
            session.Status
            == FlightSessionStatus.Suspended;
    }

    private void ResetRuntimeContext()
    {
        _processorSessionId = null;
        _processorWasSuspended = false;
        _lastTelemetryTimestamp = null;
        _evidenceProcessor.Reset();
    }

    private static DateTimeOffset Max(
        DateTimeOffset left,
        DateTimeOffset right) =>
        left >= right
            ? left
            : right;
}
