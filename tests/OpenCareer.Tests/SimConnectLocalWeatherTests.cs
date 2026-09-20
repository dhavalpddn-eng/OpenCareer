using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Planning;
using OpenCareer.SimConnect;

namespace OpenCareer.Tests;

public sealed class SimConnectLocalWeatherTests
{
    [Fact]
    public async Task WeatherDefinitionUsesOnlyVerifiedUserPositionSimVars()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        connection.Start();

        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        string[] fields = api.DataDefinitions
            .Where(static item =>
                item.DefinitionId == SimConnectLocalWeatherDefinition.DefinitionId)
            .Select(static item => $"{item.DatumName}|{item.Units}")
            .ToArray();

        Assert.Equal(
        [
            "PLANE LATITUDE|degrees",
            "PLANE LONGITUDE|degrees",
            "AMBIENT WIND DIRECTION|degrees",
            "AMBIENT WIND VELOCITY|knots",
            "DENSITY ALTITUDE|feet"
        ],
            fields);

        Assert.Contains(
            api.TelemetryRequests,
            static request =>
                request.RequestId == SimConnectLocalWeatherDefinition.RequestId
                && request.DefinitionId == SimConnectLocalWeatherDefinition.DefinitionId
                && request.Period == SimConnectPeriod.Second);

