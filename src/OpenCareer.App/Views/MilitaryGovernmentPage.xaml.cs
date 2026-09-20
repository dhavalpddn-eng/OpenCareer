using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;

namespace OpenCareer.App.Views;

public sealed partial class MilitaryGovernmentPage : Page
{
    private readonly DispatcherQueueTimer _refreshTimer;
    private MilitaryGovernmentViewModel? _viewModel;

    public MilitaryGovernmentPage()
    {
        InitializeComponent();

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        _refreshTimer.Tick += OnRefreshTimerTick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not MilitaryGovernmentViewModel viewModel)
            return;

        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Refresh();
        _refreshTimer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _refreshTimer.Stop();
        base.OnNavigatedFrom(e);
    }

    private void OnRefreshTimerTick(
        DispatcherQueueTimer sender,
        object args) =>
        _viewModel?.Refresh();

    private async void AcceptSuccessor_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel is null)
            return;

        await _viewModel.AcceptSuccessorAsync();
    }

    private void DeclineSuccessor_Click(
        object sender,
        RoutedEventArgs e) =>
        _viewModel?.DeclineSuccessor();

    private void ReconsiderSuccessor_Click(
        object sender,
        RoutedEventArgs e) =>
        _viewModel?.ReconsiderSuccessor();
}
