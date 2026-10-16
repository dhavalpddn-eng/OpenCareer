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

    private void AircraftSelection_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (DataContext is DispatchViewModel viewModel)
            viewModel.SetSelectedAircraft(AircraftPicker.SelectedValue?.ToString());
    }

    private async void EvaluatePlan_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DispatchViewModel viewModel)
            return;

        var input = new DispatchPlanningFormInput(
            OriginIcaoBox.Text,
            DestinationIcaoBox.Text,
            PayloadPoundsBox.Text,
            TotalRouteDistanceBox.Text,
            CruiseSegmentDistanceBox.Text,
            ProfileIndexBox.Text,
            FuelTypeIndexBox.Text,
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
            RunwaySafetyMarginBox.Text);

        try
        {
            await viewModel.EvaluateAsync(
                input,
                _navigationCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
            when (_navigationCts?.IsCancellationRequested == true)
        {
        }
    }
}
