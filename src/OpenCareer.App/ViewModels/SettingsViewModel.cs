using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OpenCareer.App.Services;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Telemetry;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Aircraft;

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
    private readonly IFlightAirframeConsequenceStore _airframeConsequences;
    private readonly FlightSessionCoordinator _flightSessions;
    private readonly ILiveAircraftIdentitySource _liveAircraft;
    private readonly IAircraftRegistrySource _aircraftRegistry;
    private readonly ILogger<SettingsViewModel> _logger;

    private string _actionStatus = "Ready.";
    private bool _isBusy;

    private string _connectionState = "Disconnected";
    private string _connectionIssue = "None";
    private string _simulatorIdentity = "Not reported";
    private string _simConnectVersion = "Not reported";
    private string _telemetryState = "No telemetry";
    private string _lastTelemetry = "—";
    private string _aircraftRuntimeState = "—";
    private string _liveAircraftIdentity = "No live aircraft TITLE received";
    private string? _lastResolvedTitle;
    private bool _resolvingTitle;
    private string _latestAirframeConsequence = "No finalized flight consequence yet.";

    public SettingsViewModel(
        IAppSettingsService settings,
        ISimulatorConnection connection,
        ISimulatorTelemetrySource telemetrySource,
        DiagnosticBundleService diagnostics,
        AppDataBackupService backup,
        ShellOpenService shell,
        OpenCareerDataPaths paths,
        IFlightAirframeConsequenceStore airframeConsequences,
        FlightSessionCoordinator flightSessions,
        ILiveAircraftIdentitySource liveAircraft,
        IAircraftRegistrySource aircraftRegistry,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _connection = connection;
        _telemetrySource = telemetrySource;
        _diagnostics = diagnostics;
        _backup = backup;
        _shell = shell;
        _paths = paths;
        _airframeConsequences = airframeConsequences;
        _flightSessions = flightSessions;
        _liveAircraft = liveAircraft;
        _aircraftRegistry = aircraftRegistry;
        _logger = logger;

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
    public string LiveAircraftIdentity => _liveAircraftIdentity;

    public async Task RefreshLiveAircraftIdentityAsync()
    {
        string? title = _liveAircraft.CurrentAircraftTitle;
        if (string.IsNullOrWhiteSpace(title))
        {
            _lastResolvedTitle = null;
            SetField(ref _liveAircraftIdentity, "No live aircraft TITLE received", nameof(LiveAircraftIdentity));
            return;
        }

        if (_resolvingTitle || string.Equals(title, _lastResolvedTitle, StringComparison.Ordinal))
            return;

        _resolvingTitle = true;
        try
        {
            string canonical = AircraftCanonicalIdentity.FromMsfsTitle(title);
            AircraftRegistryResolution? resolution = await _aircraftRegistry.FindAircraftAsync(canonical);
            SetField(ref _liveAircraftIdentity,
                $"TITLE: {title} | AircraftId: {canonical} | Registry: {(resolution is null ? "unresolved" : resolution.InstallationStatus.ToString())}",
                nameof(LiveAircraftIdentity));
            _logger.LogInformation(
                "Live aircraft registry: raw={RawTitle}, canonical={CanonicalAircraftId}, resolved={Resolved}, connection={ConnectionState}.",
                title, canonical, resolution is not null, _connection.Current.State);
            _lastResolvedTitle = title;
        }
        catch (Exception ex)
        {
            SetField(ref _liveAircraftIdentity, $"TITLE: {title} | Registry error: {ex.Message}", nameof(LiveAircraftIdentity));
        }
        finally
        {
            _resolvingTitle = false;
        }
    }
    public string LatestAirframeConsequence => _latestAirframeConsequence;

    public async Task RefreshLatestAirframeConsequenceAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            FlightSession? current = _flightSessions.Current;
            FlightAirframeConsequence? currentResult = null;
            if (current?.IsTerminal == true)
            {
                if (current.AircraftIdentity is null)
                {
                    SetField(ref _latestAirframeConsequence,
                        $"Session {current.SessionId:D} finalized without individual airframe identity (legacy). No ownership consequence applied.",
                        nameof(LatestAirframeConsequence));
                    return;
                }

                currentResult = await _airframeConsequences
                    .ReadAsync(current.SessionId, cancellationToken);
                if (currentResult is null)
                {
                    SetField(ref _latestAirframeConsequence,
                        $"Session {current.SessionId:D} finalized for {current.AircraftIdentity.Kind} {current.AircraftIdentity.InstanceId}; consequence pending. Recovery retries from the terminal checkpoint.",
                        nameof(LatestAirframeConsequence));
                    return;
                }
            }

            FlightAirframeConsequence? latest = currentResult
                ?? await _airframeConsequences.ReadLatestAsync(cancellationToken);
            SetField(ref _latestAirframeConsequence,
                latest is null ? "No finalized flight consequence yet."
                    : DescribeConsequence(latest), nameof(LatestAirframeConsequence));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            SetField(ref _latestAirframeConsequence,
                $"Flight consequence history unavailable: {ex.Message}",
                nameof(LatestAirframeConsequence));
        }
    }

    private static string DescribeConsequence(FlightAirframeConsequence result)
    {
        FlightAirframeSummary s = result.Summary;
        FlightAirframeDecision? d = result.Decision;
        FlightAirframeApplication? a = result.Application;
        string source = s.Aircraft.Kind == FlightSessionAircraftKind.Owned
            ? "OwnershipId" : "ProviderAircraftInstanceId";
        string evidence = d?.DescentSampleAt is { } sample
            ? $"sample {sample:u} · contact {d.CorrelatedTouchdownAt:u} · {s.MaximumTouchdownApproachDescentFeetPerMinute:0} ft/min · {d.CorrelationAgeSeconds:0.#}s before contact · within {d.CorrelationWindowSeconds:0}s window: {d.DescentWithinCorrelationWindow}"
            : "No descent sample correlated with touchdown";
        string maintenance = a?.Before is { } before && a.After is { } after
            ? $"Airframe wear {before.AirframeWearPercent:0.###} → {after.AirframeWearPercent:0.###}% · engine wear {before.EngineWearPercent:0.###} → {after.EngineWearPercent:0.###}% · gear wear {before.GearWearPercent:0.###} → {after.GearWearPercent:0.###}% · damage {before.DamagePercent:0.###} → {after.DamagePercent:0.###}% · cycles {before.LandingCycles} → {after.LandingCycles}"
            : s.Aircraft.Kind == FlightSessionAircraftKind.Provider
                ? "Provider history recorded; no player ownership changed."
                : "Maintenance state delta unavailable for this legacy record.";
        return $"Session {s.SessionId:D} · Contract {s.ContractId?.ToString("D") ?? "none"}\n"
            + $"{source} {s.Aircraft.InstanceId} · AircraftId {s.Aircraft.AircraftId}\n"
            + $"{s.StartedAt:u} → {s.EndedAt:u} · block {s.BlockHours:0.##}h · airborne {s.AirborneHours:0.##}h · takeoffs {s.TakeoffCount} · landings {s.LandingCycles} · bounces {s.BounceCount}\n"
            + $"Accepted telemetry samples {s.AcceptedObservationCount} · last accepted {s.LastAcceptedObservationAt?.ToString("u") ?? "none"}\n"
            + $"Fuel {s.StartFuelPounds?.ToString("0.#") ?? "?"} → {s.EndFuelPounds?.ToString("0.#") ?? "?"} lb · used {s.FuelBurnedPounds:0.#} lb · max IAS {s.MaximumIndicatedAirspeedKnots:0.#} kt · max altitude {s.MaximumAltitudeFeet:0} ft\n"
            + $"Touchdown {s.TouchdownAt?.ToString("u") ?? "unobserved"} · descent: {evidence}\n"
            + $"Flight finalized: {s.FinalizationStatus?.ToString() ?? "legacy status unavailable"} · classification {result.Severity} · {d?.EvidenceReason ?? "Legacy decision detail unavailable"} · MSFS crash reported: {s.CrashReported}\n"
            + $"Thresholds elevated/minor/major {d?.ElevatedThresholdFpm:0}/{d?.MinorDamageThresholdFpm:0}/{d?.MajorDamageThresholdFpm:0} ft/min\n"
            + $"Estimated wear: airframe {d?.RoutineAirframeWearPercent:0.###}% · engine {d?.RoutineEngineWearPercent:0.###}% · landing cycle {d?.LandingCycleWearPercent:0.###}% · additional gear {d?.AdditionalLandingGearWearPercent:0.###}%\n"
            + $"Estimated damage: touchdown {d?.TouchdownDamagePercent:0.###}% · crash {d?.CrashDamagePercent:0.###}% · persisted once {a is not null}\n"
            + maintenance;
    }

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
