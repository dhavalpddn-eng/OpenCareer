using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Careers;

namespace OpenCareer.App.Views;

public sealed partial class JobsPage : Page
{
    private CancellationTokenSource? _navigationCts;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public JobsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(
        NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        StopAircraftRefresh();

        if (e.Parameter is not JobsViewModel viewModel)
            return;

        _navigationCts = new CancellationTokenSource();
        CancellationToken token = _navigationCts.Token;
        DataContext = viewModel;

        try
        {
            // Preserve the initial navigation refresh, even when options already exist.
            await RefreshJobsAsync(viewModel, token);

            while (!token.IsCancellationRequested
                && viewModel.AircraftOptions.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                token.ThrowIfCancellationRequested();

                if (viewModel.AircraftOptions.Count != 0)
                    break;

                await RefreshJobsAsync(viewModel, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    protected override void OnNavigatedFrom(
        NavigationEventArgs e)
    {
        StopAircraftRefresh();
        base.OnNavigatedFrom(e);
    }

    private async Task RefreshJobsAsync(
        JobsViewModel viewModel,
        CancellationToken token)
    {
        // Keep the gate across navigation: a cancelled refresh may still be unwinding.
        await _refreshGate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            await viewModel.RefreshAsync(token);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void StopAircraftRefresh()
    {
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = null;
    }

    private async void AircraftSelection_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (DataContext is not JobsViewModel viewModel
            || sender is not ComboBox comboBox)
        {
            return;
        }

        string? aircraftId =
            (comboBox.SelectedItem as CareerJobAircraftOption)
                ?.AircraftId;

        await viewModel
            .SelectAircraftAsync(
                aircraftId,
                _navigationCts?.Token
                    ?? CancellationToken.None);
    }

    private async void AcceptStartFlight_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not JobsViewModel viewModel
            || sender is not Button button
            || button.DataContext
                is not JobOfferItemViewModel offer)
        {
            return;
        }

        await viewModel
            .StartOfferAsync(
                offer.OfferId,
                _navigationCts?.Token
                    ?? CancellationToken.None);
    }
}
