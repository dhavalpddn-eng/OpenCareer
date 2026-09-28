using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Telemetry;
using OpenCareer.SimConnect;

namespace OpenCareer.Tests;

public sealed class SimConnectTelemetryConnectionTests
{
    [Fact]
    public async Task OpenAcknowledgementConfiguresTelemetryAndPublishesSnapshotsOnConnectionWorker()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        var telemetryDefinitions = api.DataDefinitions
            .Where(static definition =>
                definition.DefinitionId == SimConnectTelemetryDefinition.DefinitionId)
            .ToArray();

        Assert.Equal(SimConnectTelemetryDefinition.ValueCount, telemetryDefinitions.Length);

        var request = Assert.Single(
            api.TelemetryRequests,
            static request =>
                request.RequestId == SimConnectTelemetryDefinition.RequestId
                && request.DefinitionId == SimConnectTelemetryDefinition.DefinitionId);

        Assert.Equal(OpenCareer.SimConnect.Native.SimConnectPeriod.Second, request.Period);
        Assert.Equal(0u, request.Origin);
        Assert.Equal(0u, request.Interval);
        Assert.Equal(0u, request.Limit);

        var criticalRequest = Assert.Single(
            api.TelemetryRequests,
            static request =>
                request.RequestId
                    == SimConnectTelemetryDefinition.FlightCriticalRequestId
                && request.DefinitionId
                    == SimConnectTelemetryDefinition.DefinitionId);

        Assert.Equal(
            OpenCareer.SimConnect.Native.SimConnectPeriod.SimFrame,
            criticalRequest.Period);
        Assert.Equal(0u, criticalRequest.Origin);
        Assert.Equal(0u, criticalRequest.Interval);
        Assert.Equal(0u, criticalRequest.Limit);

        var pause = Assert.Single(api.SystemEvents);
        Assert.Equal(SimConnectTelemetryDefinition.PauseEventId, pause.EventId);
        Assert.Equal("Pause_EX1", pause.EventName);

        var values = new double[SimConnectTelemetryDefinition.ValueCount];
        values[(int)SimConnectTelemetryValue.Latitude] = 43.2338;
        values[(int)SimConnectTelemetryValue.Longitude] = -75.4069;
        values[(int)SimConnectTelemetryValue.AltitudeMsl] = 2_000;
        values[(int)SimConnectTelemetryValue.AltitudeAgl] = 900;
        values[(int)SimConnectTelemetryValue.IndicatedAirspeed] = 130;
        values[(int)SimConnectTelemetryValue.GroundSpeed] = 142;
        values[(int)SimConnectTelemetryValue.VerticalSpeedFeetPerSecond] = 10;
        values[(int)SimConnectTelemetryValue.HeadingTrue] = 180;
        values[(int)SimConnectTelemetryValue.NormalAcceleration] = 1;
        values[(int)SimConnectTelemetryValue.NumberOfEngines] = 1;
        values[(int)SimConnectTelemetryValue.Engine1Combustion] = 1;
        values[(int)SimConnectTelemetryValue.FuelTotalWeight] = 300;
        values[(int)SimConnectTelemetryValue.TotalWeight] = 2_200;
        values[(int)SimConnectTelemetryValue.EmptyWeight] = 1_500;
        values[(int)SimConnectTelemetryValue.GearTotalPercent] = 100;

        api.Enqueue(SimConnectPackets.Event(SimConnectTelemetryDefinition.PauseEventId, 4));
        api.Enqueue(SimConnectPackets.SimObjectData(
            SimConnectTelemetryDefinition.RequestId,
            SimConnectTelemetryDefinition.DefinitionId,
            values));

        await Until(() => connection.Latest is not null);
        Assert.Equal(43.2338, connection.Latest!.LatitudeDegrees);
        Assert.Equal(600, connection.Latest.VerticalSpeedFeetPerMinute);
        Assert.Equal(1, connection.Latest.EnginesRunning);
        Assert.True(connection.Latest.Paused);
        Assert.True(connection.Latest.GearDown);

        IFlightCriticalTelemetrySource critical = connection;
        Assert.Empty(critical.ReadAfter(null));

        api.Enqueue(SimConnectPackets.SimObjectData(
            SimConnectTelemetryDefinition.FlightCriticalRequestId,
            SimConnectTelemetryDefinition.DefinitionId,
            values));
        await Until(() => critical.ReadAfter(null).Count == 1);

        var buffered =
            Assert.Single(critical.ReadAfter(null));
        Assert.Equal(
            connection.Latest.LatitudeDegrees,
            buffered.LatitudeDegrees);
        Assert.Equal(
            connection.Latest.HeadingDegrees,
            buffered.HeadingDegrees);
        Assert.True(buffered.Timestamp >= connection.Latest.Timestamp);
        critical.Clear();
        Assert.Empty(critical.ReadAfter(null));
        Assert.Equal(43.2338, connection.Latest.LatitudeDegrees);

        api.Enqueue(SimConnectPackets.SimObjectData(
            SimConnectTelemetryDefinition.FlightCriticalRequestId,
            SimConnectTelemetryDefinition.DefinitionId,
            values));
        await Until(() => critical.ReadAfter(null).Count == 1);

