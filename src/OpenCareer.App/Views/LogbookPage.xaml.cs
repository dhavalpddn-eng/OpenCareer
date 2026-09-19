using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.App.Views;

public sealed partial class LogbookPage : Page
{
    private LogbookViewModel? _viewModel;
    private CancellationTokenSource? _filterCancellation;

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

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _filterCancellation?.Cancel();
        _filterCancellation?.Dispose();
        _filterCancellation = null;
        base.OnNavigatedFrom(e);
    }

    private void EntryList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        _viewModel?.SelectEntry(
            EntryList.SelectedItem as LogbookEntryItemViewModel);

    private async void FilterControl_Changed(object sender, object e)
    {
        if (_viewModel is null)
            return;

        _filterCancellation?.Cancel();
        _filterCancellation?.Dispose();
        _filterCancellation = new CancellationTokenSource();
        CancellationToken token = _filterCancellation.Token;

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), token);

            _viewModel.SetFilters(
                SearchBox.Text,
                ParseEnum<LogbookEntryKind>(KindFilter),
                ParseEnum<FlightSafetyOutcome>(SafetyFilter));

            await _viewModel.RefreshAsync(token);
            EntryList.SelectedItem = _viewModel.SelectedEntry;
        }
        catch (OperationCanceledException)
        {
            // A newer filter change or navigation replaced this request.
        }
    }

    private static TEnum? ParseEnum<TEnum>(ComboBox comboBox)
        where TEnum : struct, Enum
    {
        if (comboBox.SelectedItem is not ComboBoxItem item)
            return null;

        string? value = item.Tag?.ToString();
        return Enum.TryParse(value, ignoreCase: true, out TEnum parsed)
            ? parsed
            : null;
    }
}
