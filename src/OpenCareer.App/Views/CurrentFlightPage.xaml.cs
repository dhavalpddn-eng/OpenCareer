using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;

namespace OpenCareer.App.Views;

public sealed partial class CurrentFlightPage : Page
{
    private CancellationTokenSource? _navigationCts;
    private JobsViewModel? _jobs;

    public CurrentFlightPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not CurrentFlightPageContext context)
            return;

        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        var navigationCts = new CancellationTokenSource();
        _navigationCts = navigationCts;

        DataContext = context.Shell;
        _jobs = context.Jobs;

        try
        {
            await context.Shell.RefreshCareerCompletionActionAsync(
                waitForCurrentAction: true,
                cancellationToken: navigationCts.Token);

            await context.Shell.RefreshCareerAbandonActionAsync(
                waitForCurrentAction: true,
                cancellationToken: navigationCts.Token);

            await context.Shell.RefreshManualPostflightAsync(
                navigationCts.Token);
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = null;
        _jobs = null;

        base.OnNavigatedFrom(e);
    }

    private async void CompleteCareerFlight_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel)
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
            bool completed =
                await viewModel.CompleteCareerFlightAsync(
                    cancellationToken);

            if (!ReferenceEquals(_navigationCts, navigationCts)
                || navigationCts.IsCancellationRequested)
            {
                return;
            }

            Exception? jobsRefreshFailure = null;

            if (completed
                && _jobs is { } jobs)
            {
                try
                {
                    await jobs.RefreshAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    jobsRefreshFailure = ex;
                }
            }

            if (!ReferenceEquals(_navigationCts, navigationCts)
                || navigationCts.IsCancellationRequested)
            {
                return;
            }

            await viewModel.RefreshCareerAbandonActionAsync(
                cancellationToken);

            if (jobsRefreshFailure is not null)
            {
                viewModel.ReportCareerCompletionProjectionRefreshFailure(
                    jobsRefreshFailure);
            }
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
    }

    private async void AbandonCurrentFlight_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel)
            return;

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
            Title = "Abandon Current Flight?",
            Content = "This cancels an active flight or discards an interrupted flight, cancels its contract, and releases its aircraft. It grants no payment, experience, reputation, location change, or completed logbook entry.",
            PrimaryButtonText = "Abandon Current Flight",
            CloseButtonText = "Keep Flight",
            DefaultButton = ContentDialogButton.Close
        };

        ContentDialogResult answer =
            await confirmation.ShowAsync();

        if (answer != ContentDialogResult.Primary
            || !ReferenceEquals(_navigationCts, navigationCts)
            || navigationCts.IsCancellationRequested)
        {
            return;
        }

        CancellationToken cancellationToken =
            navigationCts.Token;

        try
        {
            bool abandoned =
                await viewModel.AbandonCurrentFlightAsync(
                    cancellationToken);

            if (!ReferenceEquals(_navigationCts, navigationCts)
                || navigationCts.IsCancellationRequested)
            {
                return;
            }

            JobsViewModel? jobs =
                _jobs;

            Exception? jobsRefreshFailure =
                null;

            if (abandoned
                && jobs is not null)
            {
                try
                {
                    await jobs.RefreshAsync(
                        cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    jobsRefreshFailure = ex;
                }
            }

            if (!ReferenceEquals(_navigationCts, navigationCts)
                || navigationCts.IsCancellationRequested)
            {
                return;
            }

            await viewModel.RefreshCareerCompletionActionAsync(
                cancellationToken);

            await viewModel.RefreshCareerAbandonActionAsync(
                cancellationToken);

            if (jobsRefreshFailure is not null)
            {
                viewModel.ReportCareerAbandonProjectionRefreshFailure(
                    jobsRefreshFailure);
            }
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
    }

    private async void LogManualFlight_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel)
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
            await viewModel.LogManualFlightAsync(
                navigationCts.Token);
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
    }

    private async void DiscardManualFlight_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel)
            return;

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
            Title = "Discard Contractless Flight?",
            Content = "If this practice or training flight is active, OpenCareer cancels it first, then removes its checkpoint. No Logbook entry, experience, qualification, or other progression credit is granted. This cannot be undone.",
            PrimaryButtonText = "Discard Flight",
            CloseButtonText = "Keep Flight",
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
            await viewModel.DiscardManualFlightAsync(
                navigationCts.Token);
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
    }
}