        Assert.False(api.OverlapDetected);
        Assert.Single(api.ThreadIds);
    }

    [Fact]
    public async Task OptionalWeatherDefinitionFailureDoesNotDropTelemetryConnection()
    {
        var api = new SimConnectTestTransport();
        api.FailedDataDefinitionIds.Add(SimConnectLocalWeatherDefinition.DefinitionId);
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        connection.Start();

        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
        Assert.Null(connection.LocalWeather);
        Assert.Contains(
            api.TelemetryRequests,
            static request =>
                request.DefinitionId == SimConnectTelemetryDefinition.DefinitionId);
        Assert.DoesNotContain(
            api.TelemetryRequests,
            static request =>
                request.DefinitionId == SimConnectLocalWeatherDefinition.DefinitionId);
    }

    [Fact]
    public async Task LocalWeatherMapsFacilityGeometryAndDensityAltitude()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectLocalAirportWeatherSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        api.Enqueue(LocalWeatherPacket(
            latitude: 43.2338,
            longitude: -75.4070,
            windFromTrue: 180,
            windVelocity: 20,
            densityAltitude: 2400));

        await Until(() => connection.LocalWeather is not null);

        Task<AirportDispatchWeatherObservation?> lookup =
            source.FindWeatherAsync("KRME");

        await Until(() => api.FacilityRequests.Count == 1);
        var request = Assert.Single(api.FacilityRequests);

        api.Enqueue(SimConnectPackets.AirportFacility(
            request.RequestId,
            100,
            "Griffiss International",
            "KRME",
            latitudeDegrees: 43.2338,
            longitudeDegrees: -75.4070));
        api.Enqueue(SimConnectPackets.RunwayFacility(
            request.RequestId,
            101,
            100,
            itemIndex: 0,
            listSize: 1,
            lengthMeters: 3600,
            widthMeters: 60,
            surface: 4,
            primaryNumber: 18,
            primaryDesignator: 0,
            secondaryNumber: 36,
            secondaryDesignator: 0,
            centerLatitudeDegrees: 43.2338,
            centerLongitudeDegrees: -75.4070,
            headingTrueDegrees: 180));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(request.RequestId));

        AirportDispatchWeatherObservation observation =
            Assert.IsType<AirportDispatchWeatherObservation>(await lookup);

        Assert.Equal("KRME", observation.Icao);
        Assert.Equal("msfs-simconnect-local-ambient", observation.SourceId);
        Assert.Equal(DispatchWeatherAuthority.LocalSimulator, observation.Authority);
        Assert.Equal(2400, observation.DensityAltitudeFeet);

        RunwayWindObservation wind = Assert.Single(observation.RunwayWinds);
        Assert.Equal("18/36", wind.RunwayIdentifier);
        Assert.Equal(20, wind.SustainedHeadwindKnots!.Value, 6);
        Assert.Equal(0, wind.SustainedCrosswindKnots!.Value, 6);
        Assert.Null(wind.GustHeadwindKnots);
        Assert.Null(wind.GustCrosswindKnots);
    }

    [Fact]
    public async Task CrosswindUsesTrueRunwayHeadingNotRunwayNumber()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectLocalAirportWeatherSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        api.Enqueue(LocalWeatherPacket(40, -75, 90, 20, 1000));
        await Until(() => connection.LocalWeather is not null);

        Task<AirportDispatchWeatherObservation?> lookup =
            source.FindWeatherAsync("KAAA");
        await Until(() => api.FacilityRequests.Count == 1);
        var request = Assert.Single(api.FacilityRequests);

        api.Enqueue(SimConnectPackets.AirportFacility(
            request.RequestId,
            200,
            "Heading Fixture",
            "KAAA",
            40,
            -75));
        api.Enqueue(SimConnectPackets.RunwayFacility(
            request.RequestId,
            201,
            200,
            0,
            1,
            1800,
            45,
            4,
            18,
            0,
            36,
            0,
            centerLatitudeDegrees: 40,
            centerLongitudeDegrees: -75,
            headingTrueDegrees: 100));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(request.RequestId));

        RunwayWindObservation wind = Assert.Single(
            (await lookup)!.RunwayWinds);

        Assert.Equal(20 * Math.Cos(10 * Math.PI / 180), wind.SustainedHeadwindKnots!.Value, 6);
        Assert.Equal(20 * Math.Sin(10 * Math.PI / 180), wind.SustainedCrosswindKnots!.Value, 6);
    }

    [Fact]
    public async Task ClosedPrimaryEndCannotSupplyFavorableHeadwind()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectLocalAirportWeatherSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        api.Enqueue(LocalWeatherPacket(40, -75, 180, 12, 1000));
        await Until(() => connection.LocalWeather is not null);

        Task<AirportDispatchWeatherObservation?> lookup =
            source.FindWeatherAsync("KAAA");
        await Until(() => api.FacilityRequests.Count == 1);
        var request = Assert.Single(api.FacilityRequests);

        api.Enqueue(SimConnectPackets.AirportFacility(
            request.RequestId,
            300,
            "Closed End Fixture",
            "KAAA",
            40,
            -75));
        api.Enqueue(SimConnectPackets.RunwayFacility(
            request.RequestId,
            301,
            300,
            0,
            1,
            1800,
            45,
            4,
            18,
            0,
            36,
            0,
            primaryClosed: true,
            secondaryClosed: false,
            centerLatitudeDegrees: 40,
            centerLongitudeDegrees: -75,
            headingTrueDegrees: 180));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(request.RequestId));

        RunwayWindObservation wind = Assert.Single(
            (await lookup)!.RunwayWinds);

        Assert.Equal(-12, wind.SustainedHeadwindKnots!.Value, 6);
        Assert.Equal(0, wind.SustainedCrosswindKnots!.Value, 6);
    }

    [Fact]
    public async Task RemoteAirportDoesNotReuseUserPositionWeather()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectLocalAirportWeatherSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        api.Enqueue(LocalWeatherPacket(40, -75, 180, 10, 1000));
        await Until(() => connection.LocalWeather is not null);

        Task<AirportDispatchWeatherObservation?> lookup =
            source.FindWeatherAsync("KBBB");
        await Until(() => api.FacilityRequests.Count == 1);
        var request = Assert.Single(api.FacilityRequests);

        api.Enqueue(SimConnectPackets.AirportFacility(
            request.RequestId,
            400,
            "Remote Fixture",
            "KBBB",
            42,
            -75));
        api.Enqueue(SimConnectPackets.RunwayFacility(
            request.RequestId,
            401,
            400,
            0,
            1,
            1800,
            45,
            4,
            18,
            0,
            36,
            0,
            centerLatitudeDegrees: 42,
            centerLongitudeDegrees: -75,
            headingTrueDegrees: 180));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(request.RequestId));

        Assert.Null(await lookup);
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
    }

    [Fact]
    public async Task StaleLocalWeatherIsNotUsed()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));

        await using var connection = Create(api, clock);
        var source = new SimConnectLocalAirportWeatherSource(connection, clock);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        api.Enqueue(LocalWeatherPacket(40, -75, 180, 10, 1000));
        await Until(() => connection.LocalWeather is not null);

        clock.Advance(TimeSpan.FromSeconds(6));

        Task<AirportDispatchWeatherObservation?> lookup =
            source.FindWeatherAsync("KAAA");
        await Until(() => api.FacilityRequests.Count == 1);
        var request = Assert.Single(api.FacilityRequests);

        api.Enqueue(SimConnectPackets.AirportFacility(
            request.RequestId,
            500,
            "Stale Fixture",
            "KAAA",
            40,
            -75));
        api.Enqueue(SimConnectPackets.RunwayFacility(
            request.RequestId,
            501,
            500,
            0,
            1,
            1800,
            45,
            4,
            18,
            0,
            36,
            0,
            centerLatitudeDegrees: 40,
            centerLongitudeDegrees: -75,
            headingTrueDegrees: 180));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(request.RequestId));

        Assert.Null(await lookup);
    }

    [Fact]
    public async Task DisconnectClearsLocalWeather()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        api.Enqueue(LocalWeatherPacket(40, -75, 180, 10, 1000));
        await Until(() => connection.LocalWeather is not null);

        api.Enqueue(SimConnectPackets.Header(3));

        await Until(() => connection.Current.State != SimulatorConnectionState.Connected);
        await Until(() => connection.LocalWeather is null);

        Assert.Null(connection.LocalWeather);
    }

    [Fact]
    public void LocalWeatherMapperRejectsMalformedValues()
    {
        Assert.Null(SimConnectLocalWeatherMapper.Map(
            [91, -75, 180, 10, 1000],
            DateTimeOffset.UtcNow));

        Assert.Null(SimConnectLocalWeatherMapper.Map(
            [40, -75, 180, -1, 1000],
            DateTimeOffset.UtcNow));

        Assert.Null(SimConnectLocalWeatherMapper.Map(
            [40, -75, double.NaN, 10, 1000],
            DateTimeOffset.UtcNow));
    }

    private static byte[] LocalWeatherPacket(
        double latitude,
        double longitude,
        double windFromTrue,
        double windVelocity,
        double densityAltitude) =>
        SimConnectPackets.SimObjectData(
            SimConnectLocalWeatherDefinition.RequestId,
            SimConnectLocalWeatherDefinition.DefinitionId,
            [latitude, longitude, windFromTrue, windVelocity, densityAltitude]);

    private static SimConnectConnection Create(
        SimConnectTestTransport api,
        TimeProvider? clock = null) =>
        new(
            api,
            NullLogger<SimConnectConnection>.Instance,
            new SimConnectConnectionOptions
            {
                InitialRetryDelay = TimeSpan.FromMilliseconds(10),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(10),
                DispatchInterval = TimeSpan.FromMilliseconds(5)
            },
            clock ?? TimeProvider.System);

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset origin) : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() =>
            origin.AddTicks(Volatile.Read(ref _timestamp));

        public override long GetTimestamp() =>
            Volatile.Read(ref _timestamp);

        public void Advance(TimeSpan elapsed) =>
            Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }
}
