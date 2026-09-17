using OpenCareer.App.ViewModels;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public void RefreshClearsConnectionAndDoesNotClaimAircraftWithoutTelemetry()
    {
        var connection = new TestConnection { Current = new(SimulatorConnectionState.Connected) };
        var telemetry = new TestTelemetrySource();
        var viewModel = new ShellViewModel(connection, telemetry);

        viewModel.RefreshConnectionStatus();

        Assert.True(viewModel.IsSimulatorConnected);
        Assert.False(viewModel.HasTelemetry);
        Assert.Equal("Waiting for aircraft telemetry", viewModel.AircraftStatus);
        Assert.Contains("telemetry", viewModel.ConnectionDetail);

        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        viewModel.RefreshConnectionStatus();
        Assert.Empty(changed);

        connection.Current = new(SimulatorConnectionState.Reconnecting, SimulatorConnectionIssue.ConnectionLost);
        viewModel.RefreshConnectionStatus();
        Assert.False(viewModel.IsSimulatorConnected);
        Assert.False(viewModel.HasTelemetry);
        Assert.Equal("No aircraft connected", viewModel.AircraftStatus);
        Assert.Contains(nameof(ShellViewModel.IsSimulatorConnected), changed);
    }

    [Fact]
    public void TelemetryRefreshesWhileConnectionSnapshotIsUnchanged()
    {
        var connectionSnapshot = new SimulatorConnectionSnapshot(SimulatorConnectionState.Connected);
        var connection = new TestConnection { Current = connectionSnapshot };
        var telemetry = new TestTelemetrySource();
        var viewModel = new ShellViewModel(connection, telemetry);
        viewModel.RefreshConnectionStatus();

        telemetry.Latest = Snapshot(1_000, 110, -500, paused: false);
        viewModel.RefreshConnectionStatus();

        Assert.Same(connectionSnapshot, connection.Current);
        Assert.True(viewModel.HasTelemetry);
        Assert.Equal("Live aircraft telemetry", viewModel.AircraftStatus);
        Assert.Equal("1000 ft MSL / 450 ft AGL", viewModel.AltitudeSummary);
        Assert.Equal("110 kt IAS / 120 kt GS", viewModel.SpeedSummary);
        Assert.Equal("-500 ft/min", viewModel.VerticalSpeedSummary);
        Assert.Equal("090° true", viewModel.HeadingSummary);
        Assert.Equal("Airborne", viewModel.AircraftStateSummary);

        telemetry.Latest = Snapshot(1_050, 112, 0, paused: true);
        viewModel.RefreshConnectionStatus();
        Assert.Equal("Paused", viewModel.AircraftStateSummary);
        Assert.Equal("1050 ft MSL / 450 ft AGL", viewModel.AltitudeSummary);
    }

    [Fact]
    public void MissingRuntimeIsDistinguishedFromWaitingForSimulator()
    {
        var connection = new TestConnection { Current = new(SimulatorConnectionState.WaitingForSimulator) };
        var viewModel = new ShellViewModel(connection, new TestTelemetrySource());
        viewModel.RefreshConnectionStatus();
        Assert.Equal("Waiting for MSFS 2024", viewModel.ConnectionStatus);

        connection.Current = new(SimulatorConnectionState.Unavailable, SimulatorConnectionIssue.RuntimeMissing);
        viewModel.RefreshConnectionStatus();

        Assert.Equal("Simulator connection unavailable", viewModel.ConnectionStatus);
        Assert.Contains("missing", viewModel.ConnectionDetail);
        Assert.False(viewModel.IsSimulatorConnected);
    }

    private static AircraftTelemetrySnapshot Snapshot(
        double altitudeMsl,
        double indicatedAirspeed,
        double verticalSpeed,
        bool paused) =>
        new(
            DateTimeOffset.UtcNow,
            43.23,
            -75.40,
            altitudeMsl,
            450,
            indicatedAirspeed,
            120,
            verticalSpeed,
            90,
            2,
            -1,
            1.02,
            false,
            false,
            1,
            300,
            400,
            10,
            true,
            paused,
            false);

    private sealed class TestConnection : ISimulatorConnection
    {
        public SimulatorConnectionSnapshot Current { get; set; } = new(SimulatorConnectionState.Disconnected);
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestTelemetrySource : ISimulatorTelemetrySource
    {
        public AircraftTelemetrySnapshot? Latest { get; set; }
    }
}
