using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.App.ViewModels;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly ISimulatorConnection _connection;
    private readonly ISimulatorTelemetrySource _telemetrySource;
    private readonly IAppSettingsService _settings;

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

    public ShellViewModel(
        ISimulatorConnection connection,
        ISimulatorTelemetrySource telemetrySource,
        IAppSettingsService settings)
    {
        _connection = connection;
        _telemetrySource = telemetrySource;
        _settings = settings;
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
