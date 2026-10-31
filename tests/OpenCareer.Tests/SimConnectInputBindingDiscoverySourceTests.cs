using OpenCareer.Application.Simulator;
using OpenCareer.SimConnect;

namespace OpenCareer.Tests;

public sealed class SimConnectInputBindingDiscoverySourceTests
{
    [Theory]
    [InlineData(SimulatorConnectionState.Connected, "MSFS is connected.")]
    [InlineData(SimulatorConnectionState.WaitingForSimulator, "MSFS is not connected.")]
    public void SupportedApiBoundaryFailsClosedWithoutProfileAssignments(
        SimulatorConnectionState connectionState,
        string expectedConnectionDetail)
    {
        var connection = new TestConnection
        {
            Current = new SimulatorConnectionSnapshot(connectionState)
        };
        var source = new SimConnectInputBindingDiscoverySource(connection);
        InputBindingActionIdentity[] actions =
        [
            new("engine-start", "Engine start"),
            new("parking-brake", "Parking brake")
        ];

        InputBindingDiscoverySnapshot snapshot = source.Read(actions);

        Assert.Equal(InputBindingState.Unavailable, snapshot.State);
        Assert.Contains(expectedConnectionDetail, snapshot.Detail, StringComparison.Ordinal);
        Assert.Contains("does not expose", snapshot.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(actions, snapshot.Bindings.Select(binding => binding.Action));
        Assert.All(snapshot.Bindings, binding =>
        {
            Assert.Equal(InputBindingState.Unavailable, binding.State);
            Assert.Equal("UNAVAILABLE", binding.BindingDisplayText);
            Assert.Null(binding.Device);
            Assert.Null(binding.Profile);
        });
    }

    [Fact]
    public void BindingStatesDistinguishUnboundFromUnknownAndUnavailable()
    {
        Assert.NotEqual(InputBindingState.Unbound, InputBindingState.Unknown);
        Assert.NotEqual(InputBindingState.Unbound, InputBindingState.Unavailable);
        Assert.NotEqual(InputBindingState.Unknown, InputBindingState.Unavailable);
    }

    [Fact]
    public void InvalidActionIdentityFailsClosed()
    {
        var source = new SimConnectInputBindingDiscoverySource(new TestConnection());

        Assert.Throws<ArgumentException>(() =>
            source.Read([new InputBindingActionIdentity(" ", "Invalid")]));
    }

    private sealed class TestConnection : ISimulatorConnection
    {
        public SimulatorConnectionSnapshot Current { get; init; } =
            new(SimulatorConnectionState.Disconnected);

        public void Start()
        {
        }

        public Task StopAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
