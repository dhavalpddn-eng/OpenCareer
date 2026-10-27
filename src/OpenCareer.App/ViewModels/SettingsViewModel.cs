using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.App.Services;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Flights;
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
    private readonly FlightSessionCoordinator _flightSessions;
    private readonly FlightSessionPersistenceService _flightPersistence;
    private readonly IInputBindingDiscoverySource? _inputBindings;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly SemaphoreSlim _preferenceGate = new(1, 1);

    private string _actionStatus = "Ready.";
    private bool _isBusy;

    private string _connectionState = "Disconnected";
    private string _connectionIssue = "None";
    private string _simulatorIdentity = "Not reported";
    private string _simConnectVersion = "Not reported";
    private string _telemetryState = "No telemetry";
    private string _lastTelemetry = "—";
    private string _aircraftRuntimeState = "—";
    private string _careerRecoveryStatus;
    private string _inputBindingStatus =
        "UNAVAILABLE · No supported binding source is connected.";
    private bool _developmentProfileResetScheduled;

    public SettingsViewModel(
        IAppSettingsService settings,
        ISimulatorConnection connection,
        ISimulatorTelemetrySource telemetrySource,
        DiagnosticBundleService diagnostics,
        AppDataBackupService backup,
        ShellOpenService shell,
        OpenCareerDataPaths paths,
        FlightSessionCoordinator flightSessions,
        FlightSessionPersistenceService flightPersistence,
        IInputBindingDiscoverySource? inputBindings = null,
        ILogger<SettingsViewModel>? logger = null)
    {
        _settings = settings;
        _connection = connection;
        _telemetrySource = telemetrySource;
        _diagnostics = diagnostics;
        _backup = backup;
        _shell = shell;
        _paths = paths;
        _flightSessions =
            flightSessions
            ?? throw new ArgumentNullException(nameof(flightSessions));
        _flightPersistence =
            flightPersistence
            ?? throw new ArgumentNullException(nameof(flightPersistence));
        _inputBindings = inputBindings;
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        _careerRecoveryStatus =
            FormatCareerRecoveryStatus(
                _flightSessions.Current,
                _flightPersistence.RecoveryFailed);
        _developmentProfileResetScheduled =
            _paths.IsDevelopmentLiveTest
            && OpenCareerDevelopmentDataProfile.IsResetRequested(_paths);

        _settings.Changed += OnSettingsChanged;
        _flightSessions.SessionChanged += OnFlightSessionChanged;
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

    public string DataProfileStatus => _paths.IsDevelopmentLiveTest
        ? "DEVELOPMENT / TEST · KJFK · Isolated from normal career data"
        : "NORMAL CAREER · Persistent career data";

    public string DevelopmentProfileResetHelp => _paths.IsDevelopmentLiveTest
        ? _developmentProfileResetScheduled
            ? "CLEAN RESET SCHEDULED · Close OpenCareer and launch the KJFK test profile again."
            : "Use this after a test run to remove only the isolated KJFK jobs, flights, money, aircraft, logs and settings on the next launch."
        : "Clean test reset is disabled because this is the normal career profile.";

    public bool CanScheduleDevelopmentProfileReset =>
        _paths.IsDevelopmentLiveTest && !_developmentProfileResetScheduled;

    public string InputBindingStatus => _inputBindingStatus;

    public string CareerRecoveryStatus => _careerRecoveryStatus;

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
        SetField(
            ref _careerRecoveryStatus,
            FormatCareerRecoveryStatus(
                _flightSessions.Current,
                _flightPersistence.RecoveryFailed),
            nameof(CareerRecoveryStatus));

        InputBindingDiscoverySnapshot? bindingSnapshot =
            _inputBindings?.Read(Array.Empty<InputBindingActionIdentity>());
        SetField(
            ref _inputBindingStatus,
            bindingSnapshot is null
                ? "UNAVAILABLE · No supported binding source is connected."
                : $"{bindingSnapshot.State.ToString().ToUpperInvariant()} · {bindingSnapshot.Detail}",
            nameof(InputBindingStatus));

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
            current => current with
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
            current => current with { InputHints = preference },
            cancellationToken);
    }

    public Task SetShowChecklistEveryFlightAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            current => current with { ShowChecklistEveryFlight = value },
            cancellationToken);

    public Task SetAutomaticallyOfferTutorialsAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            current => current with { AutomaticallyOfferTutorials = value },
            cancellationToken);

    public Task SetReduceMotionAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            current => current with { ReduceMotion = value },
            cancellationToken);

    public Task SetAllowOptionalOnlineServicesAsync(
        bool value,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            current => current with { AllowOptionalOnlineServices = value },
            cancellationToken);

    public async Task ResetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            await _preferenceGate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                await _settings.ResetAsync(cancellationToken).ConfigureAwait(true);
                ActionStatus = "Application preferences reset to defaults. Career data and tutorial progress were not changed.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Could not reset OpenCareer application preferences.");
                ActionStatus =
                    "Application preferences could not be reset. Verify local storage is available, then retry. If this keeps happening, create a diagnostic bundle and use Open Logs Folder in Settings.";
            }
            finally
            {
                _preferenceGate.Release();
            }
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

    public void ScheduleDevelopmentProfileReset()
    {
        if (!_paths.IsDevelopmentLiveTest)
        {
            ActionStatus =
                "Clean test reset is unavailable because this is the normal career profile.";
            return;
        }

        try
        {
            OpenCareerDevelopmentDataProfile.RequestResetOnNextLaunch(_paths);
            _developmentProfileResetScheduled = true;
            OnPropertyChanged(nameof(CanScheduleDevelopmentProfileReset));
            OnPropertyChanged(nameof(DevelopmentProfileResetHelp));
            ActionStatus =
                "Clean KJFK test reset scheduled. Close OpenCareer, then launch the KJFK test profile again.";
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
        {
            _logger.LogError(
                ex,
                "Could not schedule the clean KJFK development profile reset.");
            ActionStatus =
                "Clean KJFK test reset was not scheduled. Verify the isolated test profile folder is available, then retry. If this keeps happening, create a diagnostic bundle before changing or deleting profile data.";
        }
    }

    private async Task UpdateAsync(
        Func<AppPreferences, AppPreferences> update,
        CancellationToken cancellationToken)
    {
        await _preferenceGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            try
            {
                AppPreferences preferences = update(_settings.Current);
                await _settings.UpdateAsync(preferences, cancellationToken).ConfigureAwait(true);
                ActionStatus = "Settings saved.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Could not save OpenCareer application preferences.");
                ActionStatus =
                    "Settings could not be saved. Verify local storage is available, then retry. If this keeps happening, create a diagnostic bundle and use Open Logs Folder in Settings.";
            }
        }
        finally
        {
            _preferenceGate.Release();
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

    private void OnFlightSessionChanged(
        object? sender,
        FlightSessionChangedEventArgs e) =>
        SetField(
            ref _careerRecoveryStatus,
            FormatCareerRecoveryStatus(
                e.Session,
                _flightPersistence.RecoveryFailed),
            nameof(CareerRecoveryStatus));

    private static string FormatCareerRecoveryStatus(
        FlightSession? session,
        bool recoveryFailed) =>
        session?.Status switch
        {
            null when recoveryFailed =>
                "RECOVERY ATTENTION · Flight checkpoint recovery did not complete. The app did not clear the stored checkpoint during recovery. Close and reopen OpenCareer to retry before starting another flight. If this warning returns, create a diagnostic bundle before making any reset or restore change.",
            null =>
                "SQLite-backed FlightSession checkpoint recovery, including previous-valid fallback, is enabled. No flight session is currently loaded.",
            FlightSessionStatus.Active =>
                "SQLite-backed FlightSession checkpoint recovery is enabled. An active flight session is loaded and remains eligible for restart checkpoints.",
            FlightSessionStatus.Suspended =>
                "SQLite-backed FlightSession checkpoint recovery is enabled. A suspended flight session is retained for continuity-safe resume; it is not completed.",
            FlightSessionStatus.Interrupted =>
                "SQLite-backed FlightSession checkpoint recovery is enabled. An interrupted terminal session is retained for retryable postflight or cleanup handling; it is not completed.",
            FlightSessionStatus.Completed =>
                "SQLite-backed FlightSession checkpoint recovery is enabled. A completed terminal session remains retained until required postflight processing and checkpoint cleanup succeed.",
            FlightSessionStatus.Cancelled =>
                "SQLite-backed FlightSession checkpoint recovery is enabled. A cancelled terminal session remains retained until checkpoint cleanup succeeds.",
            _ => throw new ArgumentOutOfRangeException(nameof(session))
        };

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