        api.Enqueue(SimConnectPackets.Header(3));
        await Until(() => api.Closed >= 1 && connection.Latest is null);

        Assert.Empty(critical.ReadAfter(null));
        Assert.False(api.OverlapDetected);
        Assert.Single(api.ThreadIds);
    }

    [Fact]
    public async Task SimFrameCriticalStreamPreservesSubsecondGroundEdgesWithoutChangingLatest()
    {
        var api = new SimConnectTestTransport();
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero));
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api, clock: clock);
        connection.Start();
        await Until(() =>
            connection.Current.State == SimulatorConnectionState.Connected);

        IFlightCriticalTelemetrySource critical = connection;

        EnqueueCritical(api, clock, onGround: true, milliseconds: 1);
        EnqueueCritical(api, clock, onGround: false, milliseconds: 10);
        EnqueueCritical(api, clock, onGround: true, milliseconds: 10);
        EnqueueCritical(api, clock, onGround: true, milliseconds: 10);
        EnqueueCritical(api, clock, onGround: true, milliseconds: 60);

        await Until(() => critical.ReadAfter(null).Count == 4);

        AircraftTelemetrySnapshot[] retained =
            critical.ReadAfter(null).ToArray();
        Assert.Equal([true, false, true, true], retained.Select(x => x.OnGround));
        Assert.All(
            retained.Zip(retained.Skip(1)),
            pair => Assert.True(
                pair.First.Timestamp < pair.Second.Timestamp));
        Assert.Null(connection.Latest);

        var coarseValues = Values(onGround: false);
        coarseValues[(int)SimConnectTelemetryValue.HeadingTrue] = 123;
        api.Enqueue(
            SimConnectPackets.SimObjectData(
                SimConnectTelemetryDefinition.RequestId,
                SimConnectTelemetryDefinition.DefinitionId,
                coarseValues),
            action: () => clock.Advance(TimeSpan.FromMilliseconds(1)));

        await Until(() => connection.Latest?.HeadingDegrees == 123);
        Assert.Equal(
            retained.Select(sample => sample.Timestamp),
            critical.ReadAfter(null).Select(sample => sample.Timestamp));

        api.Enqueue(SimConnectPackets.Header(3));
        await Until(() => api.Closed >= 1 && connection.Latest is null);
        Assert.Empty(critical.ReadAfter(null));

        api.Enqueue(SimConnectPackets.Open());
        await Until(() => api.Attempts == 2
            && connection.Current.State == SimulatorConnectionState.Connected);
        EnqueueCritical(api, clock, onGround: true, milliseconds: 1);
        await Until(() => critical.ReadAfter(null).Count == 1);
        Assert.Single(critical.ReadAfter(null));
    }

    [Fact]
    public async Task TelemetryConfigurationFailureNeverClaimsConnectedState()
    {
        var api = new SimConnectTestTransport { AddDefinitionResult = SimConnectTestTransport.Failure };
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api, TimeSpan.FromMinutes(1));
        connection.Start();

        await Until(() => connection.Current.Issue == SimulatorConnectionIssue.SimulatorError);

        Assert.NotEqual(SimulatorConnectionState.Connected, connection.Current.State);
        Assert.Null(connection.Latest);
        Assert.True(api.Closed >= 1);
    }

    private static SimConnectConnection Create(
        SimConnectTestTransport api,
        TimeSpan? retryDelay = null,
        TimeProvider? clock = null)
    {
        TimeSpan delay = retryDelay ?? TimeSpan.FromMilliseconds(10);
        return new(
            api,
            NullLogger<SimConnectConnection>.Instance,
            new SimConnectConnectionOptions
            {
                InitialRetryDelay = delay,
                MaximumRetryDelay = delay,
                DispatchInterval = TimeSpan.FromMilliseconds(5)
            },
            clock ?? TimeProvider.System);
    }

    private static void EnqueueCritical(
        SimConnectTestTransport api,
        ManualTimeProvider clock,
        bool onGround,
        double milliseconds)
    {
        api.Enqueue(
            SimConnectPackets.SimObjectData(
                SimConnectTelemetryDefinition.FlightCriticalRequestId,
                SimConnectTelemetryDefinition.DefinitionId,
                Values(onGround)),
            action: () =>
                clock.Advance(TimeSpan.FromMilliseconds(milliseconds)));
    }

    private static double[] Values(bool onGround)
    {
        var values = new double[SimConnectTelemetryDefinition.ValueCount];
        values[(int)SimConnectTelemetryValue.OnGround] = onGround ? 1 : 0;
        values[(int)SimConnectTelemetryValue.NumberOfEngines] = 1;
        values[(int)SimConnectTelemetryValue.Engine1Combustion] = 1;
        return values;
    }

    private sealed class ManualTimeProvider(DateTimeOffset epoch) : TimeProvider
    {
        private long _ticks;

        public override DateTimeOffset GetUtcNow() =>
            epoch.AddTicks(Volatile.Read(ref _ticks));

        public override long GetTimestamp() =>
            Volatile.Read(ref _ticks);

        public override long TimestampFrequency =>
            TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan elapsed) =>
            Interlocked.Add(ref _ticks, elapsed.Ticks);
    }

    private static async Task Until(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(5, deadline.Token);
    }
}
