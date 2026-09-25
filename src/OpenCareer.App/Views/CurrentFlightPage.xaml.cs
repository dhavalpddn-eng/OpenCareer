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
        _navigationCts = new CancellationTokenSource();

        DataContext = context.Shell;
        _jobs = context.Jobs;

        await context.Shell.RefreshCareerCompletionActionAsync(
            _navigationCts.Token);

        await context.Shell.RefreshCareerAbandonActionAsync(
            _navigationCts.Token);
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

        CancellationToken cancellationToken =
            _navigationCts?.Token
            ?? CancellationToken.None;

        await viewModel.CompleteCareerFlightAsync(
            cancellationToken);
    }

    private async void AbandonCurrentFlight_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel)
            return;

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

        if (answer != ContentDialogResult.Primary)
            return;

        CancellationToken cancellationToken =
            _navigationCts?.Token
            ?? CancellationToken.None;

        bool abandoned =
            await viewModel.AbandonCurrentFlightAsync(
                cancellationToken);

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
}
