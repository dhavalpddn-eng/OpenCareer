using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Careers;

namespace OpenCareer.App.Views;

public sealed partial class JobsPage : Page
{
    private CancellationTokenSource? _navigationCts;

    public JobsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(
        NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not JobsViewModel viewModel)
            return;

        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts =
            new CancellationTokenSource();

        DataContext =
            viewModel;

        try
        {
            await viewModel
                .RefreshAsync(
                    _navigationCts.Token);
        }
        catch (OperationCanceledException)
            when (_navigationCts.IsCancellationRequested)
        {
        }
    }

    protected override void OnNavigatedFrom(
        NavigationEventArgs e)
    {
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts =
            null;

        base.OnNavigatedFrom(e);
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
