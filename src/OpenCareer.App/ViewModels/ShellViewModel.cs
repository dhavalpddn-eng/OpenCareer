using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenCareer.App.ViewModels;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private string _connectionStatus = "Waiting for MSFS 2024";
    private string _connectionDetail = "OpenCareer will connect automatically when the simulator is available.";
    private string _aircraftStatus = "No aircraft connected";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetField(ref _connectionStatus, value);
    }

    public string ConnectionDetail
    {
        get => _connectionDetail;
        private set => SetField(ref _connectionDetail, value);
    }

    public string AircraftStatus
    {
        get => _aircraftStatus;
        private set => SetField(ref _aircraftStatus, value);
    }

    public bool IsSimulatorConnected { get; private set; }

    public void SetWaitingForSimulator()
    {
        IsSimulatorConnected = false;
        ConnectionStatus = "Waiting for MSFS 2024";
        ConnectionDetail = "OpenCareer will connect automatically when the simulator is available.";
        AircraftStatus = "No aircraft connected";
        OnPropertyChanged(nameof(IsSimulatorConnected));
    }

    public void SetConnected(string? aircraftTitle)
    {
        IsSimulatorConnected = true;
        ConnectionStatus = "Connected to MSFS 2024";
        ConnectionDetail = "Simulator telemetry connection is active.";
        AircraftStatus = string.IsNullOrWhiteSpace(aircraftTitle) ? "Aircraft detected" : aircraftTitle;
        OnPropertyChanged(nameof(IsSimulatorConnected));
    }

    public void SetDisconnected(string? reason = null)
    {
        IsSimulatorConnected = false;
        ConnectionStatus = "Disconnected";
        ConnectionDetail = string.IsNullOrWhiteSpace(reason)
            ? "MSFS 2024 is not currently connected. OpenCareer will keep trying."
            : reason;
        AircraftStatus = "No aircraft connected";
        OnPropertyChanged(nameof(IsSimulatorConnected));
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
