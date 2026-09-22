using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Simulator;
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

        api.Enqueue(SimConnectPackets.Header(3));
        await Until(() => api.Closed >= 1 && connection.Latest is null);

        Assert.False(api.OverlapDetected);
        Assert.Single(api.ThreadIds);
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
        TimeSpan? retryDelay = null)
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
            TimeProvider.System);
    }

    private static async Task Until(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(5, deadline.Token);
    }
}
