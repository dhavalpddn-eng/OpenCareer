using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;

namespace OpenCareer.App.Views;

public sealed partial class SettingsPage : Page
{
    private TutorialViewModel? _tutorial;
    private SettingsViewModel? _settings;
    private DispatcherQueueTimer? _diagnosticTimer;
    private bool _suppressPreferenceEvents;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not SettingsPageContext context)
            return;

        _tutorial = context.Tutorial;
        _settings = context.Settings;
        DataContext = _settings;

        ApplyPreferenceControls();
        _settings.RefreshDiagnostics();

        _diagnosticTimer = DispatcherQueue.CreateTimer();
        _diagnosticTimer.Interval = TimeSpan.FromSeconds(1);
        _diagnosticTimer.Tick += OnDiagnosticTimerTick;
        _diagnosticTimer.Start();
        await _settings.RefreshLatestAirframeConsequenceAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_diagnosticTimer is not null)
        {
            _diagnosticTimer.Stop();
            _diagnosticTimer.Tick -= OnDiagnosticTimerTick;
            _diagnosticTimer = null;
        }

        base.OnNavigatedFrom(e);
    }

    private void ApplyPreferenceControls()
    {
        if (_settings is null)
            return;

        _suppressPreferenceEvents = true;
        try
        {
            UnitsComboBox.ItemsSource = _settings.UnitOptions;
            UnitsComboBox.SelectedIndex = _settings.SelectedUnitIndex;

            InputHintsComboBox.ItemsSource = _settings.InputHintOptions;
            InputHintsComboBox.SelectedIndex = _settings.SelectedInputHintIndex;

            ChecklistToggle.IsOn = _settings.ShowChecklistEveryFlight;
            TutorialOfferToggle.IsOn = _settings.AutomaticallyOfferTutorials;
            ReduceMotionToggle.IsOn = _settings.ReduceMotion;
            OnlineServicesToggle.IsOn = _settings.AllowOptionalOnlineServices;
        }
        finally
        {
            _suppressPreferenceEvents = false;
        }
    }

    private void OnDiagnosticTimerTick(DispatcherQueueTimer sender, object args) =>
        _settings?.RefreshDiagnostics();

    private async void UnitsComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressPreferenceEvents || _settings is null)
            return;

        await _settings.SetUnitsAsync(UnitsComboBox.SelectedIndex);
    }

    private async void InputHintsComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressPreferenceEvents || _settings is null)
            return;

        await _settings.SetInputHintPreferenceAsync(InputHintsComboBox.SelectedIndex);
    }

    private async void ChecklistToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressPreferenceEvents || _settings is null)
            return;

        await _settings.SetShowChecklistEveryFlightAsync(ChecklistToggle.IsOn);
    }

    private async void TutorialOfferToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressPreferenceEvents || _settings is null)
            return;

        await _settings.SetAutomaticallyOfferTutorialsAsync(TutorialOfferToggle.IsOn);
    }

    private async void ReduceMotionToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressPreferenceEvents || _settings is null)
            return;

        await _settings.SetReduceMotionAsync(ReduceMotionToggle.IsOn);
    }

    private async void OnlineServicesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressPreferenceEvents || _settings is null)
            return;

        await _settings.SetAllowOptionalOnlineServicesAsync(OnlineServicesToggle.IsOn);
    }

    private async void RefreshDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        _settings?.RefreshDiagnostics();
        if (_settings is not null)
            await _settings.RefreshLatestAirframeConsequenceAsync();
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is not null)
            await _settings.CreateBackupAsync();
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) =>
        _settings?.OpenDataFolder();

    private void OpenBackupsFolder_Click(object sender, RoutedEventArgs e) =>
        _settings?.OpenBackupsFolder();

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is not null)
            await _settings.ExportDiagnosticsAsync();
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e) =>
        _settings?.OpenLogsFolder();

    private void OpenDiagnosticsFolder_Click(object sender, RoutedEventArgs e) =>
        _settings?.OpenDiagnosticsFolder();

    private async void ResetPreferences_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null)
            return;

        await _settings.ResetPreferencesAsync();
        ApplyPreferenceControls();
    }

    private async void RestartIntro_Click(object sender, RoutedEventArgs e)
    {
        if (_tutorial is not null)
            await _tutorial.RestartIntroAsync();
    }

    private async void StartFirstJob_Click(object sender, RoutedEventArgs e)
    {
        if (_tutorial is not null)
            await _tutorial.StartFirstJobAsync();
    }

    private async void PreviewBannerTow_Click(object sender, RoutedEventArgs e)
    {
        if (_tutorial is not null)
            await _tutorial.PreviewBannerTowAsync();
    }

    private async void PreviewCarrierTakeoff_Click(object sender, RoutedEventArgs e)
    {
        if (_tutorial is not null)
            await _tutorial.PreviewCarrierTakeoffAsync();
    }

    private async void PreviewCarrierLanding_Click(object sender, RoutedEventArgs e)
    {
        if (_tutorial is not null)
            await _tutorial.PreviewCarrierLandingAsync();
    }
}
