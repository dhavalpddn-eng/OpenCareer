using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Aircraft;
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
    public async Task MissingAircraftCatalogResponseRetriesWithoutDisconnecting()
    {
        var api = new SimConnectTestTransport();
        var clock = new TestClock();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api, clock: clock);
        connection.Start();

        await Until(() =>
            connection.Current.State
                == SimulatorConnectionState.Connected
            && api.AircraftEnumerations.Count == 1);

        uint firstRequest =
            api.AircraftEnumerations.Single().RequestId;

        api.Enqueue(action: () =>
            clock.Advance(
                SimConnectAircraftCatalog.ResponseTimeout
                - TimeSpan.FromSeconds(1)));

        await Task.Delay(50);
        Assert.Single(api.AircraftEnumerations);

        api.Enqueue(action: () =>
            clock.Advance(
                SimConnectAircraftCatalog.RetryDelay
                + TimeSpan.FromSeconds(2)));

        await Until(() =>
            api.AircraftEnumerations.Count >= 2);

        uint[] requests =
            api.AircraftEnumerations
                .Select(static item => item.RequestId)
                .ToArray();

        Assert.NotEqual(
            firstRequest,
            requests[^1]);
        Assert.Equal(
            SimulatorConnectionState.Connected,
            connection.Current.State);
        Assert.Equal(1, api.Attempts);
    }

    [Fact]
    public async Task SuccessfulAircraftCatalogStopsRetrying()
    {
        var api = new SimConnectTestTransport();
        var clock = new TestClock();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api, clock: clock);
        connection.Start();

        await Until(() =>
            api.AircraftEnumerations.Count == 1);

        uint requestId =
            api.AircraftEnumerations.Single().RequestId;

        api.Enqueue(
            SimConnectPackets.EnumeratedSimObjects(
                requestId,
                entryNumber:
                    0,
                outOf:
                    1,
                ("Cessna 172", "Default")));

        await Until(() =>
            connection.AircraftCatalog.IsAvailable);

        Assert.Equal(
            ["Cessna 172"],
            connection.AircraftCatalog.AircraftTitles);

        api.Enqueue(action: () =>
            clock.Advance(
                SimConnectAircraftCatalog.ResponseTimeout
                + SimConnectAircraftCatalog.RetryDelay
                + TimeSpan.FromSeconds(1)));

        await Task.Delay(50);

        Assert.Single(
            api.AircraftEnumerations);
        Assert.Equal(
            SimulatorConnectionState.Connected,
            connection.Current.State);
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
    public void RetryBackoffIsBoundedAndInvalidWaitsAreRejected()
    {
        var options = new SimConnectConnectionOptions();
        Assert.Equal(new[] { 1, 2, 4, 8, 15, 15 }, Enumerable.Range(1, 6)
            .Select(failure => (int)options.RetryDelay(failure).TotalSeconds));
        Assert.Equal(TimeSpan.FromSeconds(15), options.RetryDelay(int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => (options with { DispatchInterval = TimeSpan.Zero }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { MaximumRetryDelay = TimeSpan.FromMilliseconds(1) }).Validate());
    }

    private static SimConnectConnection Create(SimConnectTestTransport api,
        SimConnectConnectionOptions? options = null, TimeProvider? clock = null) =>
        new(api, NullLogger<SimConnectConnection>.Instance, options ?? FastOptions(), clock ?? TimeProvider.System);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidTrafficKeepsDiscoveryConnectedBeyondMissingHeartbeatTimeout(bool titleTraffic)
    {
        var api = new SimConnectTestTransport();
        var clock = new TestClock();
        api.Enqueue(SimConnectPackets.Open());
        await using var connection = Create(api, clock: clock);
        var discovery = new SimConnectInstalledAircraftObservationSource(connection);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        api.Enqueue(TitlePacket());
        await Until(() => connection.CurrentAircraftTitle is not null);
        api.Enqueue(action: () => clock.Advance(TimeSpan.FromSeconds(6)));
        await Until(() => api.Heartbeats.Count == 1);

        // No SystemState response. Advance 36 simulated seconds with genuine decoded traffic.
        for (int i = 0; i < 6; i++)
        {
            await DispatchAsync(api, titleTraffic ? TitlePacket() : TelemetryPacket(),
                () => clock.Advance(TimeSpan.FromSeconds(6)));
            Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
            Assert.Equal(1, api.Attempts);
            Assert.Equal(InstalledAircraftDiscoveryAvailability.Available, discovery.Current.Availability);
            Assert.Equal(AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId,
                Assert.Single(discovery.Current.Observations).CanonicalAircraftId);
        }
        Assert.False(api.OverlapDetected);
        Assert.Single(api.ThreadIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogExceptionDisablesOnlyCatalogUntilNextConnection(bool delayedException)
    {
        var api = new SimConnectTestTransport();
        var clock = new TestClock();
        api.Enqueue(SimConnectPackets.Open());
        await using var connection = Create(api, clock: clock);
        connection.Start();
        await Until(() => api.AircraftEnumerations.Count == 1);
        api.Enqueue(TitlePacket());
        await Until(() => connection.CurrentAircraftTitle is not null);
        uint catalogSendId = api.LastSentPacketId;
        if (delayedException)
        {
            // An error from an older request must still be isolated after a retry.
            api.LastSentPacketId++;
            await DispatchAsync(api, TitlePacket(),
                () => clock.Advance(SimConnectAircraftCatalog.ResponseTimeout));
            Assert.Equal(2, api.AircraftEnumerations.Count);
        }
        await DispatchAsync(api, SimConnectPackets.Exception(20, catalogSendId));
        int attempts = api.AircraftEnumerations.Count;
        for (int i = 0; i < 13; i++)
            await DispatchAsync(api, TitlePacket(), () => clock.Advance(TimeSpan.FromSeconds(6)));
        Assert.Equal(attempts, api.AircraftEnumerations.Count);
        Assert.Equal(1, api.Attempts);
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
        Assert.False(connection.AircraftCatalog.IsAvailable);
        Assert.Single(new SimConnectInstalledAircraftObservationSource(connection).Current.Observations);
        await DispatchAsync(api, TelemetryPacket());
        Assert.NotNull(connection.Latest);

        api.Enqueue(SimConnectPackets.Header(3));
        await Until(() => api.Attempts == 2);
        api.Enqueue(SimConnectPackets.Open());
        await Until(() => api.AircraftEnumerations.Count == attempts + 1);
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
        Assert.False(api.OverlapDetected);
        Assert.Single(api.ThreadIds);
    }

    private static byte[] TitlePacket() => SimConnectPackets.StringSimObjectData(
        SimConnectCurrentAircraftDefinition.RequestId, SimConnectCurrentAircraftDefinition.DefinitionId,
        "C172SP Classic Passengers");

    private static byte[] TelemetryPacket() => SimConnectPackets.SimObjectData(
        SimConnectTelemetryDefinition.RequestId, SimConnectTelemetryDefinition.DefinitionId,
        new double[SimConnectTelemetryDefinition.ValueCount]);

    private static async Task DispatchAsync(SimConnectTestTransport api, byte[] packet, Action? action = null)
    {
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Enqueue(packet, action: action);
        api.Enqueue(action: () => processed.SetResult());
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

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
