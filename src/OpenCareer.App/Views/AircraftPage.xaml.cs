using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;

namespace OpenCareer.App.Views;

public sealed partial class AircraftPage : Page
{
    private CancellationTokenSource? _navigationCts;

    public AircraftPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not AircraftViewModel viewModel)
            return;

        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        var navigationCts = new CancellationTokenSource();
        _navigationCts = navigationCts;
        DataContext = viewModel;

        try
        {
            await viewModel.RefreshAsync(navigationCts.Token);
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            viewModel.ReportRefreshFailure(ex);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = null;
        base.OnNavigatedFrom(e);
    }

    private async void RefreshAircraft_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AircraftViewModel viewModel)
            return;

        CancellationTokenSource? navigationCts = _navigationCts;
        if (navigationCts is null || navigationCts.IsCancellationRequested)
            return;

        try
        {
            await viewModel.RefreshAsync(
                navigationCts.Token);
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            viewModel.ReportRefreshFailure(ex);
        }
    }
}
