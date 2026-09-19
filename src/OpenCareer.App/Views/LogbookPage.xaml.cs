using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;

namespace OpenCareer.App.Views;

public sealed partial class LogbookPage : Page
{
    private LogbookViewModel? _viewModel;

    public LogbookPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not LogbookViewModel viewModel)
            return;

        _viewModel = viewModel;
        DataContext = viewModel;
        await viewModel.RefreshAsync();

        if (viewModel.SelectedEntry is not null)
            EntryList.SelectedItem = viewModel.SelectedEntry;
    }

    private void EntryList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        _viewModel?.SelectEntry(
            EntryList.SelectedItem as LogbookEntryItemViewModel);
}
