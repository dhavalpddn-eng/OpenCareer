using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;

namespace OpenCareer.App.Views;

public sealed partial class CareerPage : Page
{
    private CancellationTokenSource? _navigationCts;

    public CareerPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(
        NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not CareerViewModel viewModel)
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
                .InitializeAsync(
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

    private void HomeAirportTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (DataContext is not CareerViewModel viewModel
            || sender is not TextBox textBox)
        {
            return;
        }

        viewModel.HomeAirportIcao =
            textBox.Text;
    }

    private async void StartCareer_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not CareerViewModel viewModel)
            return;

        viewModel.HomeAirportIcao =
            HomeAirportTextBox.Text;

        try
        {
            await viewModel
                .StartCareerAsync(
                    _navigationCts?.Token
                        ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
            when (_navigationCts?.IsCancellationRequested == true)
        {
        }
    }
}
