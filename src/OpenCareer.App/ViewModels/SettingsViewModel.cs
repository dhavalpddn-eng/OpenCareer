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
    private const string PreferenceResetRequiredStatus =
        "Preferences could not be loaded. The original preference file was preserved and preference edits are locked. Use Reset preferences to defaults below to repair preferences; career data is not changed.";

    private readonly IAppSettingsService _settings;
    private readonly ISimulatorConnection _connection;
    private readonly ISimulatorTelemetrySource _telemetrySource;
    private readonly DiagnosticBundleService _diagnostics;
    private readonly AppDataBackupService _backup;
    private readonly AppDataRestoreService _restore;
    private readonly ShellOpenService _shell;
    private readonly OpenCareerDataPaths _paths;
    private readonly FlightSessionCoordinator _flightSessions;
    private readonly FlightSessionPersistenceService _flightPersistence;
    private readonly IInputBindingDiscoverySource? _inputBindings;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly SemaphoreSlim _preferenceGate = new(1, 1);

    private string _actionStatus = "Ready.";
    private int _settingsActionActive;

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
    private IReadOnlyList<string> _backupArchiveOptions = Array.Empty<string>();
    private int _selectedBackupIndex = -1;
    private bool _hasPendingDatabaseRestore;
    private bool _databaseRestoreApplyFailed;
    private string _backupRestoreStatus =
        "No OpenCareer backup archives are available in the Backups folder.";

    public SettingsViewModel(
        IAppSettingsService settings,
        ISimulatorConnection connection,
        ISimulatorTelemetrySource telemetrySource,
        DiagnosticBundleService diagnostics,
        AppDataBackupService backup,
        AppDataRestoreService restore,
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
        _restore = restore;
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

        if (PreferencesRequireReset)
            _actionStatus = PreferenceResetRequiredStatus;

        RefreshBackupArchives();
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

    public IReadOnlyList<string> BackupArchiveOptions => _backupArchiveOptions;

    public int SelectedBackupIndex
    {
        get => _selectedBackupIndex;
        set
        {
            int normalized = value >= 0 && value < _backupArchiveOptions.Count
                ? value
                : -1;

            if (_selectedBackupIndex == normalized)
                return;

            _selectedBackupIndex = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedBackupArchive));
            OnPropertyChanged(nameof(CanStageSelectedBackup));
            UpdateBackupRestoreStatus();
        }
    }

    public string? SelectedBackupArchive =>
        _selectedBackupIndex >= 0
        && _selectedBackupIndex < _backupArchiveOptions.Count
            ? _backupArchiveOptions[_selectedBackupIndex]
            : null;

    public bool HasPendingDatabaseRestore => _hasPendingDatabaseRestore;

    public bool DatabaseRestoreApplyFailed => _databaseRestoreApplyFailed;

    public bool CanStageSelectedBackup =>
        CanRunSettingsActions
        && SelectedBackupArchive is not null;

    public bool CanCancelPendingDatabaseRestore =>
        CanRunSettingsActions
        && HasPendingDatabaseRestore;

    public string BackupRestoreStatus => _backupRestoreStatus;

    public string DataProfileStatus => _paths.IsDevelopmentLiveTest
        ? "DEVELOPMENT / TEST · KJFK · Isolated from normal career data"
        : "NORMAL CAREER · Persistent career data";

    public string DevelopmentProfileResetHelp => _paths.IsDevelopmentLiveTest
        ? _developmentProfileResetScheduled
            ? "CLEAN RESET SCHEDULED · Close OpenCareer and launch the KJFK test profile again."
            : "Use this after a test run to remove only the isolated KJFK jobs, flights, money, aircraft, logs and settings on the next launch."
        : "Clean test reset is disabled because this is the normal career profile.";

    public bool CanScheduleDevelopmentProfileReset =>
        _paths.IsDevelopmentLiveTest
        && !_developmentProfileResetScheduled
        && CanRunSettingsActions;

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

    public bool IsBusy => Volatile.Read(ref _settingsActionActive) != 0;

    public bool CanRunSettingsActions => !IsBusy;

    public bool CanEditPreferences =>
        CanRunSettingsActions
        && !PreferencesRequireReset;

    public string PreferenceStorageStatus => PreferencesRequireReset
        ? "PREFERENCES RECOVERY REQUIRED · OpenCareer could not read the existing preference file. Defaults are active for this launch, preference edits are locked, and the original file is preserved. Use Reset preferences to defaults below to repair preferences; career data is not changed."
        : "Preferences are available from local storage.";

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
        if (!TryBeginSettingsAction())
            return;

        try
        {
            await _preferenceGate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                await _settings.ResetAsync(cancellationToken).ConfigureAwait(true);
                ActionStatus = "Application preferences reset to defaults. Career data and tutorial progress were not changed.";
                OnPropertyChanged(nameof(CanEditPreferences));
                OnPropertyChanged(nameof(PreferenceStorageStatus));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Could not reset OpenCareer application preferences.");
                ActionStatus = PreferencesRequireReset
                    ? "Preferences remain locked because Reset preferences could not repair the preserved preference file. Career data was not changed. Verify local storage is available, then retry Reset preferences. If this keeps happening, create a diagnostic bundle and use Open Logs Folder in Settings."
                    : "Application preferences could not be reset. Verify local storage is available, then retry. If this keeps happening, create a diagnostic bundle and use Open Logs Folder in Settings.";
            }
            finally
            {
                _preferenceGate.Release();
            }
        }
        finally
        {
            EndSettingsAction();
        }
    }

    public async Task CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBeginSettingsAction())
            return;

        try
        {
            await _preferenceGate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                SettingsActionResult result = await _backup.CreateBackupAsync(cancellationToken)
                    .ConfigureAwait(true);
                ActionStatus = result.Message;
                RefreshBackupArchives();
            }
            finally
            {
                _preferenceGate.Release();
            }
        }
        finally
        {
            EndSettingsAction();
        }
    }

    public void RefreshBackupArchives()
    {
        string? selected = SelectedBackupArchive;

        try
        {
            _backupArchiveOptions = Directory.Exists(_paths.BackupsFolder)
                ? Directory
                    .EnumerateFiles(
                        _paths.BackupsFolder,
                        "opencareer-backup-*.zip",
                        SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();

            _selectedBackupIndex = selected is null
                ? -1
                : IndexOfBackupArchive(selected);
            _hasPendingDatabaseRestore = _restore.HasPendingDatabaseRestore;
            _databaseRestoreApplyFailed =
                _restore.PendingDatabaseRestoreApplyFailed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not enumerate OpenCareer backup archives.");
            _backupArchiveOptions = Array.Empty<string>();
            _selectedBackupIndex = -1;
            _hasPendingDatabaseRestore = _restore.HasPendingDatabaseRestore;
            _databaseRestoreApplyFailed =
                _restore.PendingDatabaseRestoreApplyFailed;
        }

        OnPropertyChanged(nameof(BackupArchiveOptions));
        OnPropertyChanged(nameof(SelectedBackupIndex));
        OnPropertyChanged(nameof(SelectedBackupArchive));
        OnPropertyChanged(nameof(HasPendingDatabaseRestore));
        OnPropertyChanged(nameof(DatabaseRestoreApplyFailed));
        OnPropertyChanged(nameof(CanStageSelectedBackup));
        OnPropertyChanged(nameof(CanCancelPendingDatabaseRestore));
        UpdateBackupRestoreStatus();
    }

    public async Task StageSelectedBackupAsync(
        CancellationToken cancellationToken = default)
    {
        string? selected = SelectedBackupArchive;
        if (selected is null)
        {
            ActionStatus =
                "Select an OpenCareer backup from the Backups folder before staging a restore.";
            return;
        }

        if (!TryBeginSettingsAction())
            return;

        try
        {
            await _preferenceGate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                SettingsActionResult safetyBackup = await _backup
                    .CreateBackupAsync(cancellationToken)
                    .ConfigureAwait(true);

                if (!safetyBackup.Success)
                {
                    ActionStatus =
                        "Restore was not staged because a current safety backup could not be created. "
                        + safetyBackup.Message;
                    return;
                }

                string selectedPath = Path.Combine(_paths.BackupsFolder, selected);
                SettingsActionResult result = await _restore
                    .StageDatabaseRestoreAsync(selectedPath, cancellationToken)
                    .ConfigureAwait(true);

                ActionStatus = result.Success
                    ? $"Current safety backup created. {result.Message}"
                    : $"Current safety backup created, but restore staging stopped. {result.Message}";
            }
            finally
            {
                _preferenceGate.Release();
            }
        }
        finally
        {
            RefreshBackupArchives();
            EndSettingsAction();
        }
    }

    public void CancelPendingDatabaseRestore()
    {
        if (!TryBeginSettingsAction())
            return;

        try
        {
            SettingsActionResult result = _restore.CancelPendingDatabaseRestore();
            ActionStatus = result.Message;
            RefreshBackupArchives();
        }
        finally
        {
            EndSettingsAction();
        }
    }

    public async Task ExportDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBeginSettingsAction())
            return;

        try
        {
            await _preferenceGate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                RefreshDiagnostics();
                SettingsActionResult result = await _diagnostics.CreateAsync(cancellationToken)
                    .ConfigureAwait(true);
                ActionStatus = result.Message;
            }
            finally
            {
                _preferenceGate.Release();
            }
        }
        finally
        {
            EndSettingsAction();
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

        if (_developmentProfileResetScheduled)
        {
            ActionStatus =
                "Clean KJFK test reset is already scheduled for the next launch.";
            return;
        }

        if (!TryBeginSettingsAction())
            return;

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
        finally
        {
            EndSettingsAction();
        }
    }

    private async Task UpdateAsync(
        Func<AppPreferences, AppPreferences> update,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            ReportSettingsActionAlreadyRunning();
            return;
        }

        if (PreferencesRequireReset)
        {
            ActionStatus = PreferenceResetRequiredStatus;
            return;
        }

        await _preferenceGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (IsBusy)
            {
                ReportSettingsActionAlreadyRunning();
                return;
            }

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

    private bool TryBeginSettingsAction()
    {
        if (Interlocked.CompareExchange(ref _settingsActionActive, 1, 0) != 0)
        {
            ReportSettingsActionAlreadyRunning();
            return false;
        }

        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanRunSettingsActions));
        OnPropertyChanged(nameof(CanEditPreferences));
        OnPropertyChanged(nameof(CanScheduleDevelopmentProfileReset));
        OnPropertyChanged(nameof(CanStageSelectedBackup));
        OnPropertyChanged(nameof(CanCancelPendingDatabaseRestore));
        return true;
    }

    private void EndSettingsAction()
    {
        Interlocked.Exchange(ref _settingsActionActive, 0);
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanRunSettingsActions));
        OnPropertyChanged(nameof(CanEditPreferences));
        OnPropertyChanged(nameof(CanScheduleDevelopmentProfileReset));
        OnPropertyChanged(nameof(CanStageSelectedBackup));
        OnPropertyChanged(nameof(CanCancelPendingDatabaseRestore));
    }

    private int IndexOfBackupArchive(string fileName)
    {
        for (int index = 0; index < _backupArchiveOptions.Count; index++)
        {
            if (string.Equals(
                _backupArchiveOptions[index],
                fileName,
                StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void UpdateBackupRestoreStatus()
    {
        string status = DatabaseRestoreApplyFailed
            ? HasPendingDatabaseRestore
                ? "RESTORE ATTENTION · The staged database restore did not complete during startup. Cancel it below to stop automatic retry, or create a diagnostic bundle before restarting again. OpenCareer has not recorded the selected backup as applied."
                : "RESTORE ATTENTION · The database restore did not complete during startup and no staged restore remains to retry or cancel. OpenCareer has not recorded the selected backup as applied. Review current career data and create a diagnostic bundle before selecting that backup again."
            : HasPendingDatabaseRestore
            ? "RESTORE STAGED · Restart OpenCareer to apply it, or cancel the staged restore below to keep current career data."
            : SelectedBackupArchive is not null
                ? $"Selected: {SelectedBackupArchive}. Restoring requires confirmation and creates a current safety backup first."
                : _backupArchiveOptions.Count == 0
                    ? "No OpenCareer backup archives are available in the Backups folder."
                    : "Select an OpenCareer backup archive to make restore available.";

        SetField(ref _backupRestoreStatus, status, nameof(BackupRestoreStatus));
    }

    private void ReportSettingsActionAlreadyRunning() =>
        ActionStatus =
            "Another Settings action is already running. Wait for it to finish, then retry.";

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SelectedUnitIndex));
        OnPropertyChanged(nameof(SelectedInputHintIndex));
        OnPropertyChanged(nameof(ShowChecklistEveryFlight));
        OnPropertyChanged(nameof(AutomaticallyOfferTutorials));
        OnPropertyChanged(nameof(ReduceMotion));
        OnPropertyChanged(nameof(AllowOptionalOnlineServices));
        OnPropertyChanged(nameof(OptionalServicesStatus));
        OnPropertyChanged(nameof(CanEditPreferences));
        OnPropertyChanged(nameof(PreferenceStorageStatus));
    }

    private bool PreferencesRequireReset =>
        _settings is IAppSettingsRecoveryState { RequiresReset: true };

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
