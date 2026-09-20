using System.ComponentModel;
using System.Runtime.CompilerServices;
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
