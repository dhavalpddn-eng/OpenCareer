using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenCareer.App.ViewModels;
using OpenCareer.App.Views;
using OpenCareer.Application.Dashboard;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Tutorials;

namespace OpenCareer.App;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly FlightSessionRuntime _flightRuntime;
    private readonly ILogger<MainWindow> _logger;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private bool _tutorialInitialized;

    public MainWindow(
        ShellViewModel viewModel,
        DashboardViewModel dashboard,
        LogbookViewModel logbook,
        MilitaryGovernmentViewModel militaryGovernment,
        TutorialViewModel tutorial,
        SettingsViewModel settings,
        FlightSessionRuntime flightRuntime,
        ILogger<MainWindow> logger)
    {
        ViewModel = viewModel;
        Dashboard = dashboard;
        Logbook = logbook;
        MilitaryGovernment = militaryGovernment;
        Tutorial = tutorial;
        Settings = settings;
        _flightRuntime =
            flightRuntime
            ?? throw new ArgumentNullException(nameof(flightRuntime));
        _logger =
            logger
            ?? throw new ArgumentNullException(nameof(logger));

        InitializeComponent();

        NavView.SelectedItem = DashboardItem;
        ContentFrame.Navigate(typeof(DashboardPage), Dashboard);

        Dashboard.NavigationRequested += OnDashboardNavigationRequested;
        Tutorial.PropertyChanged += OnTutorialPropertyChanged;
        Tutorial.NavigationRequested += OnTutorialNavigationRequested;
        Activated += OnWindowActivated;

        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(250);
        _statusTimer.Tick += OnStatusTimerTick;
        _statusTimer.Start();

        Closed += (_, _) => StopStatusUpdates();
    }

    public ShellViewModel ViewModel { get; }
    public DashboardViewModel Dashboard { get; }
    public LogbookViewModel Logbook { get; }
    public MilitaryGovernmentViewModel MilitaryGovernment { get; }
    public TutorialViewModel Tutorial { get; }
    public SettingsViewModel Settings { get; }

    public void StopStatusUpdates()
    {
        _statusTimer.Stop();
        _lifetimeCts.Cancel();
        _statusTimer.Tick -= OnStatusTimerTick;
        Dashboard.NavigationRequested -= OnDashboardNavigationRequested;
        Tutorial.PropertyChanged -= OnTutorialPropertyChanged;
        Tutorial.NavigationRequested -= OnTutorialNavigationRequested;
        Activated -= OnWindowActivated;
    }

    private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_tutorialInitialized)
            return;

        _tutorialInitialized = true;

        if (Settings.AutomaticallyOfferTutorials)
            await Tutorial.InitializeAsync();

        UpdateTutorialLayer();
    }

    private async void OnStatusTimerTick(
        DispatcherQueueTimer sender,
        object args)
    {
        ViewModel.RefreshConnectionStatus();
        Tutorial.RefreshLiveEvidence();

        try
        {
            bool changed =
                await _flightRuntime
                    .RefreshAsync(
                        _lifetimeCts.Token);

            if (changed)
            {
                ViewModel.RefreshConnectionStatus();
                Tutorial.RefreshLiveEvidence();

                await ViewModel
                    .RefreshCareerCompletionActionAsync(
                        _lifetimeCts.Token);
            }
        }
        catch (OperationCanceledException)
            when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "FlightSession runtime refresh failed.");
        }
    }

    private void NavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
            return;

        NavigateToTag(
            tag,
            args.SelectedItemContainer.Content?.ToString() ?? tag);
    }

    private void NavigateToTag(string tag, string? displayName = null)
    {
        switch (tag)
        {
            case "dashboard":
                Navigate(typeof(DashboardPage), Dashboard);
                break;
            case "current-flight":
                Navigate(typeof(CurrentFlightPage), ViewModel);
                break;
            case "military":
                Navigate(
                    typeof(MilitaryGovernmentPage),
                    MilitaryGovernment);
                break;
            case "logbook":
                Navigate(typeof(LogbookPage), Logbook);
                break;
            case "settings":
                Navigate(
                    typeof(SettingsPage),
                    new SettingsPageContext(Tutorial, Settings));
                break;
            default:
                Navigate(typeof(PlaceholderPage), displayName ?? tag);
                break;
        }
    }


    private void OnDashboardNavigationRequested(
        object? sender,
        DashboardNavigationRequestedEventArgs e)
    {
        string? tag = e.Target switch
        {
            DashboardActionTarget.Jobs => "jobs",
            DashboardActionTarget.Dispatch => "dispatch",
            DashboardActionTarget.CurrentFlight => "current-flight",
            DashboardActionTarget.MapWorld => "world",
            DashboardActionTarget.Aircraft => "aircraft",
            DashboardActionTarget.Maintenance => "maintenance",
            DashboardActionTarget.Company => "company",
            DashboardActionTarget.Finances => "finances",
            DashboardActionTarget.Career => "career",
            DashboardActionTarget.Logbook => "logbook",
            _ => null
        };

        if (tag is null)
            return;

        NavigationViewItem? item = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Tag?.ToString(),
                    tag,
                    StringComparison.Ordinal));

        if (item is not null)
            NavView.SelectedItem = item;
    }

    private void OnTutorialNavigationRequested(
        object? sender,
        TutorialNavigationRequestedEventArgs e)
    {
        NavigationViewItem? item = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Tag?.ToString(),
                    e.NavigationTag,
                    StringComparison.Ordinal));

        if (item is null)
            return;

        if (!ReferenceEquals(NavView.SelectedItem, item))
        {
            NavView.SelectedItem = item;
        }
        else
        {
            NavigateToTag(e.NavigationTag, item.Content?.ToString());
        }
    }

    private void OnTutorialPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TutorialViewModel.IsActive))
            UpdateTutorialLayer();
    }

    private void UpdateTutorialLayer()
    {
        TutorialLayer.Visibility = Tutorial.IsActive
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (Tutorial.IsActive)
        {
            DispatcherQueue.TryEnqueue(() =>
                TutorialNextButton.Focus(FocusState.Programmatic));
        }
    }

    private async void NextTutorial_Click(object sender, RoutedEventArgs e) =>
        await Tutorial.NextAsync();

    private async void BackTutorial_Click(object sender, RoutedEventArgs e) =>
        await Tutorial.BackAsync();

    private async void SkipTutorial_Click(object sender, RoutedEventArgs e) =>
        await Tutorial.SkipAsync();

    private void Navigate(Type pageType, object parameter)
    {
        if (ContentFrame.CurrentSourcePageType == pageType &&
            pageType != typeof(PlaceholderPage))
        {
            return;
        }

        ContentFrame.Navigate(pageType, parameter);
    }
}
