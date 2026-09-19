using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Simulator;
using OpenCareer.SimConnect;

namespace OpenCareer.Tests;

public sealed class SimConnectConnectionTests
{
    [Fact]
    public async Task MissingSimulatorRetriesThenConnectsOnlyAfterAcknowledgement()
    {
        var api = new SimConnectTestTransport();
        api.OpenResults.Enqueue(SimConnectTestTransport.Failure);
        api.OpenResults.Enqueue(SimConnectTestTransport.Failure);
        await using var connection = Create(api);
        connection.Start();
        await Until(() => api.Attempts == 3 && connection.Current.State == SimulatorConnectionState.Connecting);
        Assert.Null(connection.Current.Simulator);
        api.Enqueue(SimConnectPackets.Open());
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        Assert.Equal("Microsoft Flight Simulator 2024", connection.Current.Simulator!.Name);
        await connection.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, api.Closed);
        Assert.Equal(SimulatorConnectionState.Disconnected, connection.Current.State);
        Assert.Null(connection.Current.Simulator);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task QuitOrTransportLossClosesBeforeReconnecting(bool gracefulQuit)
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());
        await using var connection = Create(api);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        if (gracefulQuit)
            api.Enqueue(SimConnectPackets.Header(3));
        else
            api.Enqueue(result: unchecked((int)0xC000013C));
        await Until(() => api.Attempts == 2 && connection.Current.State == SimulatorConnectionState.Connecting);
        Assert.Equal(1, api.Closed);
        Assert.Null(connection.Current.Simulator);
        api.Enqueue(SimConnectPackets.Open("Reopened simulator"));
        await Until(() => connection.Current.Simulator?.Name == "Reopened simulator");
        await connection.StopAsync();
        Assert.Equal(2, api.Closed);
        Assert.False(api.OpenedBeforeClose);
        Assert.False(api.OverlapDetected);
        Assert.Single(api.ThreadIds);
    }

    [Fact]
    public async Task DuplicateStartAndOpenDoNotCreateAnotherConnection()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());
        await using var connection = Create(api);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(connection.Start)));
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        var first = connection.Current;
        api.Enqueue(SimConnectPackets.Open("Duplicate acknowledgement"));
        int dispatched = api.Dispatched;
        await Until(() => api.Dispatched >= dispatched + 2);
        Assert.Same(first, connection.Current);
        Assert.Equal(1, api.Attempts);
        await Task.WhenAll(connection.StopAsync(), connection.StopAsync());
        Assert.Equal(1, api.Closed);
        Assert.Single(api.ThreadIds);
    }

    [Fact]
    public async Task StopInterruptsLongBackoffWithoutRetrying()
    {
        var api = new SimConnectTestTransport();
        api.OpenResults.Enqueue(SimConnectTestTransport.Failure);
        await using var connection = Create(api, FastOptions() with
        {
            InitialRetryDelay = TimeSpan.FromMinutes(1), MaximumRetryDelay = TimeSpan.FromMinutes(1)
        });
        connection.Start();
        await Until(() => connection.Current.Issue == SimulatorConnectionIssue.ConnectionFailed);
        await connection.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, api.Attempts);
        Assert.Equal(0, api.Closed);
    }

    [Fact]
    public async Task StopAndRestartCreateNewSessionAndDisposeIsIdempotent()
    {
        var api = new SimConnectTestTransport();
        await using var connection = Create(api);
        api.Enqueue(SimConnectPackets.Open());
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        await connection.StopAsync();
        api.Enqueue(SimConnectPackets.Open());
        connection.Start();
        await Until(() => api.Attempts == 2 && connection.Current.State == SimulatorConnectionState.Connected);
        await connection.DisposeAsync();
        await connection.DisposeAsync();
        Assert.Equal(2, api.Closed);
        Assert.False(api.OpenedBeforeClose);
        Assert.Throws<ObjectDisposedException>(connection.Start);
    }

    [Theory]
    [InlineData(0, SimulatorConnectionIssue.RuntimeMissing)]
    [InlineData(1, SimulatorConnectionIssue.RuntimeIncompatible)]
    [InlineData(2, SimulatorConnectionIssue.RuntimeIncompatible)]
    [InlineData(3, SimulatorConnectionIssue.UnsupportedPlatform)]
    public async Task RuntimeFailuresKeepServiceAliveAndStopCleanly(int failure, SimulatorConnectionIssue expected)
    {
        var api = new SimConnectTestTransport
        {
            OpenException = failure switch
            {
                0 => new DllNotFoundException(), 1 => new BadImageFormatException(),
                2 => new EntryPointNotFoundException(), _ => new PlatformNotSupportedException()
            }
        };
        await using var connection = Create(api);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Unavailable);
        Assert.Equal(expected, connection.Current.Issue);
        await connection.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, api.Closed);
    }

    [Fact]
    public async Task HandshakeTimeoutClosesUnacknowledgedSession()
    {
        var clock = new TestClock();
        var api = new SimConnectTestTransport();
        api.Enqueue(action: () => clock.Advance(TimeSpan.FromSeconds(11)));
        await using var connection = Create(api, FastOptions() with
        {
            InitialRetryDelay = TimeSpan.FromSeconds(10), MaximumRetryDelay = TimeSpan.FromSeconds(10)
        }, clock);
        connection.Start();
        await Until(() => connection.Current.Issue == SimulatorConnectionIssue.HandshakeTimeout);
        Assert.Equal(1, api.Closed);
        Assert.Null(connection.Current.Simulator);
        Assert.Equal(SimulatorConnectionState.WaitingForSimulator, connection.Current.State);
    }

    [Fact]
    public async Task MalformedCallbackDoesNotEscapeNativeBoundaryAndCanRecover()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Header(2));
        await using var connection = Create(api);
        connection.Start();
        await Until(() => api.Closed == 1);
        api.Enqueue(SimConnectPackets.Open());
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        Assert.Equal(2, api.Attempts);
    }

    [Fact]
    public async Task VersionMismatchClosesAndReportsUnavailable()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Exception(5));
        await using var connection = Create(api);
        connection.Start();
        await Until(() => connection.Current.Issue == SimulatorConnectionIssue.VersionMismatch);
        Assert.Equal(SimulatorConnectionState.Unavailable, connection.Current.State);
        Assert.Equal(1, api.Closed);
        Assert.Null(connection.Current.Simulator);
    }

    [Fact]
    public async Task UnexpectedFailureClosesHandleAndReportsFaulted()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(action: () => throw new InvalidOperationException("Transport failure"));
        await using var connection = Create(api);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Faulted);
        Assert.Equal(1, api.Closed);
        Assert.Equal(SimulatorConnectionIssue.UnexpectedError, connection.Current.Issue);
    }

    [Fact]
    public async Task FailedOpenWithAllocatedHandleStillClosesIt()
    {
        var api = new SimConnectTestTransport { ReturnHandleOnFailure = true };
        api.OpenResults.Enqueue(SimConnectTestTransport.Failure);
        await using var connection = Create(api);
        connection.Start();
        await Until(() => api.Attempts >= 2);
        await connection.StopAsync();
        Assert.Equal(api.Attempts, api.Closed);
        Assert.False(api.OpenedBeforeClose);
    }

    [Fact]
    public void RetryConfigurationIsBoundedAndInvalidWaitsAreRejected()
    {
        var options = new SimConnectConnectionOptions();
        options.Validate();

        Assert.Equal(TimeSpan.FromSeconds(1), options.InitialRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(15), options.MaximumRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RuntimeRetryDelay);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (options with { DispatchInterval = TimeSpan.Zero }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (options with
            {
                MaximumRetryDelay = TimeSpan.FromMilliseconds(1)
            }).Validate());
    }

    [Fact]
    public async Task RuntimeFailureRetriesThroughPollyAndRecovers()
    {
        var api = new SimConnectTestTransport
        {
            OpenException = new DllNotFoundException()
        };

        await using var connection = Create(
            api,
            FastOptions() with
            {
                RuntimeRetryDelay = TimeSpan.FromMilliseconds(10)
            });

        connection.Start();
        await Until(() =>
            api.Attempts >= 1
            && connection.Current.State
                == SimulatorConnectionState.Unavailable);

        api.OpenException = null;
        api.Enqueue(SimConnectPackets.Open("Recovered simulator"));

        await Until(() =>
            api.Attempts >= 2
            && connection.Current.State
                == SimulatorConnectionState.Connected);

        Assert.Equal(
            "Recovered simulator",
            connection.Current.Simulator!.Name);
        Assert.Single(api.ThreadIds);
    }

    private static SimConnectConnection Create(SimConnectTestTransport api,
        SimConnectConnectionOptions? options = null, TimeProvider? clock = null) =>
        new(api, NullLogger<SimConnectConnection>.Instance, options ?? FastOptions(), clock ?? TimeProvider.System);

    [Fact]
    public async Task EmptyDispatchDoesNotDisconnectAndMenuHeartbeatConfirmsLiveness()
    {
        var api = new SimConnectTestTransport();
        var clock = new TestClock();
        api.Enqueue(SimConnectPackets.Open());
        await using var connection = Create(api, clock: clock);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        api.Enqueue(result: SimConnectTestTransport.Failure, action: () => clock.Advance(TimeSpan.FromSeconds(6)));
        await Until(() => api.Heartbeats.Count == 1);
        uint request = api.Heartbeats.Single();
        api.Enqueue(SimConnectPackets.SystemState(request, inFlight: 0));
        api.Enqueue(action: () => clock.Advance(TimeSpan.FromSeconds(31)));
        await Until(() => api.Heartbeats.Count == 2);
        Assert.Equal(1, api.Attempts);
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
    }

    [Fact]
    public async Task MissingHeartbeatResponseOrWrongRequestIdCannotKeepDeadConnectionAlive()
    {
        var api = new SimConnectTestTransport();
        var clock = new TestClock();
        api.Enqueue(SimConnectPackets.Open());
        await using var connection = Create(api, FastOptions() with
        {
            InitialRetryDelay = TimeSpan.FromSeconds(10), MaximumRetryDelay = TimeSpan.FromSeconds(10)
        }, clock);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        api.Enqueue(action: () => clock.Advance(TimeSpan.FromSeconds(6)));
        await Until(() => api.Heartbeats.Count == 1);
        api.Enqueue(SimConnectPackets.SystemState(999));
        api.Enqueue(action: () => clock.Advance(TimeSpan.FromSeconds(31)));
        await Until(() => connection.Current.Issue == SimulatorConnectionIssue.ResponseTimeout);
        Assert.Equal(SimulatorConnectionState.Reconnecting, connection.Current.State);
        Assert.Equal(1, api.Closed);
        Assert.Null(connection.Current.Simulator);
    }

    [Fact]
    public async Task FailedHeartbeatRequestClosesConnection()
    {
        var api = new SimConnectTestTransport { HeartbeatResult = SimConnectTestTransport.Failure };
        var clock = new TestClock();
        api.Enqueue(SimConnectPackets.Open());
        await using var connection = Create(api, FastOptions() with
        {
            InitialRetryDelay = TimeSpan.FromSeconds(10), MaximumRetryDelay = TimeSpan.FromSeconds(10)
        }, clock);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        api.Enqueue(action: () => clock.Advance(TimeSpan.FromSeconds(6)));
        await Until(() => connection.Current.Issue == SimulatorConnectionIssue.ConnectionLost);
        Assert.Equal(1, api.Closed);
        Assert.Null(connection.Current.Simulator);
    }

    private static SimConnectConnectionOptions FastOptions() => new()
    {
        InitialRetryDelay = TimeSpan.FromMilliseconds(10),
        MaximumRetryDelay = TimeSpan.FromMilliseconds(10),
        DispatchInterval = TimeSpan.FromMilliseconds(5)
    };

    private static async Task Until(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(5, deadline.Token);
    }

    private sealed class TestClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }
}
