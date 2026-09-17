using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenCareer.Application.Simulator;

namespace OpenCareer.App.ViewModels;

public sealed class ShellViewModel(ISimulatorConnection connection) : INotifyPropertyChanged
{
    private SimulatorConnectionSnapshot? _lastSnapshot;
    private string _connectionStatus = "Waiting for MSFS 2024";
    private string _connectionDetail = "OpenCareer will connect automatically when the simulator is available.";
    private string _aircraftStatus = "No aircraft connected";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ConnectionStatus => _connectionStatus;
    public string ConnectionDetail => _connectionDetail;
    public string AircraftStatus => _aircraftStatus;
    public bool IsSimulatorConnected { get; private set; }

    // The shell calls this on its dispatcher. Native callbacks never touch observable UI state.
    public void RefreshConnectionStatus()
    {
        var snapshot = connection.Current;
        if (snapshot == _lastSnapshot)
            return;
        _lastSnapshot = snapshot;

        bool connected = snapshot.State == SimulatorConnectionState.Connected;
        if (IsSimulatorConnected != connected)
        {
            IsSimulatorConnected = connected;
            OnPropertyChanged(nameof(IsSimulatorConnected));
        }

        string status = snapshot.State switch
        {
            SimulatorConnectionState.WaitingForSimulator => "Waiting for MSFS 2024",
            SimulatorConnectionState.Connecting => "Connecting to simulator",
            SimulatorConnectionState.Connected => "Simulator connected",
            SimulatorConnectionState.Reconnecting => "Reconnecting to simulator",
            SimulatorConnectionState.Unavailable => "Simulator connection unavailable",
            SimulatorConnectionState.Faulted => "Connection stopped",
            _ => "Disconnected"
        };
        string detail = snapshot.Issue switch
        {
            SimulatorConnectionIssue.RuntimeMissing => "The simulator connection component is missing from this OpenCareer build.",
            SimulatorConnectionIssue.RuntimeIncompatible => "The simulator connection component is incompatible with this OpenCareer build.",
            SimulatorConnectionIssue.VersionMismatch => "The simulator and its connection component use incompatible versions.",
            SimulatorConnectionIssue.UnsupportedPlatform => "Simulator connection requires Windows x64.",
            SimulatorConnectionIssue.UnexpectedError => "A connection error occurred. Restart OpenCareer to try again.",
            SimulatorConnectionIssue.HandshakeTimeout => "The simulator did not acknowledge the connection. OpenCareer will try again.",
            SimulatorConnectionIssue.ResponseTimeout => "The simulator stopped responding. OpenCareer will reconnect automatically.",
            SimulatorConnectionIssue.InvalidResponse or SimulatorConnectionIssue.SimulatorError => "The simulator reported a connection error. OpenCareer will try again.",
            _ => snapshot.State switch
            {
                SimulatorConnectionState.Connected => "Connection established. Flight tracking is not available in this build.",
                SimulatorConnectionState.Connecting => "Waiting for the simulator to acknowledge the connection.",
                SimulatorConnectionState.Reconnecting => "The simulator connection was lost. OpenCareer will reconnect automatically.",
                SimulatorConnectionState.Disconnected => "Simulator connection is stopped.",
                _ => "OpenCareer will connect automatically when MSFS 2024 is available."
            }
        };

        SetField(ref _connectionStatus, status, nameof(ConnectionStatus));
        SetField(ref _connectionDetail, detail, nameof(ConnectionDetail));
        SetField(ref _aircraftStatus, connected ? "Aircraft data unavailable" : "No aircraft connected", nameof(AircraftStatus));
    }

    private void SetField(ref string field, string value, string propertyName)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
            return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
