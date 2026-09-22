using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;

namespace OpenCareer.App.Views;

public sealed partial class CurrentFlightPage : Page
{
    private CancellationTokenSource? _navigationCts;

    public CurrentFlightPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not ShellViewModel viewModel)
            return;

        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = new CancellationTokenSource();

        DataContext = viewModel;

        await viewModel.RefreshCareerCompletionActionAsync(
            _navigationCts.Token);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = null;

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
}
