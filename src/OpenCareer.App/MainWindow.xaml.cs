using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using OpenCareer.App.ViewModels;
using OpenCareer.App.Views;

namespace OpenCareer.App;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimer _statusTimer;

    public MainWindow(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        NavView.SelectedItem = DashboardItem;
        ContentFrame.Navigate(typeof(DashboardPage), ViewModel);
        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(250);
        _statusTimer.Tick += OnStatusTimerTick;
        _statusTimer.Start();
        Closed += (_, _) => StopStatusUpdates();
    }

    public ShellViewModel ViewModel { get; }

    public void StopStatusUpdates()
    {
        _statusTimer.Stop();
        _statusTimer.Tick -= OnStatusTimerTick;
    }

    private void OnStatusTimerTick(DispatcherQueueTimer sender, object args) => ViewModel.RefreshConnectionStatus();

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        switch (tag)
        {
            case "dashboard":
                Navigate(typeof(DashboardPage), ViewModel);
                break;
            case "current-flight":
                Navigate(typeof(CurrentFlightPage), ViewModel);
                break;
            default:
                Navigate(typeof(PlaceholderPage), args.SelectedItemContainer.Content?.ToString() ?? tag);
                break;
        }
    }

    private void Navigate(Type pageType, object parameter)
    {
        if (ContentFrame.CurrentSourcePageType == pageType && pageType != typeof(PlaceholderPage))
        {
            return;
        }

        ContentFrame.Navigate(pageType, parameter);
    }
}
