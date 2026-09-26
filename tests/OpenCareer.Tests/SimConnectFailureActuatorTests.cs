using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Simulator;
using OpenCareer.SimConnect;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.Tests;

public sealed partial class SimConnectFailureActuatorTests
{
    [Fact]
    public async Task OfficialBindingUsesExistingWorkerAndAppliedRequiresReadback()
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        var mapped = Assert.Single(h.Api.EventMappings);
        Assert.Equal("TOGGLE_ENGINE1_FAILURE", mapped.EventName);
        Assert.Equal(SimConnectEngineFailureDefinition.EventId, mapped.EventId);
        Assert.Contains(h.Api.DataDefinitions, d => d.DefinitionId == SimConnectEngineFailureDefinition.DefinitionId
            && d.DatumName == "GENERAL ENG FAILED:1" && d.Units == "Bool");
        Assert.Contains(h.Api.TelemetryRequests, r => r.RequestId == SimConnectEngineFailureDefinition.RequestId
            && r.DefinitionId == SimConnectEngineFailureDefinition.DefinitionId && r.Period == SimConnectPeriod.Second);

        int callerThread = Environment.CurrentManagedThreadId;
        var pending = h.Actuator.EnsureEngineFailedAsync(1);
        await Until(() => h.Api.Transmissions.Count == 1);
        await h.DrainAsync();
        Assert.False(pending.IsCompleted); // HRESULT success cannot acknowledge a physical effect.
        var sent = Assert.Single(h.Api.Transmissions);
        Assert.Equal((0u, mapped.EventId, 0u, 1u, 0x10u), (sent.ObjectId, sent.EventId, sent.Data, sent.GroupId, sent.Flags));
        await h.CoreTelemetryAsync(); // Engine stopped in ordinary telemetry is not failure acknowledgement.
        Assert.False(pending.IsCompleted);
        await h.ObserveAsync(true);
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SimulatorFailureActuationStatus.Applied, result.Status);
        Assert.True(result.Observation.Engine1Failed);
        Assert.Equal(h.Clock.GetUtcNow(), result.Observation.ObservedAt);
        Assert.Equal(SimulatorFailureActuationStatus.AlreadyFailed, (await h.Actuator.EnsureEngineFailedAsync(1)).Status);
        Assert.Single(h.Api.Transmissions);
        Assert.False(h.Api.OverlapDetected);
        Assert.False(h.Api.OpenedBeforeClose);
        Assert.NotEqual(callerThread, Assert.Single(h.Api.ThreadIds));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task AlreadyFailedNeverTransmits(int trueEncoding)
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.PacketAsync(StatePacket(trueEncoding));
        Assert.Equal(SimulatorFailureActuationStatus.AlreadyFailed, (await h.Actuator.EnsureEngineFailedAsync(1)).Status);
        Assert.Empty(h.Api.Transmissions);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task UnsupportedEngineNeverCallsNative(int engine)
    {
        await using var h = new Harness();
        Assert.Equal(SimulatorFailureActuationStatus.UnsupportedEngine, (await h.Actuator.EnsureEngineFailedAsync(engine)).Status);
        Assert.Empty(h.Api.ThreadIds);
    }

    [Fact]
    public async Task DisconnectedUnobservedAndStaleStateAreUnavailable()
    {
        await using var h = new Harness();
        await h.AssertUnavailableAsync();
        await h.StartAsync();
        await h.AssertUnavailableAsync();
        await h.ObserveAsync(false);
        await h.AdvanceAsync(SimConnectEngineFailureDefinition.MaximumObservationAge + TimeSpan.FromTicks(1));
        await h.AssertUnavailableAsync();
        Assert.Empty(h.Api.Transmissions);
        await h.CoreTelemetryAsync();
    }

    [Fact]
    public async Task TimeoutAndExplicitRetryNeverRetoggleAnUnresolvedCommand()
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        var pending = h.Actuator.EnsureEngineFailedAsync(1);
        await Until(() => h.Api.Transmissions.Count == 1);
        await h.AdvanceAsync(SimConnectEngineFailureDefinition.AcknowledgementWindow + TimeSpan.FromTicks(1));
        Assert.Equal(SimulatorFailureActuationStatus.AcknowledgementTimeout, (await pending).Status);
        await h.ObserveAsync(false);
        Assert.Equal(SimulatorFailureActuationStatus.AcknowledgementTimeout, (await h.Actuator.EnsureEngineFailedAsync(1)).Status);
        await h.ObserveAsync(true);
        Assert.Equal(SimulatorFailureActuationStatus.AlreadyFailed, (await h.Actuator.EnsureEngineFailedAsync(1)).Status);
        Assert.Single(h.Api.Transmissions);
        Assert.Contains(h.Log.Entries, e => e.Message.Contains("timed out", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PositiveReadbackBeyondAcknowledgementWindowIsNotApplied()
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        var pending = h.Actuator.EnsureEngineFailedAsync(1);
        await Until(() => h.Api.Transmissions.Count == 1);
        h.Api.Enqueue(StatePacket(1), action: () => h.Clock.Advance(TimeSpan.FromSeconds(11)));
        Assert.Equal(SimulatorFailureActuationStatus.AcknowledgementTimeout, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.Equal(SimulatorFailureActuationStatus.AlreadyFailed, (await h.Actuator.EnsureEngineFailedAsync(1)).Status);
        Assert.Single(h.Api.Transmissions);
    }

    [Fact]
    public async Task OnlyOneCommandCanOccupyBoundedWorkerSlot()
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        var first = h.Actuator.EnsureEngineFailedAsync(1);
        await Until(() => h.Api.Transmissions.Count == 1);
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => h.Actuator.EnsureEngineFailedAsync(1)));
        Assert.All(results, r => Assert.Equal(SimulatorFailureActuationStatus.Busy, r.Status));
        await h.ObserveAsync(true);
        Assert.Equal(SimulatorFailureActuationStatus.Applied, (await first).Status);
        Assert.Single(h.Api.Transmissions);
    }

    [Theory]
    [InlineData("mapping")]
    [InlineData("definition")]
    [InlineData("request")]
    [InlineData("packet-id")]
    [InlineData("missing-entry-point")]
    public async Task OptionalSetupFailurePreservesCoreConnectionAndTelemetry(string stage)
    {
        await using var h = new Harness();
        switch (stage)
        {
            case "mapping": h.Api.MapEventResult = SimConnectTestTransport.Failure; break;
            case "definition": h.Api.FailedDataDefinitionIds.Add(SimConnectEngineFailureDefinition.DefinitionId); break;
            case "request": h.Api.FailedRequestDefinitionIds.Add(SimConnectEngineFailureDefinition.DefinitionId); break;
            case "packet-id": h.Api.GetLastSentPacketIdResult = SimConnectTestTransport.Failure; break;
            case "missing-entry-point": h.Api.MapEventException = new EntryPointNotFoundException(); break;
        }
        await h.StartAsync();
        await h.ObserveAsync(false);
        await h.AssertUnavailableAsync();
        await h.CoreTelemetryAsync();
        Assert.Empty(h.Api.Transmissions);
        Assert.NotEmpty(h.Log.Entries);
    }

    [Theory]
    [InlineData("mapping")]
    [InlineData("definition")]
    [InlineData("request")]
    public async Task AsynchronousSetupExceptionDisablesOnlyOptionalCapability(string stage)
    {
        await using var h = new Harness();
        await h.StartAsync(false);
        uint id = h.Api.FailureSetupPackets.Single(p => p.Operation == stage).SendId;
        await h.PacketAsync(SimConnectPackets.Exception(3, id));
        await h.AssertUnavailableAsync();
        await h.CoreTelemetryAsync();
        Assert.Empty(h.Api.Transmissions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandSpecificRejectionDoesNotReconnectOrRetry(bool asynchronous)
    {
        await using var h = new Harness();
        if (!asynchronous) h.Api.TransmitResult = SimConnectTestTransport.Failure;
        await h.StartAsync(false);
        var pending = h.Actuator.EnsureEngineFailedAsync(1);
        await Until(() => h.Api.Transmissions.Count == 1);
        if (asynchronous) await h.PacketAsync(SimConnectPackets.Exception(3, h.Api.Transmissions.Single().SendId));
        Assert.Equal(SimulatorFailureActuationStatus.CommandRejected, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        await h.AdvanceAsync(TimeSpan.FromSeconds(12));
        await h.CoreTelemetryAsync();
        Assert.Single(h.Api.Transmissions);
        Assert.Contains(h.Log.Entries, e => e.Message.Contains("rejected", StringComparison.Ordinal));
    }

    private static byte[] StatePacket(params double[] values) => SimConnectPackets.SimObjectData(
        SimConnectEngineFailureDefinition.RequestId, SimConnectEngineFailureDefinition.DefinitionId, values);

    private static async Task Until(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(5, deadline.Token);
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddDays(20_000).AddTicks(GetTimestamp());
        internal void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }

    private sealed class Harness : IAsyncDisposable
    {
        internal SimConnectTestTransport Api { get; } = new();
        internal Clock Clock { get; } = new();
        internal CaptureLogger Log { get; } = new();
        internal SimConnectConnection Connection { get; }
        internal ISimulatorFailureActuator Actuator => Connection;
        internal Harness() => Connection = new(Api, Log, new()
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(10), MaximumRetryDelay = TimeSpan.FromMilliseconds(10),
            DispatchInterval = TimeSpan.FromMilliseconds(2), HeartbeatInterval = TimeSpan.FromHours(1), HeartbeatTimeout = TimeSpan.FromHours(1)
        }, Clock);
        internal async Task StartAsync(bool? initial = null)
        {
            Api.Enqueue(SimConnectPackets.Open()); Connection.Start();
            await Until(() => Connection.Current.State == SimulatorConnectionState.Connected);
            if (initial.HasValue) await ObserveAsync(initial.Value);
        }
        internal async Task DrainAsync()
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // CallDispatch actions precede Tick. A second dispatch proves the first worker
            // iteration (including cancellation/timeout slot cleanup) has fully completed.
            Api.Enqueue();
            Api.Enqueue(action: () => done.SetResult());
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        internal async Task PacketAsync(byte[] packet) { Api.Enqueue(packet); await DrainAsync(); }
        internal Task ObserveAsync(bool failed) => PacketAsync(StatePacket(failed ? 1 : 0));
        internal async Task AdvanceAsync(TimeSpan elapsed) { Api.Enqueue(action: () => Clock.Advance(elapsed)); await DrainAsync(); }
        internal async Task AssertUnavailableAsync() => Assert.Equal(SimulatorFailureActuationStatus.SimulatorUnavailable,
            (await Actuator.EnsureEngineFailedAsync(1).WaitAsync(TimeSpan.FromSeconds(5))).Status);
        internal async Task CoreTelemetryAsync()
        {
            await PacketAsync(SimConnectPackets.SimObjectData(SimConnectTelemetryDefinition.RequestId,
                SimConnectTelemetryDefinition.DefinitionId, new double[SimConnectTelemetryDefinition.ValueCount]));
            Assert.NotNull(Connection.Latest);
            Assert.Equal(SimulatorConnectionState.Connected, Connection.Current.State);
            Assert.Equal(1, Api.Attempts);
        }
        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    private sealed class CaptureLogger : Microsoft.Extensions.Logging.ILogger<SimConnectConnection>
    {
        internal System.Collections.Concurrent.ConcurrentQueue<(string Message, object? State)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Entries.Enqueue((formatter(state, exception), state));
    }
}
