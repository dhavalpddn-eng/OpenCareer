using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using OpenCareer.Application.Logbook;
using Windows.Foundation;
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

        RenderRouteTrack();
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
        SelectionChangedEventArgs e)
    {
        _viewModel?.SelectEntry(
            EntryList.SelectedItem as LogbookEntryItemViewModel);
        RenderRouteTrack();
    }

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
            RenderRouteTrack();
        }
        catch (OperationCanceledException)
        {
            // A newer filter change or navigation replaced this request.
        }
    }

    private void RouteCanvas_SizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        RenderRouteTrack();

    private void RenderRouteTrack()
    {
        RouteCanvas.Children.Clear();

        IReadOnlyList<ProjectedRouteLeg>? tracks =
            _viewModel?.SelectedEntry?.RouteTracks;

        if (tracks is null ||
            tracks.Sum(static track => track.Points.Count) < 2 ||
            RouteCanvas.ActualWidth <= 0 ||
            RouteCanvas.ActualHeight <= 0)
        {
            RouteTrackEmptyState.Visibility = Visibility.Visible;
            return;
        }

        RouteTrackEmptyState.Visibility = Visibility.Collapsed;

        const double padding = 18;
        double width = Math.Max(1, RouteCanvas.ActualWidth - (padding * 2));
        double height = Math.Max(1, RouteCanvas.ActualHeight - (padding * 2));

        Brush routeBrush = GetBrush("OpenCareerBrassBrush");
        Brush startBrush = GetBrush("OpenCareerSelectionBrush");
        Brush endBrush = GetBrush("OpenCareerShellTextBrush");

        foreach (ProjectedRouteLeg track in tracks)
        {
            if (track.Points.Count == 0)
                continue;

            var line = new Polyline
            {
                Stroke = routeBrush,
                StrokeThickness = 2
            };

            foreach (ProjectedRoutePoint point in track.Points)
            {
                line.Points.Add(
                    new Point(
                        padding + (point.X * width),
                        padding + (point.Y * height)));
            }

            RouteCanvas.Children.Add(line);
        }

        ProjectedRoutePoint start = tracks
            .SelectMany(static track => track.Points)
            .First();
        ProjectedRoutePoint end = tracks
            .SelectMany(static track => track.Points)
            .Last();

        AddMarker(start, startBrush, width, height, padding);
        AddMarker(end, endBrush, width, height, padding);
    }

    private void AddMarker(
        ProjectedRoutePoint point,
        Brush fill,
        double width,
        double height,
        double padding)
    {
        const double diameter = 9;
        var marker = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = fill
        };

        Canvas.SetLeft(
            marker,
            padding + (point.X * width) - (diameter / 2));
        Canvas.SetTop(
            marker,
            padding + (point.Y * height) - (diameter / 2));
        RouteCanvas.Children.Add(marker);
    }

    private static Brush GetBrush(string key) =>
        Microsoft.UI.Xaml.Application.Current.Resources[key] as Brush ??
        new SolidColorBrush(Microsoft.UI.Colors.White);

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
