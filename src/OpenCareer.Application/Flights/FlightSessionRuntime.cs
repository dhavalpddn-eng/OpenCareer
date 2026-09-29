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
    private readonly ICurrentLoadedAircraftIdentitySource _loadedAircraftIdentitySource;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private Guid? _processorSessionId;
    private bool _processorWasSuspended;
    private DateTimeOffset? _lastTelemetryTimestamp;
    private FlightStateEvidence? _currentEvidence;

    public FlightStateEvidence? Current =>
        Volatile.Read(ref _currentEvidence);

    public event Action<FlightStateEvidence?>? EvidenceChanged;

    public FlightSessionRuntime(
        FlightSessionCoordinator coordinator,
        FlightSessionPersistenceService persistence,
        FlightTelemetryEvidenceProcessor evidenceProcessor,
        FlightContinuityPolicy continuityPolicy,
        ISimulatorConnection connection,
        ISimulatorTelemetrySource telemetrySource,
        TimeProvider? timeProvider = null)
        : this(
            coordinator,
            persistence,
            evidenceProcessor,
            continuityPolicy,
            connection,
            telemetrySource,
            UnavailableLoadedAircraftIdentitySource.Instance,
            timeProvider)
    {
    }

    public FlightSessionRuntime(
        FlightSessionCoordinator coordinator,
        FlightSessionPersistenceService persistence,
        FlightTelemetryEvidenceProcessor evidenceProcessor,
        FlightContinuityPolicy continuityPolicy,
        ISimulatorConnection connection,
        ISimulatorTelemetrySource telemetrySource,
        ICurrentLoadedAircraftIdentitySource loadedAircraftIdentitySource,
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

        _loadedAircraftIdentitySource =
            loadedAircraftIdentitySource
            ?? throw new ArgumentNullException(nameof(loadedAircraftIdentitySource));

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
                ClearFlightCriticalTelemetry();

                if (current.Status
                    == FlightSessionStatus.Suspended)
                {
                    return false;
                }

                DateTimeOffset timestamp =
                    Max(
                        _timeProvider.GetUtcNow(),
                        current.UpdatedAt);

                var disconnectEvidence =
                    new FlightStateEvidence(
                        timestamp,
                        Connected: false,
                        ContinuityPlausible: false);

                await _persistence
                    .AdvanceAsync(
                        new FlightSessionAdvance(disconnectEvidence),
                        cancellationToken)
                    .ConfigureAwait(false);

                Publish(disconnectEvidence);
                return true;
            }

            if (!IsExpectedLoadedAircraft(current))
            {
                return await SuspendForUnverifiedAircraftAsync(
                        current,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            IReadOnlyList<AircraftTelemetrySnapshot> samples =
                ReadUnseenTelemetry();

            bool changed = false;
            foreach (AircraftTelemetrySnapshot telemetry in samples)
            {
                current = _coordinator.Current;
                if (current is null || current.IsTerminal)
                    break;

                bool sampleChanged = await ProcessTelemetryAsync(
                        current,
                        telemetry,
                        connection,
                        cancellationToken)
                    .ConfigureAwait(false);

                changed |= sampleChanged;

                if (current.Status != FlightSessionStatus.Suspended
                    && _coordinator.Current?.Status == FlightSessionStatus.Suspended)
                {
                    break;
                }
            }

            return changed;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private bool IsExpectedLoadedAircraft(
        FlightSession session)
    {
        if (session.ContractId is null)
            return true;

        string? expectedCanonicalAircraftId =
            session.Plan?.ExpectedCanonicalAircraftId;

        if (string.IsNullOrWhiteSpace(expectedCanonicalAircraftId))
            return false;

        CurrentLoadedAircraftIdentitySnapshot loaded =
            _loadedAircraftIdentitySource.Current;

        try
        {
            loaded.Validate();
        }
        catch (ArgumentException)
        {
            return false;
        }

        return loaded.Status
                == CurrentLoadedAircraftIdentityStatus.Identified
            && string.Equals(
                expectedCanonicalAircraftId.Trim(),
                loaded.CanonicalAircraftId?.Trim(),
                StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> SuspendForUnverifiedAircraftAsync(
        FlightSession current,
        CancellationToken cancellationToken)
    {
        ClearFlightCriticalTelemetry();

        if (current.Status == FlightSessionStatus.Suspended)
            return false;

        DateTimeOffset timestamp =
            Max(
                _timeProvider.GetUtcNow(),
                current.UpdatedAt);

        var suspensionEvidence =
            new FlightStateEvidence(
                timestamp,
                Connected: false,
                ContinuityPlausible: false);

        await _persistence
            .AdvanceAsync(
                new FlightSessionAdvance(suspensionEvidence),
                cancellationToken)
            .ConfigureAwait(false);

        Publish(suspensionEvidence);
        return true;
    }

    private IReadOnlyList<AircraftTelemetrySnapshot> ReadUnseenTelemetry()
    {
        if (_telemetrySource is IFlightCriticalTelemetrySource critical)
            return critical.ReadAfter(_lastTelemetryTimestamp);

        return _telemetrySource.Latest is { } latest
            ? [latest]
            : [];
    }

    private async Task<bool> ProcessTelemetryAsync(
        FlightSession current,
        AircraftTelemetrySnapshot telemetry,
        SimulatorConnectionSnapshot connection,
        CancellationToken cancellationToken)
    {
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
            var continuityFailureEvidence =
                new FlightStateEvidence(
                    telemetry.Timestamp,
                    Connected: false,
                    ContinuityPlausible: false);

            await _persistence
                .AdvanceAsync(
                    new FlightSessionAdvance(continuityFailureEvidence),
                    cancellationToken)
                .ConfigureAwait(false);

            _lastTelemetryTimestamp = telemetry.Timestamp;
            Publish(continuityFailureEvidence);
            return true;
        }

        FlightTelemetryEvidenceProcessor.State processorState =
            _evidenceProcessor.CaptureState();
        FlightStateEvidence evidence;

        try
        {
            evidence =
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
        }
        catch
        {
            if (ReferenceEquals(
                    _coordinator.Current,
                    current))
            {
                _evidenceProcessor.RestoreState(processorState);
            }
            else
            {
                _lastTelemetryTimestamp = telemetry.Timestamp;
            }

            throw;
        }

        _lastTelemetryTimestamp =
            telemetry.Timestamp;

        Publish(evidence);
        return true;
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

        if (!double.IsFinite(telemetry.SimulationRate)
            || telemetry.SimulationRate <= 0d)
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
            SimulationRate: telemetry.SimulationRate,
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
            Night: telemetry.IsNight is true,
            ActualInstrument: telemetry.IsInCloud is true);
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

            if (sessionChanged || newlySuspended)
            {
                ClearFlightCriticalTelemetry();
            }

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
        ClearFlightCriticalTelemetry();
        _evidenceProcessor.Reset();
        ClearPublishedEvidence();
    }

    private void ClearFlightCriticalTelemetry()
    {
        if (_telemetrySource is IFlightCriticalTelemetrySource critical)
            critical.Clear();
    }

    private void ClearPublishedEvidence()
    {
        FlightStateEvidence? previous =
            Interlocked.Exchange(
                ref _currentEvidence,
                null);

        if (previous is not null)
            EvidenceChanged?.Invoke(null);
    }

    private void Publish(FlightStateEvidence evidence)
    {
        Volatile.Write(ref _currentEvidence, evidence);
        EvidenceChanged?.Invoke(evidence);
    }

    private static DateTimeOffset Max(
        DateTimeOffset left,
        DateTimeOffset right) =>
        left >= right
            ? left
            : right;

    private sealed class UnavailableLoadedAircraftIdentitySource
        : ICurrentLoadedAircraftIdentitySource
    {
        public static UnavailableLoadedAircraftIdentitySource Instance { get; } =
            new();

        public CurrentLoadedAircraftIdentitySnapshot Current =>
            CurrentLoadedAircraftIdentitySnapshot.Unavailable;
    }
}
