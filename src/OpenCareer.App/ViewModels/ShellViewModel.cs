using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.App.ViewModels;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly ISimulatorConnection _connection;
    private readonly ISimulatorTelemetrySource _telemetrySource;
    private readonly IAppSettingsService _settings;
    private readonly FlightSessionCoordinator _flightSessions;
    private readonly FlightSessionPersistenceService? _flightPersistence;
    private readonly CareerJobPlayableLoopReadinessSource? _careerReadiness;
    private readonly ICareerJobCompletionAction? _careerCompletionAction;
    private readonly ICareerFlightAbandonAction? _careerAbandonAction;
    private readonly ILogger<ShellViewModel>? _logger;
    private readonly SemaphoreSlim _careerActionGate = new(1, 1);

    private SimulatorConnectionSnapshot? _lastConnectionSnapshot;
    private AircraftTelemetrySnapshot? _lastTelemetry;
    private string _connectionStatus = "Waiting for MSFS 2024";
    private string _connectionDetail = "OpenCareer will connect automatically when the simulator is available.";
    private string _aircraftStatus = "No aircraft connected";
    private string _positionSummary = "—";
    private string _altitudeSummary = "—";
    private string _speedSummary = "—";
    private string _verticalSpeedSummary = "—";
    private string _headingSummary = "—";
    private string _attitudeSummary = "—";
    private string _aircraftStateSummary = "—";
    private string _configurationSummary = "—";
    private string _loadSummary = "—";
    private Guid? _lastFlightSessionId;
    private DateTimeOffset? _lastFlightSessionUpdatedAt;
    private FlightSessionStatus? _lastFlightSessionStatus;
    private FlightOperationState? _lastFlightOperationState;
    private string _currentFlightTitle = "No active flight";
    private string _currentFlightStatus = "NO ACTIVE FLIGHT";
    private string _currentFlightDetail =
        "Accept an operation before OpenCareer creates a career FlightSession.";
    private string _currentFlightRecoveryText = "No saved active flight";
    private string _currentFlightTimeSummary = "—";
    private string _currentFlightEventSummary = "—";
    private string _currentFlightMilestoneSummary = "—";
    private string _currentFlightRouteSummary = "—";
    private string _currentFlightPerformanceSummary = "—";
    private string _currentFlightFuelSummary = "—";
    private string _careerWorkflowStatus = "CAREER WORKFLOW UNAVAILABLE";
    private string _careerWorkflowDetail =
        "Career workflow readiness is not connected to this view.";

    private bool _canCompleteCareerFlight;
    private bool _isCareerCompletionBusy;
    private string _careerCompletionActionText =
        "Complete Career Flight";
    private string _careerCompletionActionDetail =
        "Career completion inputs have not been checked yet.";
    private bool _canAbandonCurrentFlight;
    private bool _isCareerAbandonBusy;
    private string _careerAbandonActionText =
        "Abandon Current Flight";
    private string _careerAbandonActionDetail =
        "Flight abandonment availability has not been checked yet.";
    private Guid? _abandonSessionId;
    private Guid? _abandonContractId;


    public ShellViewModel(
        ISimulatorConnection connection,
        ISimulatorTelemetrySource telemetrySource,
        IAppSettingsService settings)
        : this(
            connection,
            telemetrySource,
            settings,
            new FlightSessionCoordinator(),
            flightPersistence: null)
    {
    }

    public ShellViewModel(
        ISimulatorConnection connection,
        ISimulatorTelemetrySource telemetrySource,
        IAppSettingsService settings,
        FlightSessionCoordinator flightSessions,
        FlightSessionPersistenceService? flightPersistence)
        : this(
            connection,
            telemetrySource,
            settings,
            flightSessions,
            flightPersistence,
            careerReadiness:
                null)
    {
    }

    public ShellViewModel(
        ISimulatorConnection connection,
        ISimulatorTelemetrySource telemetrySource,
        IAppSettingsService settings,
        FlightSessionCoordinator flightSessions,
        FlightSessionPersistenceService? flightPersistence,
        CareerJobPlayableLoopReadinessSource? careerReadiness)
        : this(
            connection,
            telemetrySource,
            settings,
            flightSessions,
            flightPersistence,
            careerReadiness,
            careerCompletionAction:
                null,
            careerAbandonAction:
                null,
            logger:
                null)
    {
    }

    public ShellViewModel(
        ISimulatorConnection connection,
        ISimulatorTelemetrySource telemetrySource,
        IAppSettingsService settings,
        FlightSessionCoordinator flightSessions,
        FlightSessionPersistenceService? flightPersistence,
        CareerJobPlayableLoopReadinessSource? careerReadiness,
        ICareerJobCompletionAction? careerCompletionAction,
        ILogger<ShellViewModel>? logger)
        : this(
            connection,
            telemetrySource,
            settings,
            flightSessions,
            flightPersistence,
            careerReadiness,
            careerCompletionAction,
            careerAbandonAction:
                null,
            logger:
                logger)
    {
    }

    public ShellViewModel(
        ISimulatorConnection connection,
        ISimulatorTelemetrySource telemetrySource,
        IAppSettingsService settings,
        FlightSessionCoordinator flightSessions,
        FlightSessionPersistenceService? flightPersistence,
        CareerJobPlayableLoopReadinessSource? careerReadiness,
        ICareerJobCompletionAction? careerCompletionAction,
        ICareerFlightAbandonAction? careerAbandonAction,
        ILogger<ShellViewModel>? logger)
    {
        _connection =
            connection
            ?? throw new ArgumentNullException(nameof(connection));

        _telemetrySource =
            telemetrySource
            ?? throw new ArgumentNullException(nameof(telemetrySource));

        _settings =
            settings
            ?? throw new ArgumentNullException(nameof(settings));

        _flightSessions =
            flightSessions
            ?? throw new ArgumentNullException(nameof(flightSessions));

        _flightPersistence = flightPersistence;
        _careerReadiness = careerReadiness;
        _careerCompletionAction = careerCompletionAction;
        _careerAbandonAction = careerAbandonAction;
        _logger = logger;
        _settings.Changed += OnSettingsChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ConnectionStatus => _connectionStatus;
    public string ConnectionDetail => _connectionDetail;
    public string AircraftStatus => _aircraftStatus;
    public string PositionSummary => _positionSummary;
    public string AltitudeSummary => _altitudeSummary;
    public string SpeedSummary => _speedSummary;
    public string VerticalSpeedSummary => _verticalSpeedSummary;
    public string HeadingSummary => _headingSummary;
    public string AttitudeSummary => _attitudeSummary;
    public string AircraftStateSummary => _aircraftStateSummary;
    public string ConfigurationSummary => _configurationSummary;
    public string LoadSummary => _loadSummary;
    public string CurrentFlightTitle => _currentFlightTitle;
    public string CurrentFlightStatus => _currentFlightStatus;
    public string CurrentFlightDetail => _currentFlightDetail;
    public string CurrentFlightRecoveryText => _currentFlightRecoveryText;
    public string CurrentFlightTimeSummary => _currentFlightTimeSummary;
    public string CurrentFlightEventSummary => _currentFlightEventSummary;
    public string CurrentFlightMilestoneSummary => _currentFlightMilestoneSummary;
    public string CurrentFlightRouteSummary => _currentFlightRouteSummary;
    public string CurrentFlightPerformanceSummary => _currentFlightPerformanceSummary;
    public string CurrentFlightFuelSummary => _currentFlightFuelSummary;
    public string CareerWorkflowStatus => _careerWorkflowStatus;
    public string CareerWorkflowDetail => _careerWorkflowDetail;
    public bool CanCompleteCareerFlight => _canCompleteCareerFlight;
    public bool IsCareerCompletionBusy => _isCareerCompletionBusy;
    public string CareerCompletionActionText => _careerCompletionActionText;
    public string CareerCompletionActionDetail => _careerCompletionActionDetail;
    public bool CanAbandonCurrentFlight => _canAbandonCurrentFlight;
    public bool IsCareerAbandonBusy => _isCareerAbandonBusy;
    public string CareerAbandonActionText => _careerAbandonActionText;
    public string CareerAbandonActionDetail => _careerAbandonActionDetail;
    private bool _hasFlightSession;
    private bool _hasRecoveredFlightSession;

    public bool HasFlightSession => _hasFlightSession;
    public bool HasRecoveredFlightSession => _hasRecoveredFlightSession;
    public bool IsSimulatorConnected { get; private set; }
    public bool HasTelemetry { get; private set; }

    // The shell calls this on its dispatcher. Native callbacks never touch observable UI state.
    public void RefreshConnectionStatus()
    {
        SimulatorConnectionSnapshot connectionSnapshot = _connection.Current;
        if (connectionSnapshot != _lastConnectionSnapshot)
        {
            _lastConnectionSnapshot = connectionSnapshot;
            RefreshConnection(connectionSnapshot);
        }

        AircraftTelemetrySnapshot? telemetry =
            connectionSnapshot.State == SimulatorConnectionState.Connected
                ? _telemetrySource.Latest
                : null;

        if (telemetry != _lastTelemetry)
        {
            _lastTelemetry = telemetry;
            RefreshTelemetry(telemetry);
        }

        RefreshFlightSession();
        RefreshCareerWorkflow();
    }

    private void RefreshConnection(SimulatorConnectionSnapshot snapshot)
    {
        bool connected = snapshot.State == SimulatorConnectionState.Connected;
        if (IsSimulatorConnected != connected)
        {
            IsSimulatorConnected = connected;
            OnPropertyChanged(nameof(IsSimulatorConnected));
        }

        string status = snapshot.State switch
        {
            SimulatorConnectionState.WaitingForSimulator => "Waiting for MSFS 2024",
            SimulatorConnectionState.Connecting => "Connecting to simulator",
            SimulatorConnectionState.Connected => "Simulator connected",
            SimulatorConnectionState.Reconnecting => "Reconnecting to simulator",
            SimulatorConnectionState.Unavailable => "Simulator connection unavailable",
            SimulatorConnectionState.Faulted => "Connection stopped",
            _ => "Disconnected"
        };

        string detail = snapshot.Issue switch
        {
            SimulatorConnectionIssue.RuntimeMissing => "The simulator connection component is missing from this OpenCareer build.",
            SimulatorConnectionIssue.RuntimeIncompatible => "The simulator connection component is incompatible with this OpenCareer build.",
            SimulatorConnectionIssue.VersionMismatch => "The simulator and its connection component use incompatible versions.",
            SimulatorConnectionIssue.UnsupportedPlatform => "Simulator connection requires Windows x64.",
            SimulatorConnectionIssue.UnexpectedError => "A connection error occurred. Restart OpenCareer to try again.",
            SimulatorConnectionIssue.HandshakeTimeout => "The simulator did not acknowledge the connection. OpenCareer will try again.",
            SimulatorConnectionIssue.ResponseTimeout => "The simulator stopped responding. OpenCareer will reconnect automatically.",
            SimulatorConnectionIssue.InvalidResponse or SimulatorConnectionIssue.SimulatorError => "The simulator reported a connection error. OpenCareer will try again.",
            _ => snapshot.State switch
            {
                SimulatorConnectionState.Connected => "Connection established. Live aircraft telemetry will appear when MSFS begins sending data.",
                SimulatorConnectionState.Connecting => "Waiting for the simulator to acknowledge the connection.",
                SimulatorConnectionState.Reconnecting => "The simulator connection was lost. OpenCareer will reconnect automatically.",
                SimulatorConnectionState.Disconnected => "Simulator connection is stopped.",
                _ => "OpenCareer will connect automatically when MSFS 2024 is available."
            }
        };

        SetField(ref _connectionStatus, status, nameof(ConnectionStatus));
        SetField(ref _connectionDetail, detail, nameof(ConnectionDetail));

        if (!connected)
            SetField(ref _aircraftStatus, "No aircraft connected", nameof(AircraftStatus));
        else if (!HasTelemetry)
            SetField(ref _aircraftStatus, "Waiting for aircraft telemetry", nameof(AircraftStatus));
    }

    private void RefreshTelemetry(AircraftTelemetrySnapshot? telemetry)
    {
        bool hasTelemetry = telemetry is not null;
        if (HasTelemetry != hasTelemetry)
        {
            HasTelemetry = hasTelemetry;
            OnPropertyChanged(nameof(HasTelemetry));
        }

        if (telemetry is null)
        {
            SetField(
                ref _aircraftStatus,
                IsSimulatorConnected ? "Waiting for aircraft telemetry" : "No aircraft connected",
                nameof(AircraftStatus));
            SetField(ref _positionSummary, "—", nameof(PositionSummary));
            SetField(ref _altitudeSummary, "—", nameof(AltitudeSummary));
            SetField(ref _speedSummary, "—", nameof(SpeedSummary));
            SetField(ref _verticalSpeedSummary, "—", nameof(VerticalSpeedSummary));
            SetField(ref _headingSummary, "—", nameof(HeadingSummary));
            SetField(ref _attitudeSummary, "—", nameof(AttitudeSummary));
            SetField(ref _aircraftStateSummary, "—", nameof(AircraftStateSummary));
            SetField(ref _configurationSummary, "—", nameof(ConfigurationSummary));
            SetField(ref _loadSummary, "—", nameof(LoadSummary));
            return;
        }

        SetField(ref _aircraftStatus, "Live aircraft telemetry", nameof(AircraftStatus));
        SetField(
            ref _positionSummary,
            FormattableString.Invariant(
                $"{telemetry.LatitudeDegrees:0.00000}°, {telemetry.LongitudeDegrees:0.00000}°"),
            nameof(PositionSummary));

        if (_settings.Current.MeasurementSystem == MeasurementSystem.Metric)
        {
            SetField(
                ref _altitudeSummary,
                FormattableString.Invariant(
                    $"{FeetToMeters(telemetry.AltitudeMslFeet):0} m MSL / {FeetToMeters(telemetry.AltitudeAglFeet):0} m AGL"),
                nameof(AltitudeSummary));
            SetField(
                ref _speedSummary,
                FormattableString.Invariant(
                    $"{KnotsToKilometersPerHour(telemetry.IndicatedAirspeedKnots):0} km/h IAS / {KnotsToKilometersPerHour(telemetry.GroundSpeedKnots):0} km/h GS"),
                nameof(SpeedSummary));
            SetField(
                ref _verticalSpeedSummary,
                FormattableString.Invariant(
                    $"{FeetPerMinuteToMetersPerSecond(telemetry.VerticalSpeedFeetPerMinute):+0.0;-0.0;0.0} m/s"),
                nameof(VerticalSpeedSummary));
            SetField(
                ref _loadSummary,
                FormattableString.Invariant(
                    $"{PoundsToKilograms(telemetry.FuelTotalPounds):0} kg fuel / {PoundsToKilograms(telemetry.PayloadPounds):0} kg payload"),
                nameof(LoadSummary));
        }
        else
        {
            SetField(
                ref _altitudeSummary,
                FormattableString.Invariant(
                    $"{telemetry.AltitudeMslFeet:0} ft MSL / {telemetry.AltitudeAglFeet:0} ft AGL"),
                nameof(AltitudeSummary));
            SetField(
                ref _speedSummary,
                FormattableString.Invariant(
                    $"{telemetry.IndicatedAirspeedKnots:0} kt IAS / {telemetry.GroundSpeedKnots:0} kt GS"),
                nameof(SpeedSummary));
            SetField(
                ref _verticalSpeedSummary,
                FormattableString.Invariant(
                    $"{telemetry.VerticalSpeedFeetPerMinute:+0;-0;0} ft/min"),
                nameof(VerticalSpeedSummary));
            SetField(
                ref _loadSummary,
                FormattableString.Invariant(
                    $"{telemetry.FuelTotalPounds:0} lb fuel / {telemetry.PayloadPounds:0} lb payload"),
                nameof(LoadSummary));
        }

        SetField(
            ref _headingSummary,
            FormattableString.Invariant($"{telemetry.HeadingDegrees:000}° true"),
            nameof(HeadingSummary));
        SetField(
            ref _attitudeSummary,
            FormattableString.Invariant(
                $"{telemetry.PitchDegrees:+0.0;-0.0;0.0}° pitch / {telemetry.BankDegrees:+0.0;-0.0;0.0}° bank / {telemetry.NormalAccelerationG:0.00} G"),
            nameof(AttitudeSummary));

        string motionState = telemetry.SlewActive
            ? "Slew active"
            : telemetry.Paused
                ? "Paused"
                : telemetry.OnGround ? "On ground" : "Airborne";
        SetField(ref _aircraftStateSummary, motionState, nameof(AircraftStateSummary));

        string gear = telemetry.GearDown ? "Gear down" : "Gear up";
        SetField(
            ref _configurationSummary,
            FormattableString.Invariant(
                $"{gear} / Flaps {telemetry.FlapsPositionPercent:0}% / {telemetry.EnginesRunning} engine(s) running"),
            nameof(ConfigurationSummary));
    }

    public async Task RefreshCareerCompletionActionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_careerCompletionAction is null)
        {
            SetBoolean(
                ref _canCompleteCareerFlight,
                false,
                nameof(CanCompleteCareerFlight));

            SetField(
                ref _careerCompletionActionDetail,
                "Career completion action is not connected to this view.",
                nameof(CareerCompletionActionDetail));

            return;
        }

        bool entered =
            await _careerActionGate
                .WaitAsync(
                    millisecondsTimeout:
                        0,
                    cancellationToken);

        if (!entered)
            return;

        try
        {
            CareerJobCompletionActionAvailability availability =
                await _careerCompletionAction
                    .ReadAvailabilityAsync(
                        cancellationToken);

            SetBoolean(
                ref _canCompleteCareerFlight,
                availability.CanComplete
                    && !_isCareerCompletionBusy,
                nameof(CanCompleteCareerFlight));

            SetField(
                ref _careerCompletionActionDetail,
                availability.Detail,
                nameof(CareerCompletionActionDetail));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Career completion readiness refresh failed.");

            SetBoolean(
                ref _canCompleteCareerFlight,
                false,
                nameof(CanCompleteCareerFlight));

            SetField(
                ref _careerCompletionActionDetail,
                "Career completion readiness could not be verified.",
                nameof(CareerCompletionActionDetail));
        }
        finally
        {
            _careerActionGate.Release();
        }
    }

    public async Task CompleteCareerFlightAsync(
        CancellationToken cancellationToken = default)
    {
        if (_careerCompletionAction is null)
            return;

        await _careerActionGate
            .WaitAsync(cancellationToken);

        try
        {
            SetBoolean(
                ref _isCareerCompletionBusy,
                true,
                nameof(IsCareerCompletionBusy));

            SetBoolean(
                ref _canCompleteCareerFlight,
                false,
                nameof(CanCompleteCareerFlight));

            SetBoolean(
                ref _canAbandonCurrentFlight,
                false,
                nameof(CanAbandonCurrentFlight));

            SetField(
                ref _careerCompletionActionText,
                "Completing Career Flight…",
                nameof(CareerCompletionActionText));

            SetField(
                ref _careerCompletionActionDetail,
                "Applying authoritative completion, settlement, logbook, career progression, and cleanup.",
                nameof(CareerCompletionActionDetail));

            await _careerCompletionAction
                .CompleteAsync(cancellationToken);

            _abandonSessionId = null;
            _abandonContractId = null;

            RefreshConnectionStatus();

            SetField(
                ref _careerCompletionActionDetail,
                "Career flight completed. Settlement, logbook, experience, Fleet release, and terminal cleanup finished.",
                nameof(CareerCompletionActionDetail));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Career flight completion failed.");

            SetField(
                ref _careerCompletionActionDetail,
                $"Career flight completion failed: {ex.Message}",
                nameof(CareerCompletionActionDetail));

            try
            {
                CareerJobCompletionActionAvailability availability =
                    await _careerCompletionAction
                        .ReadAvailabilityAsync(
                            cancellationToken);

                SetBoolean(
                    ref _canCompleteCareerFlight,
                    availability.CanComplete,
                    nameof(CanCompleteCareerFlight));
            }
            catch (Exception refreshEx)
            {
                _logger?.LogWarning(
                    refreshEx,
                    "Career completion readiness could not be refreshed after a failed completion attempt.");
            }
        }
        finally
        {
            SetBoolean(
                ref _isCareerCompletionBusy,
                false,
                nameof(IsCareerCompletionBusy));

            SetField(
                ref _careerCompletionActionText,
                "Complete Career Flight",
                nameof(CareerCompletionActionText));

            _careerActionGate.Release();
        }

    }

    public async Task RefreshCareerAbandonActionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_careerAbandonAction is null)
        {
            SetAbandonAvailability(
                canAbandon: false,
                sessionId: null,
                contractId: null,
                detail:
                    "Flight abandonment is not connected to this view.");
            return;
        }

        bool entered =
            await _careerActionGate
                .WaitAsync(
                    millisecondsTimeout:
                        0,
                    cancellationToken);

        if (!entered)
            return;

        try
        {
            CareerFlightAbandonAvailability availability =
                await _careerAbandonAction
                    .ReadAvailabilityAsync(
                        cancellationToken);

            SetAbandonAvailability(
                availability.CanAbandon
                    && !_isCareerAbandonBusy,
                availability.SessionId,
                availability.ContractId,
                availability.Detail);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Career flight abandonment readiness refresh failed.");

            SetAbandonAvailability(
                canAbandon: false,
                sessionId: null,
                contractId: null,
                detail:
                    "Flight abandonment readiness could not be verified.");
        }
        finally
        {
            _careerActionGate.Release();
        }
    }

    public async Task<bool> AbandonCurrentFlightAsync(
        CancellationToken cancellationToken = default)
    {
        if (_careerAbandonAction is null
            || _abandonSessionId is not { } sessionId
            || _abandonContractId is not { } contractId)
        {
            return false;
        }

        await _careerActionGate
            .WaitAsync(cancellationToken);

        try
        {
            SetBoolean(
                ref _isCareerAbandonBusy,
                true,
                nameof(IsCareerAbandonBusy));

            SetBoolean(
                ref _canAbandonCurrentFlight,
                false,
                nameof(CanAbandonCurrentFlight));

            SetBoolean(
                ref _canCompleteCareerFlight,
                false,
                nameof(CanCompleteCareerFlight));

            SetField(
                ref _careerAbandonActionText,
                "Abandoning Current Flight…",
                nameof(CareerAbandonActionText));

            SetField(
                ref _careerAbandonActionDetail,
                "Persisting cancellation, cancelling the contract, releasing its Fleet reservation, and cleaning up the saved session.",
                nameof(CareerAbandonActionDetail));

            CareerFlightAbandonResult result =
                await _careerAbandonAction
                    .AbandonAsync(
                        sessionId,
                        contractId,
                        cancellationToken);

            _abandonSessionId = null;
            _abandonContractId = null;

            RefreshConnectionStatus();

            SetField(
                ref _careerAbandonActionDetail,
                result.Status == CareerFlightAbandonStatus.NoActiveSession
                    ? "No current FlightSession remained; no abandonment changes were made."
                    : "Current flight abandoned. The contract was cancelled and its aircraft reservation released; no completion rewards were applied.",
                nameof(CareerAbandonActionDetail));

            return true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Career flight abandonment failed for session {SessionId} and contract {ContractId}.",
                sessionId,
                contractId);

            SetField(
                ref _careerAbandonActionDetail,
                $"Flight abandonment failed: {ex.Message}",
                nameof(CareerAbandonActionDetail));

            try
            {
                CareerFlightAbandonAvailability availability =
                    await _careerAbandonAction
                        .ReadAvailabilityAsync(
                            cancellationToken);

                SetAbandonAvailability(
                    availability.CanAbandon,
                    availability.SessionId,
                    availability.ContractId,
                    _careerAbandonActionDetail);
            }
            catch (Exception refreshEx)
            {
                _logger?.LogWarning(
                    refreshEx,
                    "Flight abandonment readiness could not be refreshed after a failed attempt.");
            }

            return false;
        }
        finally
        {
            SetBoolean(
                ref _isCareerAbandonBusy,
                false,
                nameof(IsCareerAbandonBusy));

            SetField(
                ref _careerAbandonActionText,
                "Abandon Current Flight",
                nameof(CareerAbandonActionText));

            _careerActionGate.Release();
        }
    }

    private void SetAbandonAvailability(
        bool canAbandon,
        Guid? sessionId,
        Guid? contractId,
        string detail)
    {
        _abandonSessionId =
            canAbandon
                ? sessionId
                : null;
        _abandonContractId =
            canAbandon
                ? contractId
                : null;

        SetBoolean(
            ref _canAbandonCurrentFlight,
            canAbandon,
            nameof(CanAbandonCurrentFlight));

        SetField(
            ref _careerAbandonActionDetail,
            detail,
            nameof(CareerAbandonActionDetail));
    }

    public void ReportCareerAbandonProjectionRefreshFailure(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        _logger?.LogWarning(
            exception,
            "Career flight was abandoned, but the Jobs projection refresh failed.");

        SetField(
            ref _careerAbandonActionDetail,
            "Current flight was abandoned successfully, but the Jobs view could not refresh. Reopen Jobs to retry its authoritative refresh.",
            nameof(CareerAbandonActionDetail));
    }

    private void RefreshCareerWorkflow()
    {
        if (_careerReadiness is null)
        {
            SetField(
                ref _careerWorkflowStatus,
                "CAREER WORKFLOW UNAVAILABLE",
                nameof(CareerWorkflowStatus));

            SetField(
                ref _careerWorkflowDetail,
                "Career workflow readiness is not connected to this view.",
                nameof(CareerWorkflowDetail));

            return;
        }

        CareerJobPlayableReadinessSnapshot snapshot =
            _careerReadiness.Current;

        SetField(
            ref _careerWorkflowStatus,
            snapshot.StatusText,
            nameof(CareerWorkflowStatus));

        SetField(
            ref _careerWorkflowDetail,
            snapshot.Detail,
            nameof(CareerWorkflowDetail));
    }

    private void RefreshFlightSession()
    {
        FlightSession? session =
            _flightSessions.Current;

        if (session is null)
        {
            _lastFlightSessionId = null;
            _lastFlightSessionUpdatedAt = null;
            _lastFlightSessionStatus = null;
            _lastFlightOperationState = null;

            SetBoolean(
                ref _hasFlightSession,
                false,
                nameof(HasFlightSession));

            SetBoolean(
                ref _hasRecoveredFlightSession,
                false,
                nameof(HasRecoveredFlightSession));

            SetField(
                ref _currentFlightTitle,
                "No active flight",
                nameof(CurrentFlightTitle));

            SetField(
                ref _currentFlightStatus,
                "NO ACTIVE FLIGHT",
                nameof(CurrentFlightStatus));

            SetField(
                ref _currentFlightDetail,
                "Accept an operation before OpenCareer creates a career FlightSession.",
                nameof(CurrentFlightDetail));

            SetField(
                ref _currentFlightRecoveryText,
                "No saved active flight",
                nameof(CurrentFlightRecoveryText));

            SetField(
                ref _currentFlightTimeSummary,
                "—",
                nameof(CurrentFlightTimeSummary));

            SetField(
                ref _currentFlightEventSummary,
                "—",
                nameof(CurrentFlightEventSummary));

            SetField(
                ref _currentFlightMilestoneSummary,
                "—",
                nameof(CurrentFlightMilestoneSummary));

            SetField(
                ref _currentFlightRouteSummary,
                "—",
                nameof(CurrentFlightRouteSummary));

            SetField(
                ref _currentFlightPerformanceSummary,
                "—",
                nameof(CurrentFlightPerformanceSummary));

            SetField(
                ref _currentFlightFuelSummary,
                "—",
                nameof(CurrentFlightFuelSummary));

            return;
        }

        bool changed =
            session.SessionId != _lastFlightSessionId
            || session.UpdatedAt != _lastFlightSessionUpdatedAt
            || session.Status != _lastFlightSessionStatus
            || session.OperationState != _lastFlightOperationState;

        bool recovered =
            _flightPersistence?.LastRecoveredSessionId
            == session.SessionId;

        SetBoolean(
            ref _hasFlightSession,
            true,
            nameof(HasFlightSession));

        SetBoolean(
            ref _hasRecoveredFlightSession,
            recovered,
            nameof(HasRecoveredFlightSession));

        if (!changed)
        {
            string recoveryText =
                recovered
                    ? "RESTORED FROM LOCAL SAVE"
                    : "LIVE SESSION";

            SetField(
                ref _currentFlightRecoveryText,
                recoveryText,
                nameof(CurrentFlightRecoveryText));

            return;
        }

        _lastFlightSessionId = session.SessionId;
        _lastFlightSessionUpdatedAt = session.UpdatedAt;
        _lastFlightSessionStatus = session.Status;
        _lastFlightOperationState = session.OperationState;

        SetField(
            ref _currentFlightTitle,
            FlightTitle(session.Status),
            nameof(CurrentFlightTitle));

        SetField(
            ref _currentFlightStatus,
            $"{session.Status.ToString().ToUpperInvariant()} · {FormatOperationState(session.OperationState)}",
            nameof(CurrentFlightStatus));

        SetField(
            ref _currentFlightDetail,
            FlightDetail(session),
            nameof(CurrentFlightDetail));

        SetField(
            ref _currentFlightRecoveryText,
            recovered
                ? "RESTORED FROM LOCAL SAVE"
                : "LIVE SESSION",
            nameof(CurrentFlightRecoveryText));

        SetField(
            ref _currentFlightTimeSummary,
            $"Career {FormatDuration(session.TimeLedger.CareerCreditTime)} · Airborne {FormatDuration(session.TimeLedger.AirborneTime)} · Block {FormatDuration(session.TimeLedger.BlockTime)}",
            nameof(CurrentFlightTimeSummary));

        SetField(
            ref _currentFlightEventSummary,
            $"TO {session.Tracking.TakeoffCount} · Landing episodes {session.Tracking.LandingEpisodeCount} · Bounces {session.Tracking.BounceCount} · T&G {session.Tracking.TouchAndGoCount} · RTO {session.Tracking.RejectedTakeoffCount}",
            nameof(CurrentFlightEventSummary));

        SetField(
            ref _currentFlightMilestoneSummary,
            LatestMilestone(session.Milestones),
            nameof(CurrentFlightMilestoneSummary));

        SetField(
            ref _currentFlightRouteSummary,
            FormatRouteSummary(session),
            nameof(CurrentFlightRouteSummary));

        SetField(
            ref _currentFlightPerformanceSummary,
            FormatPerformanceSummary(session.EffectiveStatistics),
            nameof(CurrentFlightPerformanceSummary));

        SetField(
            ref _currentFlightFuelSummary,
            FormatFuelSummary(session.EffectiveStatistics),
            nameof(CurrentFlightFuelSummary));
    }

    private string FormatRouteSummary(
        FlightSession session)
    {
        string origin =
            session.Plan?.PlannedOrigin
            ?? "—";

        string destination =
            session.Plan?.PlannedDestination
            ?? "—";

        double distance =
            session.EffectiveStatistics.DistanceNauticalMiles;

        return $"{origin} → {destination} · {distance:0.0} nm tracked";
    }

    private string FormatPerformanceSummary(
        FlightSessionStatistics statistics)
    {
        if (_settings.Current.MeasurementSystem
            == MeasurementSystem.Metric)
        {
            return FormattableString.Invariant(
                $"Max {FeetToMeters(statistics.MaximumAltitudeMslFeet):0} m · {KnotsToKilometersPerHour(statistics.MaximumIndicatedAirspeedKnots):0} km/h IAS · {KnotsToKilometersPerHour(statistics.MaximumGroundSpeedKnots):0} km/h GS");
        }

        return FormattableString.Invariant(
            $"Max {statistics.MaximumAltitudeMslFeet:0} ft · {statistics.MaximumIndicatedAirspeedKnots:0} kt IAS · {statistics.MaximumGroundSpeedKnots:0} kt GS");
    }

    private string FormatFuelSummary(
        FlightSessionStatistics statistics)
    {
        if (statistics.StartFuelPounds is null)
            return "Fuel summary unavailable";

        if (_settings.Current.MeasurementSystem
            == MeasurementSystem.Metric)
        {
            return FormattableString.Invariant(
                $"Burned {PoundsToKilograms(statistics.FuelBurnedPounds):0.0} kg · Added {PoundsToKilograms(statistics.FuelAddedPounds):0.0} kg · Remaining {PoundsToKilograms(statistics.LastFuelPounds ?? 0):0.0} kg");
        }

        return FormattableString.Invariant(
            $"Burned {statistics.FuelBurnedPounds:0.0} lb · Added {statistics.FuelAddedPounds:0.0} lb · Remaining {statistics.LastFuelPounds ?? 0:0.0} lb");
    }

    private static string FlightTitle(
        FlightSessionStatus status) =>
        status switch
        {
            FlightSessionStatus.Active =>
                "Active flight",

            FlightSessionStatus.Suspended =>
                "Flight suspended",

            FlightSessionStatus.Interrupted =>
                "Flight interrupted",

            FlightSessionStatus.Completed =>
                "Flight complete",

            FlightSessionStatus.Cancelled =>
                "Flight cancelled",

            _ =>
                "Flight session"
        };

    private static string FlightDetail(
        FlightSession session) =>
        session.Status switch
        {
            FlightSessionStatus.Suspended =>
                "The simulator connection was interrupted. OpenCareer preserved the flight and is waiting for trustworthy continuity before normal tracking resumes.",

            FlightSessionStatus.Interrupted =>
                "OpenCareer preserved the partial flight because continuity could not be proven. The session will not be auto-completed.",

            FlightSessionStatus.Completed =>
                "The flight session reached its terminal state and is preserved for debrief, logbook and one-time settlement integration.",

            FlightSessionStatus.Cancelled =>
                "The operation was cancelled. Its saved session remains available until the terminal record is acknowledged.",

            _ =>
                $"OpenCareer is tracking the operation at {FormatOperationState(session.OperationState)}."
        };

    private static string FormatOperationState(
        FlightOperationState state) =>
        state switch
        {
            FlightOperationState.ReadyForStart => "Ready for start",
            FlightOperationState.EngineStart => "Engine start",
            FlightOperationState.TaxiOut => "Taxi out",
            FlightOperationState.DepartureReady => "Departure ready",
            FlightOperationState.Airborne => "Airborne",
            FlightOperationState.Landed => "Landed",
            FlightOperationState.TaxiIn => "Taxi in",
            _ => state.ToString()
        };

    private static string LatestMilestone(
        FlightSessionMilestones milestones)
    {
        (string Name, DateTimeOffset? Time)[] values =
        [
            ("Complete", milestones.CompletedAt),
            ("Shutdown", milestones.ShutdownAt),
            ("Parked", milestones.ParkedAt),
            ("Taxi in", milestones.TaxiInAt),
            ("Landing", milestones.LandingAt),
            ("Touchdown", milestones.FirstTouchdownAt),
            ("Approach", milestones.ApproachAt),
            ("Takeoff", milestones.TakeoffAt),
            ("Takeoff roll", milestones.TakeoffRollAt),
            ("Taxi out", milestones.TaxiOutAt),
            ("Engine start", milestones.EngineStartAt),
            ("Aircraft ready", milestones.AircraftReadyAt),
            ("Interrupted", milestones.InterruptedAt)
        ];

        (string Name, DateTimeOffset? Time)? latest =
            values
                .Where(value => value.Time is not null)
                .OrderByDescending(value => value.Time)
                .FirstOrDefault();

        if (latest is null
            || latest.Value.Time is null)
        {
            return "No flight milestones yet";
        }

        return $"{latest.Value.Name} · {latest.Value.Time.Value.ToLocalTime():t}";
    }

    private static string FormatDuration(
        TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        int totalHours =
            (int)duration.TotalHours;

        return $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_lastTelemetry is not null)
            RefreshTelemetry(_lastTelemetry);

        if (_flightSessions.Current is not null)
        {
            _lastFlightSessionUpdatedAt = null;
            RefreshFlightSession();
        }
    }

    private static double FeetToMeters(double feet) => feet * 0.3048;
    private static double KnotsToKilometersPerHour(double knots) => knots * 1.852;
    private static double FeetPerMinuteToMetersPerSecond(double feetPerMinute) =>
        feetPerMinute * 0.00508;
    private static double PoundsToKilograms(double pounds) => pounds * 0.45359237;

    private void SetBoolean(
        ref bool field,
        bool value,
        string propertyName)
    {
        if (field == value)
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void SetField(ref string field, string value, string propertyName)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
