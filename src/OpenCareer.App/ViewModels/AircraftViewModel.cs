using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenCareer.Application.Planning;

namespace OpenCareer.App.ViewModels;

public sealed class AircraftViewModel : INotifyPropertyChanged
{
    private readonly IInstalledAircraftInventorySource _inventory;
    private IReadOnlyList<InstalledAircraftItemViewModel> _aircraft =
        Array.Empty<InstalledAircraftItemViewModel>();
    private string _statusText = "Installed-aircraft inventory has not been loaded.";
    private bool _isRefreshing;

    public AircraftViewModel(IInstalledAircraftInventorySource inventory)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<InstalledAircraftItemViewModel> Aircraft => _aircraft;
    public string StatusText => _statusText;
    public bool IsRefreshing => _isRefreshing;
    public bool CanRefresh => !_isRefreshing;
    public bool HasAircraft => _aircraft.Count > 0;
    public string RefreshButtonText => _isRefreshing ? "Refreshing…" : "Refresh Aircraft";

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_isRefreshing)
            return;

        SetField(ref _isRefreshing, true, nameof(IsRefreshing));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(RefreshButtonText));

        try
        {
            InstalledAircraftInventorySnapshot snapshot = await _inventory
                .ReadAsync(cancellationToken)
                .ConfigureAwait(true);

            SetField(
                ref _aircraft,
                snapshot.Aircraft
                    .Select(static item => new InstalledAircraftItemViewModel(
                        item.CanonicalAircraftId,
                        item.DisplayName,
                        item.InstallationText,
                        item.TypeIdentityText,
                        item.FamilyIdentityText,
                        item.PowerplantClassText,
                        item.PowerplantText,
                        item.CapabilityText,
                        item.MaximumPayloadText,
                        item.MaximumRangeText,
                        item.SeatsText,
                        item.IfrCapabilityText,
                        item.PressurizationText,
                        item.LandingGearText,
                        item.CapabilityEvidenceText,
                        item.FleetStatusText,
                        item.ReservationText,
                        item.AccessText,
                        item.PassengerCapacityText,
                        item.TypicalCruiseText))
                    .ToArray(),
                nameof(Aircraft));
            SetField(ref _statusText, snapshot.Detail, nameof(StatusText));
            OnPropertyChanged(nameof(HasAircraft));
        }
        finally
        {
            SetField(ref _isRefreshing, false, nameof(IsRefreshing));
            OnPropertyChanged(nameof(CanRefresh));
            OnPropertyChanged(nameof(RefreshButtonText));
        }
    }

    private bool SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));
}

public sealed record InstalledAircraftItemViewModel(
    string CanonicalAircraftId,
    string DisplayName,
    string InstallationText,
    string TypeIdentityText,
    string FamilyIdentityText,
    string PowerplantClassText,
    string PowerplantText,
    string CapabilityText,
    string MaximumPayloadText,
    string MaximumRangeText,
    string SeatsText,
    string IfrCapabilityText,
    string PressurizationText,
    string LandingGearText,
    string CapabilityEvidenceText,
    string FleetStatusText,
    string ReservationText,
    string AccessText,
    string PassengerCapacityText,
    string TypicalCruiseText);
