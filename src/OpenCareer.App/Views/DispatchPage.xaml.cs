using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenCareer.App.ViewModels;

namespace OpenCareer.App.Views;

public sealed partial class DispatchPage : Page
{
    private CancellationTokenSource? _navigationCts;

    public DispatchPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not DispatchViewModel viewModel)
            return;

        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = new();
        DataContext = viewModel;

        try
        {
            await viewModel.InitializeAsync(_navigationCts.Token);
            AircraftPicker.SelectedValue = viewModel.SelectedAircraftId;
            OriginIcaoBox.Text = viewModel.AcceptedOriginIcao ?? string.Empty;
            DestinationIcaoBox.Text = viewModel.AcceptedDestinationIcao ?? string.Empty;
            PayloadPoundsBox.Text = viewModel.AcceptedPayloadPoundsText ?? string.Empty;
            TotalRouteDistanceBox.Text = viewModel.AcceptedRouteDistanceText ?? string.Empty;
        }
        catch (OperationCanceledException)
            when (_navigationCts.IsCancellationRequested)
        {
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = null;
        base.OnNavigatedFrom(e);
    }

    private async void AircraftSelection_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (DataContext is not DispatchViewModel viewModel)
            return;

        try
        {
            await viewModel.SetSelectedAircraftAsync(
                AircraftPicker.SelectedValue?.ToString(),
                _navigationCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
            when (_navigationCts?.IsCancellationRequested == true)
        {
        }
    }

    private void CruiseProfileSelection_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (DataContext is DispatchViewModel viewModel)
        {
            viewModel.SetSelectedCruiseProfile(
                CruiseProfilePicker.SelectedValue is int index
                    ? index
                    : null);
        }
    }

    private async void EvaluatePlan_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DispatchViewModel viewModel)
            return;

        try
        {
            await viewModel.EvaluateAsync(
                ReadInput(),
                _navigationCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
            when (_navigationCts?.IsCancellationRequested == true)
        {
        }
    }

    private async void PreparePlan_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DispatchViewModel viewModel)
            return;

        try
        {
            await viewModel.PrepareAsync(
                ReadInput(),
                _navigationCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
            when (_navigationCts?.IsCancellationRequested == true)
        {
        }
    }

    private async void CancelAcceptedJob_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DispatchViewModel viewModel
            || !viewModel.CanCancelAcceptedJob)
        {
            return;
        }

        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Cancel accepted job?",
            Content = viewModel.CancelAcceptedJobDialogText,
            PrimaryButtonText = "Cancel Job",
            CloseButtonText = "Keep Job",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            await viewModel.CancelAcceptedJobAsync(
                _navigationCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
            when (_navigationCts?.IsCancellationRequested == true)
        {
        }
    }

    private DispatchPlanningFormInput ReadInput() =>
        new(
            OriginIcaoBox.Text,
            DestinationIcaoBox.Text,
            PayloadPoundsBox.Text,
            TotalRouteDistanceBox.Text,
            CruiseSegmentDistanceBox.Text,
            CruiseWeightBox.Text,
            IsaDeviationBox.Text,
            PressureAltitudeBox.Text,
            ClimbTimeBox.Text,
            DescentTimeBox.Text,
            ClimbFuelBox.Text,
            DescentFuelBox.Text,
            ContingencyPercentBox.Text,
            FinalReserveMinutesBox.Text,
            AdditionalReserveFuelBox.Text,
            RangeSafetyMarginBox.Text,
            RunwaySafetyMarginBox.Text,
            ApplyWeatherLimitsCheckBox.IsChecked == true,
            MaximumCrosswindBox.Text,
            MaximumTailwindBox.Text,
            MaximumDensityAltitudeBox.Text,
            MinimumVisibilityBox.Text,
            MinimumCeilingBox.Text);
}
