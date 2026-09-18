using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Simulator;
using OpenCareer.SimConnect;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.Tests;

public sealed class SimConnectMissionActorTests
{
    [Fact]
    public async Task EnrouteMissionActorIsCreatedTrackedAndRemovedOnConnectionWorker()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        connection.Start();
        await Until(() =>
            connection.Current.State
                == SimulatorConnectionState.Connected);

        var request =
            new SimulatorMissionAircraftSpawnRequest(
                ActorKey: "escort-transport-1",
                ContainerTitle: "Test Transport",
                Livery: string.Empty,
                TailNumber: "OC001",
                FlightNumber: -1,
                FlightPlanPath: "OpenCareer\\escort-test",
                FlightPlanPosition: 2.5,
                TouchAndGo: false);

        Task<SimulatorMissionActorHandle> spawnTask =
            connection.SpawnEnrouteAircraftAsync(request);

        await Until(() =>
            api.EnrouteCreates.Count == 1);
        var create = api.EnrouteCreates.Single();

        Assert.Equal(
            "Test Transport",
            create.ContainerTitle);
        Assert.Equal(
            2.5,
            create.FlightPlanPosition);

        api.Enqueue(
            SimConnectPackets.AssignedObjectId(
                create.RequestId,
                42));

        SimulatorMissionActorHandle actor =
            await spawnTask.WaitAsync(
                TimeSpan.FromSeconds(5));

        Assert.Equal(42u, actor.ObjectId);
        Assert.Equal(
            "escort-transport-1",
            actor.ActorKey);

        await Until(() =>
            connection.Actors.Count == 1);

        var actorRequest =
            api.TelemetryRequests.Single(
                requestRow =>
                    requestRow.ObjectId == 42);

        api.Enqueue(
            SimConnectPackets.SimObjectData(
                actorRequest.RequestId,
                actorRequest.DefinitionId,
                CreateTelemetryValues(),
                objectId: 42));

        await Until(() =>
            connection.Actors[0].Telemetry is not null);

        Assert.Equal(
            43.2338,
            connection.Actors[0]
                .Telemetry!
                .LatitudeDegrees);
        Assert.Equal(
            42u,
            connection.Actors[0]
                .Handle
                .ObjectId);

        await connection
            .RemoveActorAsync(actor)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(
            api.RemoveRequests,
            row => row.ObjectId == 42);
        Assert.Empty(connection.Actors);
        Assert.False(api.OverlapDetected);
        Assert.Single(api.ThreadIds);
    }

    [Fact]
    public async Task MissionActorCreationFailureDoesNotPublishPhantomActor()
    {
        var api = new SimConnectTestTransport
        {
            CreateEnrouteResult =
                SimConnectTestTransport.Failure
        };
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        connection.Start();
        await Until(() =>
            connection.Current.State
                == SimulatorConnectionState.Connected);

        var request =
            new SimulatorMissionAircraftSpawnRequest(
                "escort-transport-1",
                "Test Transport",
                string.Empty,
                "OC001",
                -1,
                "OpenCareer\\escort-test",
                2.5,
                false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await connection
                    .SpawnEnrouteAircraftAsync(request));

        Assert.Empty(connection.Actors);
        Assert.False(api.OverlapDetected);
    }

    [Fact]
    public void AssignedObjectIdDecoderPreservesRequestAndObjectIds()
    {
        SimConnectMessage? message = null;
        SimConnectPackets.WithPointer(
            SimConnectPackets.AssignedObjectId(
                123,
                456),
            (data, size) =>
                message =
                    SimConnectMessageDecoder.Decode(
                        data,
                        size));

        Assert.NotNull(message);
        Assert.Equal(
            SimConnectMessageKind.AssignedObjectId,
            message.Kind);
        Assert.Equal(123u, message.RequestId);
        Assert.Equal(456u, message.ObjectId);
    }

    private static double[] CreateTelemetryValues()
    {
        var values =
            new double[
                SimConnectTelemetryDefinition.ValueCount];

        values[
            (int)SimConnectTelemetryValue.Latitude] =
            43.2338;
        values[
            (int)SimConnectTelemetryValue.Longitude] =
            -75.4069;
        values[
            (int)SimConnectTelemetryValue.AltitudeMsl] =
            8_000;
        values[
            (int)SimConnectTelemetryValue.AltitudeAgl] =
            5_000;
        values[
            (int)SimConnectTelemetryValue.IndicatedAirspeed] =
            250;
        values[
            (int)SimConnectTelemetryValue.GroundSpeed] =
            270;
        values[
            (int)SimConnectTelemetryValue.HeadingTrue] =
            90;
        values[
            (int)SimConnectTelemetryValue.NormalAcceleration] =
            1;
        values[
            (int)SimConnectTelemetryValue.NumberOfEngines] =
            2;
        values[
            (int)SimConnectTelemetryValue.Engine1Combustion] =
            1;
        values[
            (int)SimConnectTelemetryValue.Engine2Combustion] =
            1;
        values[
            (int)SimConnectTelemetryValue.FuelTotalWeight] =
            5_000;
        values[
            (int)SimConnectTelemetryValue.TotalWeight] =
            25_000;
        values[
            (int)SimConnectTelemetryValue.EmptyWeight] =
            15_000;
        values[
            (int)SimConnectTelemetryValue.GearTotalPercent] =
            0;

        return values;
    }

    private static SimConnectConnection Create(
        SimConnectTestTransport api) =>
        new(
            api,
            NullLogger<SimConnectConnection>.Instance,
            new SimConnectConnectionOptions
            {
                InitialRetryDelay =
                    TimeSpan.FromMilliseconds(10),
                MaximumRetryDelay =
                    TimeSpan.FromMilliseconds(10),
                DispatchInterval =
                    TimeSpan.FromMilliseconds(5)
            },
            TimeProvider.System);

    private static async Task Until(
        Func<bool> condition)
    {
        using var deadline =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(5));

        while (!condition())
        {
            await Task.Delay(
                5,
                deadline.Token);
        }
    }
}
