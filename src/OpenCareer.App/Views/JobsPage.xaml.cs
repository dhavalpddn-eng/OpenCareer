using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;

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
}
