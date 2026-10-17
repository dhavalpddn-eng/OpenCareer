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
        _navigationCts = new();
        DataContext = viewModel;

        try
        {
            await viewModel.RefreshAsync(_navigationCts.Token);
        }
        catch (OperationCanceledException)
            when (_navigationCts.IsCancellationRequested)
        {
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

        try
        {
            await viewModel.RefreshAsync(
                _navigationCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
            when (_navigationCts?.IsCancellationRequested == true)
        {
        }
    }
}
