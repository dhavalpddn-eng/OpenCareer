using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;

namespace OpenCareer.App.Views;

public sealed partial class FinancesPage : Page
{
    public FinancesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(
        NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not FinancesViewModel viewModel)
            return;

        DataContext = viewModel;
        _ = viewModel.RefreshAsync();
    }
}
