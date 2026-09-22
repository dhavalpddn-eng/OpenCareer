using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Airports;
using OpenCareer.SimConnect;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.Tests;

public sealed class SimConnectAirportFacilityTests
{
    [Fact]
    public void DecoderReadsAirportRunwayAndEndPackets()
    {
        SimConnectMessage? airport = null;
        SimConnectPackets.WithPointer(
            SimConnectPackets.AirportFacility(900, 41, "Fixture Municipal", "KAAA"),
            (data, size) => airport = SimConnectMessageDecoder.Decode(data, size));

        Assert.NotNull(airport);
        Assert.Equal(SimConnectMessageKind.FacilityData, airport!.Kind);
        Assert.Equal(900u, airport.RequestId);
        Assert.Equal(SimConnectFacilityDataType.Airport, airport.FacilityDataType);
        Assert.Equal(41u, airport.UniqueRequestId);
        Assert.False(airport.IsListItem);
        Assert.Equal("Fixture Municipal", airport.AirportFacilityData!.Name);
        Assert.Equal("KAAA", airport.AirportFacilityData.Icao);

        SimConnectMessage? runway = null;
        SimConnectPackets.WithPointer(
            SimConnectPackets.RunwayFacility(
                900,
                uniqueRequestId: 42,
                parentUniqueRequestId: 41,
                itemIndex: 0,
                listSize: 1,
                lengthMeters: 1828.8f,
                widthMeters: 45.72f,
                surface: 4,
                primaryNumber: 18,
                primaryDesignator: 1,
                secondaryNumber: 36,
                secondaryDesignator: 2,
                primaryClosed: false,
                secondaryClosed: true),
            (data, size) => runway = SimConnectMessageDecoder.Decode(data, size));

        Assert.NotNull(runway);
        Assert.Equal(SimConnectFacilityDataType.Runway, runway!.FacilityDataType);
        Assert.Equal(42u, runway.UniqueRequestId);
        Assert.Equal(41u, runway.ParentUniqueRequestId);
        Assert.True(runway.IsListItem);
        Assert.Equal(0u, runway.ItemIndex);
        Assert.Equal(1u, runway.ListSize);
        Assert.Equal(1828.8f, runway.RunwayFacilityData!.LengthMeters);
        Assert.Equal(45.72f, runway.RunwayFacilityData.WidthMeters);
        Assert.Equal(4, runway.RunwayFacilityData.Surface);
        Assert.Equal(18, runway.RunwayFacilityData.PrimaryNumber);
        Assert.Equal(1, runway.RunwayFacilityData.PrimaryDesignator);
        Assert.Equal(36, runway.RunwayFacilityData.SecondaryNumber);
        Assert.Equal(2, runway.RunwayFacilityData.SecondaryDesignator);
        Assert.False(runway.RunwayFacilityData.PrimaryClosed);
        Assert.True(runway.RunwayFacilityData.SecondaryClosed);

        SimConnectMessage? end = null;
        SimConnectPackets.WithPointer(
            SimConnectPackets.FacilityDataEnd(900),
            (data, size) => end = SimConnectMessageDecoder.Decode(data, size));

        Assert.NotNull(end);
        Assert.Equal(SimConnectMessageKind.FacilityDataEnd, end!.Kind);
        Assert.Equal(900u, end.RequestId);
    }

    [Fact]
    public void DecoderRejectsTruncatedFacilityPacket()
    {
        byte[] packet = SimConnectPackets.RunwayFacility(
            requestId: 900,
            uniqueRequestId: 42,
            parentUniqueRequestId: 41,
            itemIndex: 0,
            listSize: 1,
            lengthMeters: 1828.8f,
            widthMeters: 45.72f,
            surface: 4,
            primaryNumber: 18,
            primaryDesignator: 0,
            secondaryNumber: 36,
            secondaryDesignator: 0);

        Array.Resize(ref packet, packet.Length - 4);
        BitConverter.GetBytes((uint)packet.Length).CopyTo(packet, 0);

        SimConnectPackets.WithPointer(
            packet,
            (data, size) =>
            {
                Assert.Throws<InvalidDataException>(
                    () => SimConnectMessageDecoder.Decode(data, size));
            });
    }

    [Fact]
    public async Task FacilityRequestUsesOwnedWorkerAndMapsAirportRunways()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectAirportDataObservationSource(connection, TimeProvider.System);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        await Until(() => api.FacilityDefinitions.Count == SimConnectAirportFacilityDefinition.Fields.Length);

