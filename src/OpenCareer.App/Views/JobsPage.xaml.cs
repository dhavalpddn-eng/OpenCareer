using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Careers;

namespace OpenCareer.App.Views;

public sealed partial class JobsPage : Page
{
    private CancellationTokenSource? _navigationCts;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _refreshTimer;
    private bool _refreshingAircraft;
    private bool _synchronizingAircraftSelection;

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
        var navigationCts =
            new CancellationTokenSource();
        _navigationCts =
            navigationCts;

        DataContext =
            viewModel;

        try
        {
            await viewModel
                .RefreshAsync(
                    navigationCts.Token);

            if (!ReferenceEquals(_navigationCts, navigationCts)
                || navigationCts.IsCancellationRequested)
            {
                return;
            }

            SynchronizeAircraftSelection(viewModel);
            StartAircraftRefreshTimer(
                viewModel);
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await viewModel.ReportRefreshFailureAsync(ex);
        }
    }

    protected override void OnNavigatedFrom(
        NavigationEventArgs e)
    {
        StopAircraftRefreshTimer();

        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts =
            null;

        base.OnNavigatedFrom(e);
    }

    private void StartAircraftRefreshTimer(
        JobsViewModel viewModel)
    {
        StopAircraftRefreshTimer();

        _refreshTimer =
            DispatcherQueue.CreateTimer();

        _refreshTimer.Interval =
            TimeSpan.FromSeconds(2);

        _refreshTimer.Tick +=
            async (_, _) =>
            {
                CancellationTokenSource? navigationCts =
                    _navigationCts;

                if (_refreshingAircraft
                    || navigationCts is null
                    || navigationCts.IsCancellationRequested)
                {
                    return;
                }

                _refreshingAircraft = true;

                try
                {
                    await viewModel
                        .RefreshAircraftAndReadinessAsync(
                            navigationCts.Token);

                    if (!ReferenceEquals(_navigationCts, navigationCts)
                        || navigationCts.IsCancellationRequested)
                    {
                        return;
                    }

                    SynchronizeAircraftSelection(viewModel);
                }
                catch (OperationCanceledException)
                    when (navigationCts.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    StopAircraftRefreshTimer();
                    await viewModel.ReportRefreshFailureAsync(ex);
                }
                finally
                {
                    _refreshingAircraft = false;
                }
            };

        _refreshTimer.Start();
    }

    private void StopAircraftRefreshTimer()
    {
        if (_refreshTimer is null)
            return;

        _refreshTimer.Stop();
        _refreshTimer =
            null;
        _refreshingAircraft =
            false;
    }

    private async void AircraftSelection_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_synchronizingAircraftSelection
            || DataContext is not JobsViewModel viewModel
            || sender is not ComboBox comboBox)
        {
            return;
        }

        CancellationTokenSource? navigationCts =
            _navigationCts;

        if (navigationCts is null
            || navigationCts.IsCancellationRequested)
        {
            return;
        }

        string? aircraftId =
            (comboBox.SelectedItem as CareerJobAircraftOption)
                ?.AircraftId;

        try
        {
            await viewModel
                .SelectAircraftAsync(
                    aircraftId,
                    navigationCts.Token);

            if (ReferenceEquals(_navigationCts, navigationCts)
                && !navigationCts.IsCancellationRequested)
            {
                SynchronizeAircraftSelection(viewModel);
            }
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
    }

    private void SynchronizeAircraftSelection(JobsViewModel viewModel)
    {
        if (AircraftPicker.IsDropDownOpen)
            return;

        // Page event continuations run on the UI thread. WinUI can clear SelectedItem
        // when ItemsSource changes even while the retained ID/binding remains unchanged.
        _synchronizingAircraftSelection = true;
        try
        {
            AircraftPicker.SelectedItem = viewModel.SelectedAircraftOption;
        }
        finally
        {
            _synchronizingAircraftSelection = false;
        }
    }

    private async void RefreshJobs_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not JobsViewModel viewModel)
            return;

        CancellationTokenSource? navigationCts =
            _navigationCts;

        if (navigationCts is null
            || navigationCts.IsCancellationRequested)
        {
            return;
        }

        CancellationToken cancellationToken =
            navigationCts.Token;

        try
        {
            await viewModel.RefreshAsync(cancellationToken);

            if (!ReferenceEquals(_navigationCts, navigationCts)
                || navigationCts.IsCancellationRequested)
            {
                return;
            }

            SynchronizeAircraftSelection(viewModel);
            StartAircraftRefreshTimer(viewModel);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await viewModel.ReportRefreshFailureAsync(ex);
        }
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

        CancellationTokenSource? navigationCts =
            _navigationCts;

        if (navigationCts is null
            || navigationCts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await viewModel
                .StartOfferAsync(
                    offer.OfferId,
                    navigationCts.Token);

            if (ReferenceEquals(_navigationCts, navigationCts)
                && !navigationCts.IsCancellationRequested)
            {
                SynchronizeAircraftSelection(viewModel);
            }
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
    }

    private async void GenerateDevelopmentFlight_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not JobsViewModel viewModel)
            return;

        CancellationTokenSource? navigationCts =
            _navigationCts;

        if (navigationCts is null
            || navigationCts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await viewModel
                .GenerateDevelopmentFlightAsync(
                    PositionTestPilot.IsChecked == true,
                    navigationCts.Token);

            if (ReferenceEquals(_navigationCts, navigationCts)
                && !navigationCts.IsCancellationRequested)
            {
                SynchronizeAircraftSelection(viewModel);
            }
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
    }

    private async void CancelAcceptedJobs_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not JobsViewModel viewModel
            || !viewModel.CanCancelAcceptedJobs)
        {
            return;
        }

        CancellationTokenSource? navigationCts =
            _navigationCts;

        if (navigationCts is null
            || navigationCts.IsCancellationRequested)
        {
            return;
        }

        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Cancel accepted job?",
            Content = viewModel.CancelAcceptedJobsDialogText,
            PrimaryButtonText = "Cancel Job",
            CloseButtonText = "Keep Job",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirmation.ShowAsync() != ContentDialogResult.Primary
            || !ReferenceEquals(_navigationCts, navigationCts)
            || navigationCts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await viewModel
                .CancelAcceptedJobsAsync(
                    navigationCts.Token);

            if (ReferenceEquals(_navigationCts, navigationCts)
                && !navigationCts.IsCancellationRequested)
            {
                SynchronizeAircraftSelection(viewModel);
            }
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
    }

    private void OpenCurrentFlight_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is JobsViewModel viewModel)
            viewModel.OpenCurrentFlight();
    }
}
