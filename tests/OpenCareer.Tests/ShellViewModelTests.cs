using OpenCareer.App.ViewModels;
using OpenCareer.Application.Simulator;

namespace OpenCareer.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public void RefreshClearsConnectionAndDoesNotClaimAircraftOrTelemetry()
    {
        var connection = new TestConnection { Current = new(SimulatorConnectionState.Connected) };
        var viewModel = new ShellViewModel(connection);
        viewModel.RefreshConnectionStatus();
        Assert.True(viewModel.IsSimulatorConnected);
        Assert.Equal("Aircraft data unavailable", viewModel.AircraftStatus);
        Assert.Contains("not available", viewModel.ConnectionDetail);

        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        viewModel.RefreshConnectionStatus();
        Assert.Empty(changed);

        connection.Current = new(SimulatorConnectionState.Reconnecting, SimulatorConnectionIssue.ConnectionLost);
        viewModel.RefreshConnectionStatus();
        Assert.False(viewModel.IsSimulatorConnected);
        Assert.Equal("No aircraft connected", viewModel.AircraftStatus);
        Assert.Contains(nameof(ShellViewModel.IsSimulatorConnected), changed);
    }

    [Fact]
    public void MissingRuntimeIsDistinguishedFromWaitingForSimulator()
    {
        var connection = new TestConnection { Current = new(SimulatorConnectionState.WaitingForSimulator) };
        var viewModel = new ShellViewModel(connection);
        viewModel.RefreshConnectionStatus();
        Assert.Equal("Waiting for MSFS 2024", viewModel.ConnectionStatus);
        connection.Current = new(SimulatorConnectionState.Unavailable, SimulatorConnectionIssue.RuntimeMissing);
        viewModel.RefreshConnectionStatus();
        Assert.Equal("Simulator connection unavailable", viewModel.ConnectionStatus);
        Assert.Contains("missing", viewModel.ConnectionDetail);
        Assert.False(viewModel.IsSimulatorConnected);
    }

    private sealed class TestConnection : ISimulatorConnection
    {
        public SimulatorConnectionSnapshot Current { get; set; } = new(SimulatorConnectionState.Disconnected);
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
