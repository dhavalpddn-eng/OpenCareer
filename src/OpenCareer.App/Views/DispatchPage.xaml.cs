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
        var navigationCts = new CancellationTokenSource();
        _navigationCts = navigationCts;
        DataContext = viewModel;

        try
        {
            await viewModel.InitializeAsync(navigationCts.Token);

            if (!ReferenceEquals(_navigationCts, navigationCts)
                || navigationCts.IsCancellationRequested)
            {
                return;
            }

            AircraftPicker.SelectedValue = viewModel.SelectedAircraftId;
            OriginIcaoBox.Text = viewModel.AcceptedOriginIcao ?? string.Empty;
            DestinationIcaoBox.Text = viewModel.AcceptedDestinationIcao ?? string.Empty;
            PayloadPoundsBox.Text = viewModel.AcceptedPayloadPoundsText ?? string.Empty;
            TotalRouteDistanceBox.Text = viewModel.AcceptedRouteDistanceText ?? string.Empty;
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            viewModel.ReportInitializationFailure(ex);
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

        CancellationTokenSource? navigationCts = _navigationCts;
        if (navigationCts is null || navigationCts.IsCancellationRequested)
            return;

        try
        {
            await viewModel.SetSelectedAircraftAsync(
                AircraftPicker.SelectedValue?.ToString(),
                navigationCts.Token);
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
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

        CancellationTokenSource? navigationCts = _navigationCts;
        if (navigationCts is null || navigationCts.IsCancellationRequested)
            return;

        try
        {
            await viewModel.EvaluateAsync(
                ReadInput(),
                navigationCts.Token);
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
        {
        }
    }

    private async void PreparePlan_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DispatchViewModel viewModel)
            return;

        CancellationTokenSource? navigationCts = _navigationCts;
        if (navigationCts is null || navigationCts.IsCancellationRequested)
            return;

        try
        {
            await viewModel.PrepareAsync(
                ReadInput(),
                navigationCts.Token);
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
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

        CancellationTokenSource? navigationCts = _navigationCts;
        if (navigationCts is null || navigationCts.IsCancellationRequested)
            return;

        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Cancel accepted job?",
            Content = viewModel.CancelAcceptedJobDialogText,
            PrimaryButtonText = "Cancel Job",
            CloseButtonText = "Keep Job",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirmation.ShowAsync() != ContentDialogResult.Primary
            || !ReferenceEquals(_navigationCts, navigationCts)
            || navigationCts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await viewModel.CancelAcceptedJobAsync(
                navigationCts.Token);
        }
        catch (OperationCanceledException)
            when (navigationCts.IsCancellationRequested)
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