        Task<AirportDataObservation?> lookup = source.FindAirportObservationAsync("kaaa");
        await Until(() => api.FacilityRequests.Count == 1);

        var request = Assert.Single(api.FacilityRequests);
        Assert.Equal(SimConnectAirportFacilityDefinition.DefinitionId, request.DefinitionId);
        Assert.Equal("KAAA", request.Icao);
        Assert.Equal(string.Empty, request.Region);

        Assert.Equal(
            SimConnectAirportFacilityDefinition.Fields,
            api.FacilityDefinitions
                .Select(static item => item.FieldName)
                .ToArray());

        api.Enqueue(SimConnectPackets.AirportFacility(
            request.RequestId,
            uniqueRequestId: 101,
            name: "Fixture Municipal",
            icao: "KAAA",
            latitudeDegrees: 43.1112,
            longitudeDegrees: -76.1063));
        api.Enqueue(SimConnectPackets.RunwayFacility(
            request.RequestId,
            uniqueRequestId: 102,
            parentUniqueRequestId: 101,
            itemIndex: 0,
            listSize: 2,
            lengthMeters: 1828.8f,
            widthMeters: 45.72f,
            surface: 4,
            primaryNumber: 18,
            primaryDesignator: 1,
            secondaryNumber: 36,
            secondaryDesignator: 2,
            headingTrueDegrees: 181.5f));
        api.Enqueue(SimConnectPackets.RunwayFacility(
            request.RequestId,
            uniqueRequestId: 103,
            parentUniqueRequestId: 101,
            itemIndex: 1,
            listSize: 2,
            lengthMeters: 1219.2f,
            widthMeters: 30.48f,
            surface: 0,
            primaryNumber: 9,
            primaryDesignator: 0,
            secondaryNumber: 27,
            secondaryDesignator: 0,
            primaryClosed: true,
            secondaryClosed: true,
            headingTrueDegrees: 90f));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(request.RequestId));

        AirportDataObservation result = Assert.IsType<AirportDataObservation>(await lookup);

        Assert.Equal(AirportDataAuthority.LocalSimulator, result.Provenance.Authority);
        Assert.Equal("msfs-simconnect-facility", result.Provenance.SourceId);
        Assert.Equal("KAAA", result.Airport.Icao);
        Assert.Equal("Fixture Municipal", result.Airport.Name);
        Assert.Equal(43.1112, result.Airport.LatitudeDegrees);
        Assert.Equal(-76.1063, result.Airport.LongitudeDegrees);

        Assert.Collection(
            result.Airport.Runways,
            runway =>
            {
                Assert.Equal("09/27", runway.Identifier);
                Assert.InRange(runway.UsableLengthFeet!.Value, 3999.9, 4000.1);
                Assert.InRange(runway.WidthFeet!.Value, 99.9, 100.1);
                Assert.Equal(RunwaySurface.Concrete, runway.Surface);
                Assert.True(runway.IsClosed);
                Assert.Collection(
                    Assert.IsAssignableFrom<IReadOnlyList<RunwayEndRecord>>(runway.Ends),
                    end =>
                    {
                        Assert.Equal("09", end.Identifier);
                        Assert.Equal(90, end.TrueHeadingDegrees, 6);
                        Assert.True(end.IsClosed);
                    },
                    end =>
                    {
                        Assert.Equal("27", end.Identifier);
                        Assert.Equal(270, end.TrueHeadingDegrees, 6);
                        Assert.True(end.IsClosed);
                    });
            },
            runway =>
            {
                Assert.Equal("18L/36R", runway.Identifier);
                Assert.InRange(runway.UsableLengthFeet!.Value, 5999.9, 6000.1);
                Assert.InRange(runway.WidthFeet!.Value, 149.9, 150.1);
                Assert.Equal(RunwaySurface.Asphalt, runway.Surface);
                Assert.False(runway.IsClosed);
                Assert.Collection(
                    Assert.IsAssignableFrom<IReadOnlyList<RunwayEndRecord>>(runway.Ends),
                    end =>
                    {
                        Assert.Equal("18L", end.Identifier);
                        Assert.Equal(181.5, end.TrueHeadingDegrees, 6);
                        Assert.False(end.IsClosed);
                    },
                    end =>
                    {
                        Assert.Equal("36R", end.Identifier);
                        Assert.Equal(1.5, end.TrueHeadingDegrees, 6);
                        Assert.False(end.IsClosed);
                    });
            });

        Assert.False(api.OverlapDetected);
        Assert.Single(api.ThreadIds);
    }

    [Fact]
    public async Task UnknownDimensionsAndSurfaceRemainUnknown()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectAirportDataObservationSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        Task<AirportDataObservation?> lookup = source.FindAirportObservationAsync("KAAA");
        await Until(() => api.FacilityRequests.Count == 1);
        var request = Assert.Single(api.FacilityRequests);

        api.Enqueue(SimConnectPackets.AirportFacility(
            request.RequestId,
            201,
            "Unknown Field Airport",
            "KAAA"));
        api.Enqueue(SimConnectPackets.RunwayFacility(
            request.RequestId,
            202,
            201,
            0,
            1,
            lengthMeters: 0,
            widthMeters: float.NaN,
            surface: 254,
            primaryNumber: 12,
            primaryDesignator: 0,
            secondaryNumber: 30,
            secondaryDesignator: 0));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(request.RequestId));

        AirportDataObservation observation = Assert.IsType<AirportDataObservation>(await lookup);
        RunwayRecord runway = Assert.Single(observation.Airport.Runways);

        Assert.Null(runway.UsableLengthFeet);
        Assert.Null(runway.WidthFeet);
        Assert.Equal(RunwaySurface.Unknown, runway.Surface);
    }

    [Fact]
    public async Task MismatchedFacilityRequestIdsAreIgnored()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectAirportDataObservationSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        Task<AirportDataObservation?> lookup = source.FindAirportObservationAsync("KAAA");
        await Until(() => api.FacilityRequests.Count == 1);
        var request = Assert.Single(api.FacilityRequests);
        uint wrongRequestId = request.RequestId + 1;

        api.Enqueue(SimConnectPackets.AirportFacility(
            wrongRequestId,
            401,
            "Wrong Airport",
            "KZZZ"));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(wrongRequestId));

        api.Enqueue(SimConnectPackets.AirportFacility(
            request.RequestId,
            402,
            "Correct Airport",
            "KAAA"));
        api.Enqueue(SimConnectPackets.RunwayFacility(
            request.RequestId,
            403,
            402,
            0,
            1,
            lengthMeters: 1000,
            widthMeters: 30,
            surface: 0,
            primaryNumber: 1,
            primaryDesignator: 0,
            secondaryNumber: 19,
            secondaryDesignator: 0));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(request.RequestId));

        AirportDataObservation observation = Assert.IsType<AirportDataObservation>(await lookup);
        Assert.Equal("KAAA", observation.Airport.Icao);
        Assert.Equal("Correct Airport", observation.Airport.Name);
        Assert.Single(observation.Airport.Runways);
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
    }

    [Fact]
    public async Task UnsupportedSurfaceRemainsUnknown()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectAirportDataObservationSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        Task<AirportDataObservation?> lookup = source.FindAirportObservationAsync("KAAA");
        await Until(() => api.FacilityRequests.Count == 1);
        var request = Assert.Single(api.FacilityRequests);

        api.Enqueue(SimConnectPackets.AirportFacility(
            request.RequestId,
            501,
            "Unsupported Surface Airport",
            "KAAA"));
        api.Enqueue(SimConnectPackets.RunwayFacility(
            request.RequestId,
            502,
            501,
            0,
            1,
            lengthMeters: 1000,
            widthMeters: 30,
            surface: 999,
            primaryNumber: 5,
            primaryDesignator: 0,
            secondaryNumber: 23,
            secondaryDesignator: 0));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(request.RequestId));

        AirportDataObservation observation = Assert.IsType<AirportDataObservation>(await lookup);
        Assert.Equal(RunwaySurface.Unknown, Assert.Single(observation.Airport.Runways).Surface);
    }

    [Fact]
    public async Task FacilityLookupCancellationDoesNotBreakConnectionOrNextRequest()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectAirportDataObservationSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        using var cancellation = new CancellationTokenSource();
        Task<AirportDataObservation?> canceledLookup =
            source.FindAirportObservationAsync("KAAA", cancellation.Token);
        await Until(() => api.FacilityRequests.Count == 1);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
            {
                await canceledLookup;
            });
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);

        Task<AirportDataObservation?> retry = source.FindAirportObservationAsync("KBBB");
        await Until(() => api.FacilityRequests.Count == 2);
        var retryRequest = api.FacilityRequests.Last();

        api.Enqueue(SimConnectPackets.AirportFacility(
            retryRequest.RequestId,
            601,
            "Retry Airport",
            "KBBB"));
        api.Enqueue(SimConnectPackets.RunwayFacility(
            retryRequest.RequestId,
            602,
            601,
            0,
            1,
            lengthMeters: 1200,
            widthMeters: 30,
            surface: 4,
            primaryNumber: 9,
            primaryDesignator: 0,
            secondaryNumber: 27,
            secondaryDesignator: 0));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(retryRequest.RequestId));

        AirportDataObservation observation = Assert.IsType<AirportDataObservation>(await retry);
        Assert.Equal("KBBB", observation.Airport.Icao);
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
    }

    [Fact]
    public async Task FacilityLookupTimeoutReturnsUnavailableWithoutDroppingConnection()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));

        await using var connection = Create(api, clock);
        var source = new SimConnectAirportDataObservationSource(connection, clock);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        Task<AirportDataObservation?> lookup = source.FindAirportObservationAsync("KAAA");
        await Until(() => api.FacilityRequests.Count == 1);

        clock.Advance(SimConnectAirportFacilityDefinition.ResponseTimeout + TimeSpan.FromSeconds(1));

        Assert.Null(await lookup.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
    }

    [Fact]
    public async Task ImmediateFacilityFailureReturnsNullWithoutDroppingTelemetryConnection()
    {
        var api = new SimConnectTestTransport
        {
            FacilityRequestResult = SimConnectTestTransport.Failure
        };
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectAirportDataObservationSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        AirportDataObservation? result = await source.FindAirportObservationAsync("KAAA");

        Assert.Null(result);
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
        Assert.Single(api.FacilityRequests);
    }

    [Fact]
    public async Task AsyncFacilityExceptionReturnsNullWithoutDroppingTelemetryConnection()
    {
        var api = new SimConnectTestTransport
        {
            LastSentPacketId = 812
        };
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectAirportDataObservationSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        Task<AirportDataObservation?> lookup = source.FindAirportObservationAsync("KAAA");
        await Until(() => api.FacilityRequests.Count == 1);

        api.Enqueue(SimConnectPackets.Exception(
            code: 23,
            sendId: api.LastSentPacketId,
            parameterIndex: 0));

        Assert.Null(await lookup);
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
    }

    [Fact]
    public async Task FacilityDefinitionFailureDoesNotPreventConnection()
    {
        var api = new SimConnectTestTransport
        {
            FacilityDefinitionResult = SimConnectTestTransport.Failure
        };
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectAirportDataObservationSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        AirportDataObservation? result = await source.FindAirportObservationAsync("KAAA");

        Assert.Null(result);
        Assert.Empty(api.FacilityRequests);
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
    }

    [Fact]
    public async Task IncompleteRunwayListFailsClosedAtEndMarker()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectAirportDataObservationSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        Task<AirportDataObservation?> lookup = source.FindAirportObservationAsync("KAAA");
        await Until(() => api.FacilityRequests.Count == 1);
        var request = Assert.Single(api.FacilityRequests);

        api.Enqueue(SimConnectPackets.AirportFacility(
            request.RequestId,
            301,
            "Incomplete Airport",
            "KAAA"));
        api.Enqueue(SimConnectPackets.RunwayFacility(
            request.RequestId,
            302,
            301,
            itemIndex: 0,
            listSize: 2,
            lengthMeters: 1000,
            widthMeters: 30,
            surface: 0,
            primaryNumber: 1,
            primaryDesignator: 0,
            secondaryNumber: 19,
            secondaryDesignator: 0));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(request.RequestId));

        Assert.Null(await lookup);
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
    }

    [Fact]
    public async Task DisconnectCompletesPendingFacilityLookupAsUnavailable()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectAirportDataObservationSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        Task<AirportDataObservation?> lookup = source.FindAirportObservationAsync("KAAA");
        await Until(() => api.FacilityRequests.Count == 1);

        api.Enqueue(SimConnectPackets.Header(3));

        Assert.Null(await lookup);
        await Until(() => connection.Current.State != SimulatorConnectionState.Connected);
    }

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

    private static async Task Until(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(5, deadline.Token);
    }
}
