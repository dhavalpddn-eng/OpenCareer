using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Dashboard;

namespace OpenCareer.App.Views;

public sealed partial class DashboardPage : Page
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = RefreshInterval
    };

    private DashboardViewModel? _viewModel;
    private CancellationTokenSource? _refreshCancellation;

    public DashboardPage()
    {
        InitializeComponent();
        _refreshTimer.Tick += OnRefreshTimerTick;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not DashboardViewModel viewModel)
            return;

        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();

        _viewModel = viewModel;
        DataContext = viewModel;
        _refreshTimer.Start();

        try
        {
            await viewModel.RefreshAsync(_refreshCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Navigation away cancels an in-flight refresh.
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _refreshTimer.Stop();

        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = null;

        base.OnNavigatedFrom(e);
    }

    private async void OnRefreshTimerTick(object? sender, object e)
    {
        if (_viewModel is null || _refreshCancellation is null)
            return;

        try
        {
            await _viewModel.RefreshAsync(_refreshCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal when the user leaves Home during a refresh.
        }
    }

    private void PrimaryAction_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.RequestPrimaryAction();

    private void ActiveOperationAction_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.RequestActiveOperationAction();

    private void NavigateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not FrameworkElement element)
            return;

        if (Enum.TryParse(
                element.Tag?.ToString(),
                ignoreCase: true,
                out DashboardActionTarget target))
        {
            _viewModel.RequestNavigation(target);
        }
    }

    private void SocialSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e) =>
        _viewModel?.SetSocialSearch(SocialSearchBox.Text);
}
