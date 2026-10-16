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
                        item.PowerplantText,
                        item.CapabilityText,
                        item.AccessText))
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
    string PowerplantText,
    string CapabilityText,
    string AccessText);
