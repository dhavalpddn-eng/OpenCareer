using OpenCareer.Application.Flights;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;
using OpenCareer.SimConnect;

namespace OpenCareer.LiveProbe;

internal sealed record LiveFlightSessionTransition(
    DateTimeOffset Timestamp,
    FlightSessionStatus Status,
    FlightOperationState OperationState,
    FlightTrackingState TrackingState,
    int TakeoffCount,
    int LandingEpisodeCount,
    int BounceCount,
    int TouchAndGoCount,
    string Reason);

internal sealed class LiveFlightSessionValidator
{
    private readonly SimConnectConnection _connection;
    private readonly FlightSessionCoordinator _coordinator;
    private readonly FlightSessionPersistenceService _persistence;
    private readonly FlightSessionRuntime _runtime;
    private readonly FlightSessionCompletionService _completionService;

    private FlightSessionStatus? _lastStatus;
    private FlightOperationState? _lastOperationState;
    private FlightTrackingState? _lastTrackingState;
    private bool _completionAttempted;

    public LiveFlightSessionValidator(
        SimConnectConnection connection)
    {
        _connection =
            connection
            ?? throw new ArgumentNullException(nameof(connection));

        _coordinator = new FlightSessionCoordinator();

        _persistence = new FlightSessionPersistenceService(
            _coordinator,
            new InMemoryFlightSessionCheckpointStore());

        _runtime = new FlightSessionRuntime(
            _coordinator,
            _persistence,
            new FlightTelemetryEvidenceProcessor(),
            new FlightContinuityPolicy(),
            connection,
            connection);

        _completionService = new FlightSessionCompletionService(
            _coordinator,
            _persistence);
    }

    public FlightSession? Current => _coordinator.Current;

    public async Task<IReadOnlyList<LiveFlightSessionTransition>> ObserveAsync(
        AircraftTelemetrySnapshot telemetry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        var transitions =
            new List<LiveFlightSessionTransition>();

        if (_coordinator.Current is null)
        {
            if (!CanStart(telemetry))
                return transitions;

            await _persistence
                .StartAsync(
                    telemetry.Timestamp,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            CaptureIfChanged(
                transitions,
                "probe-start");
        }

        if (_coordinator.Current is { IsTerminal: false })
        {
            await _runtime
                .RefreshAsync(cancellationToken)
                .ConfigureAwait(false);

            CaptureIfChanged(
                transitions,
                "telemetry");
        }

        FlightSession? current =
            _coordinator.Current;

        if (!_completionAttempted
            && current is
            {
                IsTerminal: false,
                Tracking.State: FlightTrackingState.Parked,
                OperationState: FlightOperationState.Shutdown
            })
        {
            _completionAttempted = true;

            await _completionService
                .CompleteAsync(
                    new FlightSessionCompletionRequest(
                        Max(
                            telemetry.Timestamp,
                            current.UpdatedAt),
                        MissionConditionsVerified: true,
                        PostFlightTasksVerified: true),
                    cancellationToken)
                .ConfigureAwait(false);

            CaptureIfChanged(
                transitions,
                "probe-completion-verification");
        }

        return transitions;
    }

    private bool CanStart(
        AircraftTelemetrySnapshot telemetry) =>
        _connection.Current.State
            == SimulatorConnectionState.Connected
        && telemetry.OnGround
        && !telemetry.Paused
        && !telemetry.SlewActive
        && telemetry.GroundSpeedKnots < 3
        && double.IsFinite(telemetry.LatitudeDegrees)
        && double.IsFinite(telemetry.LongitudeDegrees)
        && double.IsFinite(telemetry.AltitudeMslFeet);

    private void CaptureIfChanged(
        ICollection<LiveFlightSessionTransition> transitions,
        string reason)
    {
        FlightSession? current =
            _coordinator.Current;

        if (current is null)
            return;

        if (_lastStatus == current.Status
            && _lastOperationState == current.OperationState
            && _lastTrackingState == current.Tracking.State)
        {
            return;
        }

        transitions.Add(
            new LiveFlightSessionTransition(
                current.UpdatedAt,
                current.Status,
                current.OperationState,
                current.Tracking.State,
                current.Tracking.TakeoffCount,
                current.Tracking.LandingEpisodeCount,
                current.Tracking.BounceCount,
                current.Tracking.TouchAndGoCount,
                reason));

        _lastStatus = current.Status;
        _lastOperationState = current.OperationState;
        _lastTrackingState = current.Tracking.State;
    }

    private static DateTimeOffset Max(
        DateTimeOffset left,
        DateTimeOffset right) =>
        left >= right
            ? left
            : right;
}
