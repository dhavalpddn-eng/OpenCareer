using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;

namespace OpenCareer.App.Views;

public sealed partial class MilitaryGovernmentPage : Page
{
    private readonly DispatcherQueueTimer _refreshTimer;
    private MilitaryGovernmentViewModel? _viewModel;
    private bool _synchronizingCompletedOperationSelection;

    public MilitaryGovernmentPage()
    {
        InitializeComponent();

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        _refreshTimer.Tick += OnRefreshTimerTick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not MilitaryGovernmentViewModel viewModel)
            return;

        _viewModel = viewModel;
        DataContext = viewModel;
        RefreshPresentation();
        _refreshTimer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _refreshTimer.Stop();
        base.OnNavigatedFrom(e);
    }

    private void OnRefreshTimerTick(
        DispatcherQueueTimer sender,
        object args) =>
        RefreshPresentation();

    private void OperationalMapCanvas_SizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        RenderOperationalMap();

    private void RefreshPresentation()
    {
        _viewModel?.Refresh();
        SynchronizeCompletedOperationSelection();
        UpdateCompletedOperationDetailVisibility();
        RenderOperationalMap();
    }

    private void CompletedOperationsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_synchronizingCompletedOperationSelection
            || _viewModel is null)
        {
            return;
        }

        if (CompletedOperationsList.SelectedItem
            is MilitaryCompletedOperationItemViewModel selected)
        {
            _viewModel.SelectCompletedOperation(selected.CampaignId);
        }
        else
        {
            _viewModel.ClearCompletedOperationSelection();
        }

        UpdateCompletedOperationDetailVisibility();
    }

    private void SynchronizeCompletedOperationSelection()
    {
        if (_viewModel is null)
            return;

        MilitaryCompletedOperationItemViewModel? selected =
            _viewModel.SelectedCompletedOperation;

        if (ReferenceEquals(
            CompletedOperationsList.SelectedItem,
            selected))
        {
            return;
        }

        _synchronizingCompletedOperationSelection = true;
        try
        {
            CompletedOperationsList.SelectedItem = selected;
        }
        finally
        {
            _synchronizingCompletedOperationSelection = false;
        }
    }

    private void UpdateCompletedOperationDetailVisibility()
    {
        bool hasDetail =
            _viewModel?.SelectedCompletedOperationDetail is not null;

        CompletedOperationEmptyState.Visibility =
            hasDetail
                ? Visibility.Collapsed
                : Visibility.Visible;

        CompletedOperationDetailPanel.Visibility =
            hasDetail
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void RenderOperationalMap()
    {
        OperationalMapCanvas.Children.Clear();

        if (_viewModel is null
            || OperationalMapCanvas.ActualWidth <= 0
            || OperationalMapCanvas.ActualHeight <= 0)
        {
            return;
        }

        foreach (MilitaryOperationalMapMarkerViewModel marker
            in _viewModel.OperationalMapMarkers)
        {
            double size = marker.Kind == MilitaryOperationalMapMarkerKind.Threat
                ? 24
                : 18;

            var text = new TextBlock
            {
                Text = marker.Symbol,
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources[
                    "OpenCareerShellTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var visual = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = MarkerBrush(marker.Kind),
                BorderBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources[
                    "OpenCareerShellTextBrush"],
                BorderThickness = new Thickness(1),
                Child = text
            };

            ToolTipService.SetToolTip(
                visual,
                $"{marker.Label}\n{marker.Detail}");

            AutomationProperties.SetName(
                visual,
                $"{marker.Label}. {marker.Detail}");

            Canvas.SetLeft(
                visual,
                marker.NormalizedX
                    * Math.Max(0, OperationalMapCanvas.ActualWidth - size));

            Canvas.SetTop(
                visual,
                marker.NormalizedY
                    * Math.Max(0, OperationalMapCanvas.ActualHeight - size));

            OperationalMapCanvas.Children.Add(visual);
        }
    }

    private static Brush MarkerBrush(
        MilitaryOperationalMapMarkerKind kind)
    {
        string resourceKey = kind switch
        {
            MilitaryOperationalMapMarkerKind.FriendlyUnit =>
                "OpenCareerSuccessBrush",
            MilitaryOperationalMapMarkerKind.HostileUnit =>
                "OpenCareerDangerBrush",
            MilitaryOperationalMapMarkerKind.NeutralUnit =>
                "OpenCareerDisabledBrush",
            MilitaryOperationalMapMarkerKind.Threat =>
                "OpenCareerWarningBrush",
            MilitaryOperationalMapMarkerKind.SupportRequest =>
                "OpenCareerBrassBrush",
            _ => "OpenCareerDisabledBrush"
        };

        return (Brush)Microsoft.UI.Xaml.Application.Current.Resources[resourceKey];
    }

    private async void AcceptSuccessor_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel is null)
            return;

        await _viewModel.AcceptSuccessorAsync();
    }

    private void DeclineSuccessor_Click(
        object sender,
        RoutedEventArgs e) =>
        _viewModel?.DeclineSuccessor();

    private void ReconsiderSuccessor_Click(
        object sender,
        RoutedEventArgs e) =>
        _viewModel?.ReconsiderSuccessor();
}
