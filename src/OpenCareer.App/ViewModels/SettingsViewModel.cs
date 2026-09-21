using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenCareer.App.Services;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IAppSettingsService _settings;
    private readonly ISimulatorConnection _connection;
    private readonly ISimulatorTelemetrySource _telemetrySource;
    private readonly DiagnosticBundleService _diagnostics;
    private readonly AppDataBackupService _backup;
    private readonly ShellOpenService _shell;
    private readonly OpenCareerDataPaths _paths;

    private string _actionStatus = "Ready.";
    private bool _isBusy;

    private string _connectionState = "Disconnected";
    private string _connectionIssue = "None";
    private string _simulatorIdentity = "Not reported";
    private string _simConnectVersion = "Not reported";
    private string _telemetryState = "No telemetry";
    private string _lastTelemetry = "—";
    private string _aircraftRuntimeState = "—";

    public SettingsViewModel(
        IAppSettingsService settings,
        ISimulatorConnection connection,
        ISimulatorTelemetrySource telemetrySource,
        DiagnosticBundleService diagnostics,
        AppDataBackupService backup,
        ShellOpenService shell,
        OpenCareerDataPaths paths)
    {
        _settings = settings;
        _connection = connection;
        _telemetrySource = telemetrySource;
        _diagnostics = diagnostics;
        _backup = backup;
        _shell = shell;
        _paths = paths;

        _settings.Changed += OnSettingsChanged;
        RefreshDiagnostics();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> UnitOptions { get; } =
    [
        "Aviation — ft / kt / lb",
        "Metric — m / km/h / kg"
    ];

    public IReadOnlyList<string> InputHintOptions { get; } =
    [
        "Controller + keyboard",
        "Controller first",
        "Keyboard first"
    ];

    public int SelectedUnitIndex =>
        _settings.Current.MeasurementSystem == MeasurementSystem.Metric ? 1 : 0;

    public int SelectedInputHintIndex => _settings.Current.InputHints switch
    {
        InputHintPreference.ControllerFirst => 1,
        InputHintPreference.KeyboardFirst => 2,
        _ => 0
    };

    public bool ShowChecklistEveryFlight => _settings.Current.ShowChecklistEveryFlight;
    public bool AutomaticallyOfferTutorials => _settings.Current.AutomaticallyOfferTutorials;
    public bool ReduceMotion => _settings.Current.ReduceMotion;
    public bool AllowOptionalOnlineServices => _settings.Current.AllowOptionalOnlineServices;

    public string ConnectionState => _connectionState;
    public string ConnectionIssue => _connectionIssue;
    public string SimulatorIdentity => _simulatorIdentity;
    public string SimConnectVersion => _simConnectVersion;
    public string TelemetryState => _telemetryState;
    public string LastTelemetry => _lastTelemetry;
    public string AircraftRuntimeState => _aircraftRuntimeState;

    public string AppVersion =>
        typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() ?? "unknown";

    public string RuntimeSummary =>
        $"{RuntimeInformation.FrameworkDescription} / {RuntimeInformation.ProcessArchitecture}";

    public string DataFolder => _paths.Root;
    public string LogFile => _paths.LogFile;
    public string BackupsFolder => _paths.BackupsFolder;
    public string DiagnosticsFolder => _paths.DiagnosticsFolder;

    public string InputBindingStatus =>
        "Binding resolver is not implemented yet (MBL-05). This preference controls which verified hint is shown first once bindings are available.";

    public string CareerRecoveryStatus =>
        "Settings, tutorial state and logs can be backed up now. Authoritative FlightSession/SQLite recovery will be added with MBL-07 before career saves rely on it.";

    public string OptionalServicesStatus =>
        AllowOptionalOnlineServices
            ? "Permission is enabled, but no optional external provider is currently configured."
            : "Offline-first mode. Optional online providers are not permitted until you enable them.";

    public string AccessibilityStatus =>
        "OpenCareer keeps native WinUI focus/high-contrast behavior. Reduced-motion preference is persisted for current and future animations.";

    public string ActionStatus
    {
        get => _actionStatus;
        private set => SetField(ref _actionStatus, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public void RefreshDiagnostics()
    {
        SimulatorConnectionSnapshot snapshot = _connection.Current;
        AircraftTelemetrySnapshot? telemetry = snapshot.State == SimulatorConnectionState.Connected
            ? _telemetrySource.Latest
            : null;

        SetField(ref _connectionState, snapshot.State.ToString(), nameof(ConnectionState));
        SetField(
            ref _connectionIssue,
            snapshot.Issue == SimulatorConnectionIssue.None ? "None" : snapshot.Issue.ToString(),
            nameof(ConnectionIssue));

        string simulator = snapshot.Simulator is null
            ? "Not reported"
            : $"{snapshot.Simulator.Name} {snapshot.Simulator.ApplicationVersion}";
        SetField(ref _simulatorIdentity, simulator, nameof(SimulatorIdentity));

        string simConnectVersion = snapshot.Simulator?.SimConnectVersion.ToString() ?? "Not reported";
        SetField(ref _simConnectVersion, simConnectVersion, nameof(SimConnectVersion));

        if (telemetry is null)
        {
            SetField(ref _telemetryState, "No telemetry", nameof(TelemetryState));
            SetField(ref _lastTelemetry, "—", nameof(LastTelemetry));
            SetField(ref _aircraftRuntimeState, "—", nameof(AircraftRuntimeState));
            return;
        }

        TimeSpan age = DateTimeOffset.UtcNow - telemetry.Timestamp;
        string freshness = age <= TimeSpan.FromSeconds(3)
            ? "Live"
            : $"Stale — {Math.Max(0, age.TotalSeconds):0}s old";
        SetField(ref _telemetryState, freshness, nameof(TelemetryState));
        SetField(
            ref _lastTelemetry,
            telemetry.Timestamp.ToLocalTime().ToString("G"),
            nameof(LastTelemetry));

        string state = telemetry.SlewActive
            ? "Slew active"
            : telemetry.Paused
                ? "Paused"
                : telemetry.OnGround
                    ? "On ground"
                    : "Airborne";
        SetField(ref _aircraftRuntimeState, state, nameof(AircraftRuntimeState));
    }

    public Task SetUnitsAsync(int selectedIndex, CancellationToken cancellationToken = default) =>
        UpdateAsync(
            _settings.Current with
            {
                MeasurementSystem = selectedIndex == 1
                    ? MeasurementSystem.Metric
                    : MeasurementSystem.Aviation
            },
            cancellationToken);

    public Task SetInputHintPreferenceAsync(
        int selectedIndex,
        CancellationToken cancellationToken = default)
    {
        InputHintPreference preference = selectedIndex switch
        {
            1 => InputHintPreference.ControllerFirst,
            2 => InputHintPreference.KeyboardFirst,
            _ => InputHintPreference.Both
        };

        return UpdateAsync(
            _settings.Current with { InputHints = preference },
            cancellationToken);
    }

    public Task SetShowChecklistEveryFlightAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            _settings.Current with { ShowChecklistEveryFlight = value },
            cancellationToken);

    public Task SetAutomaticallyOfferTutorialsAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            _settings.Current with { AutomaticallyOfferTutorials = value },
            cancellationToken);

    public Task SetReduceMotionAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            _settings.Current with { ReduceMotion = value },
            cancellationToken);

    public Task SetAllowOptionalOnlineServicesAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            _settings.Current with { AllowOptionalOnlineServices = value },
            cancellationToken);

    public async Task ResetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            await _settings.ResetAsync(cancellationToken).ConfigureAwait(true);
            ActionStatus = "Application preferences reset to defaults. Career data and tutorial progress were not changed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActionStatus = $"Could not reset preferences: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            SettingsActionResult result = await _backup.CreateBackupAsync(cancellationToken)
                .ConfigureAwait(true);
            ActionStatus = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            RefreshDiagnostics();
            SettingsActionResult result = await _diagnostics.CreateAsync(cancellationToken)
                .ConfigureAwait(true);
            ActionStatus = result.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void OpenDataFolder() =>
        ActionStatus = _shell.OpenFolder(_paths.Root).Message;

    public void OpenLogsFolder() =>
        ActionStatus = _shell.OpenFolder(_paths.LogsFolder).Message;

    public void OpenBackupsFolder() =>
        ActionStatus = _shell.OpenFolder(_paths.BackupsFolder).Message;

    public void OpenDiagnosticsFolder() =>
        ActionStatus = _shell.OpenFolder(_paths.DiagnosticsFolder).Message;

    private async Task UpdateAsync(
        AppPreferences preferences,
        CancellationToken cancellationToken)
    {
        try
        {
            await _settings.UpdateAsync(preferences, cancellationToken).ConfigureAwait(true);
            ActionStatus = "Settings saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActionStatus = $"Could not save settings: {ex.Message}";
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SelectedUnitIndex));
        OnPropertyChanged(nameof(SelectedInputHintIndex));
        OnPropertyChanged(nameof(ShowChecklistEveryFlight));
        OnPropertyChanged(nameof(AutomaticallyOfferTutorials));
        OnPropertyChanged(nameof(ReduceMotion));
        OnPropertyChanged(nameof(AllowOptionalOnlineServices));
        OnPropertyChanged(nameof(OptionalServicesStatus));
    }

    private void SetField(
        ref string field,
        string value,
        [CallerMemberName] string? propertyName = null)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void SetField(
        ref bool field,
        bool value,
        [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
