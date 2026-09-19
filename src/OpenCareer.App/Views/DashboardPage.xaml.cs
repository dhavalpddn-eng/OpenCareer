using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Dashboard;

namespace OpenCareer.App.Views;

public sealed partial class DashboardPage : Page
{
    private DashboardViewModel? _viewModel;

    public DashboardPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not DashboardViewModel viewModel)
            return;

        _viewModel = viewModel;
        DataContext = viewModel;
        await viewModel.RefreshAsync();
    }

    private void PrimaryAction_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.RequestPrimaryAction();

    private void NavigateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not FrameworkElement element)
            return;

        if (Enum.TryParse(
                element.Tag?.ToString(),
                ignoreCase: true,
                out DashboardActionTarget target))
        {
            _viewModel.RequestNavigation(target);
        }
    }

    private void SocialSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e) =>
        _viewModel?.SetSocialSearch(SocialSearchBox.Text);
}
