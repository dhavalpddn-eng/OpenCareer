using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Application.Flights;

public sealed class FlightSessionRuntime : IFlightStateEvidenceSource
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
    private FlightStateEvidence? _currentEvidence;

    public FlightStateEvidence? Current =>
        _currentEvidence;

    public event EventHandler<FlightStateEvidenceChangedEventArgs>? EvidenceChanged;

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

                var evidence =
                    new FlightStateEvidence(
                        timestamp,
                        Connected: false,
                        ContinuityPlausible: false);

                await _persistence
                    .AdvanceAsync(
                        new FlightSessionAdvance(
                            evidence),
                        cancellationToken)
                    .ConfigureAwait(false);

                Publish(evidence);
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
                var evidence =
                    new FlightStateEvidence(
                        telemetry.Timestamp,
                        Connected: false,
                        ContinuityPlausible: false);

                await _persistence
                    .AdvanceAsync(
                        new FlightSessionAdvance(
                            evidence),
                        cancellationToken)
                    .ConfigureAwait(false);

                Publish(evidence);
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

            bool trustworthyObservation =
                evidence.StableTelemetry
                && continuityPlausible
                && !telemetry.SlewActive;

            FlightContinuityAnchor? anchor =
                trustworthyObservation
                    ? FlightContinuityPolicy
                        .CreateAnchor(telemetry)
                    : null;

            FlightSessionObservation? observation =
                trustworthyObservation
                    ? new FlightSessionObservation(
                        telemetry.Timestamp,
                        telemetry.LatitudeDegrees,
                        telemetry.LongitudeDegrees,
                        telemetry.AltitudeMslFeet,
                        telemetry.IndicatedAirspeedKnots,
                        telemetry.GroundSpeedKnots,
                        telemetry.FuelTotalPounds,
                        telemetry.PayloadPounds,
                        ShouldCaptureTrackPoint(
                            current,
                            evidence,
                            telemetry.Timestamp))
                    : null;

            FlightTimeInterval? timeInterval =
                CreateTimeInterval(
                    current,
                    telemetry);

            bool shutdownConfirmed =
                evidence.ParkingConfirmed
                && telemetry.EnginesRunning == 0;

            await _persistence
                .AdvanceAsync(
                    new FlightSessionAdvance(
                        evidence,
                        TimeInterval:
                            timeInterval,
                        ShutdownConfirmed:
                            shutdownConfirmed,
                        ContinuityAnchor:
                            anchor,
                        Observation:
                            observation),
                    cancellationToken)
                .ConfigureAwait(false);

            _lastTelemetryTimestamp =
                telemetry.Timestamp;

            Publish(evidence);
            return true;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private bool ShouldCaptureTrackPoint(
        FlightSession session,
        FlightStateEvidence evidence,
        DateTimeOffset timestamp)
    {
        IReadOnlyList<FlightSessionTrackPoint> track =
            session.EffectiveStatistics.RouteTrack;

        if (track.Count == 0)
            return true;

        if (track.Count
            >= FlightSessionStatistics.MaximumTrackPoints)
        {
            return false;
        }

        if (evidence.TakeoffCandidate
            || evidence.AirborneConfirmed
            || evidence.TouchdownConfirmed
            || evidence.ParkingConfirmed)
        {
            return true;
        }

        return timestamp - track[^1].Timestamp
            >= TimeSpan.FromMinutes(5);
    }

    private FlightTimeInterval? CreateTimeInterval(
        FlightSession session,
        AircraftTelemetrySnapshot telemetry)
    {
        if (_lastTelemetryTimestamp is not { } previous
            || session.Status
                != FlightSessionStatus.Active)
        {
            return null;
        }

        TimeSpan wallDuration =
            telemetry.Timestamp - previous;

        if (wallDuration <= TimeSpan.Zero
            || wallDuration > TimeSpan.FromSeconds(30))
        {
            return null;
        }

        bool taxiOut =
            session.OperationState
                is FlightOperationState.TaxiOut
                    or FlightOperationState.DepartureReady;

        bool airborne =
            session.OperationState
                is FlightOperationState.Airborne
                    or FlightOperationState.Landed;

        bool taxiIn =
            session.OperationState
                == FlightOperationState.TaxiIn;

        bool countsTowardFlightTime =
            taxiOut
            || airborne
            || taxiIn;

        bool countsTowardBlockTime =
            session.OperationState
                is FlightOperationState.EngineStart
                    or FlightOperationState.Ramp
                    or FlightOperationState.TaxiOut
                    or FlightOperationState.DepartureReady
                    or FlightOperationState.Airborne
                    or FlightOperationState.Landed
                    or FlightOperationState.TaxiIn
                    or FlightOperationState.Parked;

        return new FlightTimeInterval(
            wallDuration,
            SimulationRate: 1d,
            ValidOperationalEvidence: true,
            Paused: telemetry.Paused,
            SlewActive: telemetry.SlewActive,
            CountsTowardBlockTime:
                countsTowardBlockTime,
            CountsTowardFlightTime:
                countsTowardFlightTime,
            Airborne: airborne,
            TaxiOut: taxiOut,
            TaxiIn: taxiIn,
            Night: false,
            ActualInstrument: false);
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

    private void Publish(
        FlightStateEvidence evidence)
    {
        _currentEvidence = evidence;

        EvidenceChanged?.Invoke(
            this,
            new FlightStateEvidenceChangedEventArgs(
                evidence));
    }

    private static DateTimeOffset Max(
        DateTimeOffset left,
        DateTimeOffset right) =>
        left >= right
            ? left
            : right;
}
