using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Airports;
using OpenCareer.Infrastructure.Airports;
using OpenCareer.SimConnect;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.Tests;

public sealed class SimConnectAirportFacilityTests
{
    // Official retail MSFS 2024 SDK Core 1.7.3, SimConnect.h, lines 467 and 725-735:
    // https://sdk.flightsimulator.com/msfs2024/files/installers/1.7.3/MSFS2024_SDK_Core_Installer_1.7.3.zip
    // pack(1); IsListItem is DWORD. The HTML page's bool declaration is not the SDK header.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SdkFacilityHeader
    {
        public uint Size, Version, Id;
        public uint UserRequestId, UniqueRequestId, ParentUniqueRequestId, Type;
        public uint IsListItem, ItemIndex, ListSize, Data;
    }

    [Fact]
    public void FacilityPacketOffsetsMatchOfficialSdkHeaderAndRequestedFieldTypes()
    {
        Assert.Equal(28u, (uint)SimConnectMessageKind.FacilityData);
        Assert.Equal(29u, (uint)SimConnectMessageKind.FacilityDataEnd);
        Assert.Equal(38u, (uint)SimConnectMessageKind.EnumerateSimObjectAndLiveryList);
        Assert.Equal(28, Marshal.OffsetOf<SdkFacilityHeader>(nameof(SdkFacilityHeader.IsListItem)).ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<SdkFacilityHeader>(nameof(SdkFacilityHeader.ItemIndex)).ToInt32());
        Assert.Equal(36, Marshal.OffsetOf<SdkFacilityHeader>(nameof(SdkFacilityHeader.ListSize)).ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<SdkFacilityHeader>(nameof(SdkFacilityHeader.Data)).ToInt32());
        // AddToFacilityDefinition: two FLOAT64 + STRING64 + STRING8; runway:
        // two FLOAT64 + three FLOAT32 + five INT32 + two INT8 (no trailing padding).
        Assert.Equal(40 + 88, SimConnectPackets.AirportFacility(1, 2, "Airport", "KALB").Length);
        Assert.Equal(40 + 50, SimConnectPackets.RunwayFacility(1, 3, 2, 0, 1, 1000, 20, 4, 1, 0, 19, 0).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedOrUnsupportedActiveFacilityResponseFailsOnlyAirportQuery(bool unsupportedType)
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());
        await using var connection = Create(api);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        api.Enqueue(SimConnectPackets.StringSimObjectData(SimConnectCurrentAircraftDefinition.RequestId,
            SimConnectCurrentAircraftDefinition.DefinitionId, "C172SP Classic Passengers"));
        var initialTelemetry = new double[SimConnectTelemetryDefinition.ValueCount];
        initialTelemetry[(int)SimConnectTelemetryValue.SimulationRate] = 1;
        api.Enqueue(SimConnectPackets.SimObjectData(SimConnectTelemetryDefinition.RequestId,
            SimConnectTelemetryDefinition.DefinitionId, initialTelemetry));
        await Until(() => connection.CurrentAircraftTitle is not null && connection.Latest is not null);

        var source = new SimConnectAirportDataObservationSource(connection);
        var query = source.FindAirportObservationAsync("KALB");
        await Until(() => api.FacilityRequests.Count == 1);
        uint requestId = api.FacilityRequests.Single().RequestId;
        byte[] packet = SimConnectPackets.AirportFacility(requestId, 1, "Fixture Albany", "KALB");
        if (unsupportedType)
            BitConverter.GetBytes(999u).CopyTo(packet, 24);
        else
        {
            Array.Resize(ref packet, packet.Length - 1);
            BitConverter.GetBytes((uint)packet.Length).CopyTo(packet, 0);
        }
        api.Enqueue(packet);

        Assert.Null(await query.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
        Assert.Equal(1, api.Attempts);
        var values = new double[SimConnectTelemetryDefinition.ValueCount];
        values[(int)SimConnectTelemetryValue.HeadingTrue] = 123;
        values[(int)SimConnectTelemetryValue.SimulationRate] = 1;
        api.Enqueue(SimConnectPackets.SimObjectData(SimConnectTelemetryDefinition.RequestId,
            SimConnectTelemetryDefinition.DefinitionId, values));
        await Until(() => connection.Latest?.HeadingDegrees == 123);
        Assert.Equal("C172SP Classic Passengers", connection.CurrentAircraftTitle);
        Assert.False(api.OverlapDetected);
        Assert.Single(api.ThreadIds);

        // A malformed late response/exception from the failed query must not poison its successor.
        api.LastSentPacketId++;
        var next = source.FindAirportObservationAsync("KJFK");
        await Until(() => api.FacilityRequests.Count == 2);
        uint nextId = api.FacilityRequests.Last().RequestId;
        api.Enqueue(packet);
        api.Enqueue(SimConnectPackets.Exception(20, api.LastSentPacketId - 1));
        api.Enqueue(SimConnectPackets.AirportFacility(nextId, 2, "Kennedy", "KJFK"));
        api.Enqueue(SimConnectPackets.RunwayFacility(nextId, 3, 2, 0, 1, 3000, 45, 4, 4, 1, 22, 2));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(nextId));
        Assert.NotNull(await next.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, api.Attempts);
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
    }

    [Fact]
    public async Task FailedFacilityLookupIsSuppressedPerIcaoAndConcurrentRetryIsCoalesced()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));

        await using var connection = Create(api);
        var live = new SimConnectAirportDataObservationSource(connection, clock);
        var airports = new CachedAirportDataSource(
            [live, new PlayableLoopReferenceAirportObservationSource(clock)],
            clock: clock);

        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        Task<AirportDataObservation?> failed = live.FindAirportObservationAsync("KJFK");
        await Until(() => api.FacilityRequests.Count == 1);
        EnqueueTruncatedAirportFacility(
            api,
            api.FacilityRequests.Single().RequestId,
            "KJFK");

        Assert.Null(await failed.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
        Assert.Equal(1, api.Attempts);

        for (int index = 0; index < 5; index++)
        {
            AirportDataObservation fallback = Assert.IsType<AirportDataObservation>(
                await airports.FindAirportObservationAsync("KJFK"));
            Assert.Equal(AirportDataAuthority.Reference, fallback.Provenance.Authority);
        }
        Assert.Single(api.FacilityRequests);

        Task<AirportDataObservation?> independent = live.FindAirportObservationAsync("KALB");
        await Until(() => api.FacilityRequests.Count == 2);
        CompleteValidAirportFacility(api, api.FacilityRequests.Last().RequestId, "KALB");
        Assert.NotNull(await independent.WaitAsync(TimeSpan.FromSeconds(5)));

        clock.Advance(SimConnectAirportDataObservationSource.FailureCooldown - TimeSpan.FromTicks(1));
        Assert.Null(await live.FindAirportObservationAsync("KJFK"));
        Assert.Equal(2, api.FacilityRequests.Count);

        clock.Advance(TimeSpan.FromTicks(1));
        Task<AirportDataObservation?>[] retries =
        [
            live.FindAirportObservationAsync("KJFK"),
            live.FindAirportObservationAsync("KJFK"),
            live.FindAirportObservationAsync("KJFK")
        ];

        await Until(() => api.FacilityRequests.Count == 3);
        CompleteValidAirportFacility(api, api.FacilityRequests.Last().RequestId, "KJFK");
        await Until(() => retries.All(static task => task.IsCompleted)
            || api.FacilityRequests.Count > 3);
        Assert.Equal(3, api.FacilityRequests.Count);
        AirportDataObservation?[] retryResults = await Task.WhenAll(retries);
        Assert.All(retryResults, static result => Assert.NotNull(result));

        Task<AirportDataObservation?> afterSuccess = live.FindAirportObservationAsync("KJFK");
        await Until(() => api.FacilityRequests.Count == 4);
        CompleteValidAirportFacility(api, api.FacilityRequests.Last().RequestId, "KJFK");
        Assert.NotNull(await afterSuccess.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
        Assert.Equal(1, api.Attempts);
    }

    [Fact]
    public async Task ConcurrentFailedFacilityLookupsQueueOneNativeRequest()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectAirportDataObservationSource(connection);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        Task<AirportDataObservation?>[] lookups = Enumerable.Range(0, 8)
            .Select(_ => source.FindAirportObservationAsync("KJFK"))
            .ToArray();

        await Until(() => api.FacilityRequests.Count == 1);
        var request = Assert.Single(api.FacilityRequests);
        EnqueueTruncatedAirportFacility(api, request.RequestId, "KJFK");

        await Until(() => lookups.All(static task => task.IsCompleted)
            || api.FacilityRequests.Count > 1);
        Assert.Single(api.FacilityRequests);
        AirportDataObservation?[] results = await Task.WhenAll(lookups);
        Assert.All(results, static result => Assert.Null(result));
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
    }

    [Fact]
    public async Task MalformedCorePacketStillReconnectsWhileAirportQueryIsPending()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());
        await using var connection = Create(api);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        var query = connection.RequestAirportFacilityAsync("KALB", CancellationToken.None);
        await Until(() => api.FacilityRequests.Count == 1);
        api.Enqueue(SimConnectPackets.Header(8)); // truncated core SIMOBJECT_DATA, not facility data
        Assert.Null(await query.WaitAsync(TimeSpan.FromSeconds(5)));
        await Until(() => api.Attempts == 2);
        Assert.Equal(1, api.Closed);
        api.Enqueue(SimConnectPackets.Open());
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        Assert.Equal(2, api.Attempts);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(91)]
    public async Task InvalidFacilityCoordinatesReturnUnavailableWithoutDisconnect(double latitude)
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());
        await using var connection = Create(api);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);
        var query = new SimConnectAirportDataObservationSource(connection).FindAirportObservationAsync("KALB");
        await Until(() => api.FacilityRequests.Count == 1);
        uint requestId = api.FacilityRequests.Single().RequestId;
        api.Enqueue(SimConnectPackets.AirportFacility(requestId, 1, "Airport", "KALB", latitude));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(requestId));
        Assert.Null(await query.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
        Assert.Equal(1, api.Attempts);
    }

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
    public void TruncatedAirportDiagnosticRetainsOnlySafeEnvelopeMetadata()
    {
        byte[] packet = SimConnectPackets.AirportFacility(
            requestId: 900,
            uniqueRequestId: 41,
            name: "Private fixture airport name",
            icao: "KAAA");
        Array.Resize(ref packet, packet.Length - 1);
        BitConverter.GetBytes((uint)packet.Length).CopyTo(packet, 0);

        SimConnectFacilityDecodeDiagnostic diagnostic = ReadDecodeDiagnostic(packet);

        Assert.Equal(127u, diagnostic.CallbackBufferSize);
        Assert.Equal(127u, diagnostic.DeclaredRecvSize);
        Assert.Equal((uint)SimConnectMessageKind.FacilityData, diagnostic.MessageKind);
        Assert.Equal(900u, diagnostic.RequestId);
        Assert.Equal(41u, diagnostic.UniqueRequestId);
        Assert.Equal(0u, diagnostic.ParentUniqueRequestId);
        Assert.Equal((uint)SimConnectFacilityDataType.Airport, diagnostic.FacilityType);
        Assert.Equal(0u, diagnostic.IsListItem);
        Assert.Equal(0u, diagnostic.ItemIndex);
        Assert.Equal(0u, diagnostic.ListSize);
        Assert.Equal(128u, diagnostic.RequiredMinimumSize);
        Assert.Equal("Truncated SimConnect message.", diagnostic.FailureReason);
    }

    [Fact]
    public void TruncatedRunwayDiagnosticRetainsOnlySafeEnvelopeMetadata()
    {
        byte[] packet = SimConnectPackets.RunwayFacility(
            requestId: 901,
            uniqueRequestId: 42,
            parentUniqueRequestId: 41,
            itemIndex: 2,
            listSize: 3,
            lengthMeters: 1828.8f,
            widthMeters: 45.72f,
            surface: 4,
            primaryNumber: 18,
            primaryDesignator: 0,
            secondaryNumber: 36,
            secondaryDesignator: 0);
        Array.Resize(ref packet, packet.Length - 1);
        BitConverter.GetBytes((uint)packet.Length).CopyTo(packet, 0);

        SimConnectFacilityDecodeDiagnostic diagnostic = ReadDecodeDiagnostic(packet);

        Assert.Equal(89u, diagnostic.CallbackBufferSize);
        Assert.Equal(89u, diagnostic.DeclaredRecvSize);
        Assert.Equal((uint)SimConnectMessageKind.FacilityData, diagnostic.MessageKind);
        Assert.Equal(901u, diagnostic.RequestId);
        Assert.Equal(42u, diagnostic.UniqueRequestId);
        Assert.Equal(41u, diagnostic.ParentUniqueRequestId);
        Assert.Equal((uint)SimConnectFacilityDataType.Runway, diagnostic.FacilityType);
        Assert.Equal(1u, diagnostic.IsListItem);
        Assert.Equal(2u, diagnostic.ItemIndex);
        Assert.Equal(3u, diagnostic.ListSize);
        Assert.Equal(90u, diagnostic.RequiredMinimumSize);
        Assert.Equal("Truncated SimConnect message.", diagnostic.FailureReason);
    }

    [Theory]
    [InlineData(16, false, false, false, false, false, false)]
    [InlineData(19, false, false, false, false, false, false)]
    [InlineData(20, true, false, false, false, false, false)]
    [InlineData(24, true, true, false, false, false, false)]
    [InlineData(28, true, true, true, false, false, false)]
    [InlineData(32, true, true, true, true, false, false)]
    [InlineData(36, true, true, true, true, true, false)]
    [InlineData(40, true, true, true, true, true, true)]
    public void DiagnosticNeverReadsPastCallbackBuffer(
        uint callbackBufferSize,
        bool hasUnique,
        bool hasParent,
        bool hasType,
        bool hasListFlag,
        bool hasItemIndex,
        bool hasListSize)
    {
        byte[] packet = SimConnectPackets.RunwayFacility(
            requestId: 901,
            uniqueRequestId: 42,
            parentUniqueRequestId: 41,
            itemIndex: 2,
            listSize: 3,
            lengthMeters: 1828.8f,
            widthMeters: 45.72f,
            surface: 4,
            primaryNumber: 18,
            primaryDesignator: 0,
            secondaryNumber: 36,
            secondaryDesignator: 0);

        SimConnectFacilityDecodeDiagnostic? diagnostic = null;
        SimConnectPackets.WithPointer(
            packet,
            (data, _) =>
            {
                Assert.True(SimConnectMessageDecoder.TryCreateFacilityDecodeDiagnostic(
                    data,
                    callbackBufferSize,
                    "test failure",
                    out diagnostic));
            });

        Assert.NotNull(diagnostic);
        Assert.Equal(callbackBufferSize, diagnostic!.CallbackBufferSize);
        Assert.Equal(hasUnique, diagnostic.UniqueRequestId.HasValue);
        Assert.Equal(hasParent, diagnostic.ParentUniqueRequestId.HasValue);
        Assert.Equal(hasType, diagnostic.FacilityType.HasValue);
        Assert.Equal(hasListFlag, diagnostic.IsListItem.HasValue);
        Assert.Equal(hasItemIndex, diagnostic.ItemIndex.HasValue);
        Assert.Equal(hasListSize, diagnostic.ListSize.HasValue);
    }

    [Fact]
    public void DiagnosticExtractionWithoutSafeRequestPrefixFailsWithoutThrowing()
    {
        Assert.False(SimConnectMessageDecoder.TryCreateFacilityDecodeDiagnostic(
            nint.Zero,
            uint.MaxValue,
            "test failure",
            out SimConnectFacilityDecodeDiagnostic? nullPointerDiagnostic));
        Assert.Null(nullPointerDiagnostic);

        byte[] packet = SimConnectPackets.AirportFacility(900, 41, "Airport", "KAAA");
        SimConnectPackets.WithPointer(
            packet,
            (data, _) =>
            {
                Assert.False(SimConnectMessageDecoder.TryCreateFacilityDecodeDiagnostic(
                    data,
                    15,
                    "test failure",
                    out SimConnectFacilityDecodeDiagnostic? truncatedPrefixDiagnostic));
                Assert.Null(truncatedPrefixDiagnostic);
            });
    }

    [Fact]
    public async Task MalformedFacilityLogsBoundedEnvelopeAndRetainsConnection()
    {
        var api = new SimConnectTestTransport();
        var logger = new CollectingLogger<SimConnectConnection>();
        api.Enqueue(SimConnectPackets.Open());
        await using var connection = Create(api, logger: logger);
        connection.Start();
        await Until(() => connection.Current.State == SimulatorConnectionState.Connected);

        Task<SimConnectAirportFacilitySnapshot?> query =
            connection.RequestAirportFacilityAsync("KAAA", CancellationToken.None);
        await Until(() => api.FacilityRequests.Count == 1);
        byte[] packet = SimConnectPackets.AirportFacility(
            api.FacilityRequests.Single().RequestId,
            uniqueRequestId: 41,
            name: "Private fixture airport name",
            icao: "KAAA",
            latitudeDegrees: 12.3456789,
            longitudeDegrees: -98.7654321);
        Array.Resize(ref packet, packet.Length - 1);
        BitConverter.GetBytes((uint)packet.Length).CopyTo(packet, 0);
        api.Enqueue(packet);

        Assert.Null(await query.WaitAsync(TimeSpan.FromSeconds(5)));
        await Until(() => logger.Entries.Any(static entry =>
            entry.Properties.ContainsKey("RequiredMinimumSize")));
        LogEntry entry = Assert.Single(logger.Entries, static entry =>
            entry.Properties.ContainsKey("RequiredMinimumSize"));
        Assert.Equal(127u, entry.Properties["CallbackBufferSize"]);
        Assert.Equal(127u, entry.Properties["DeclaredRecvSize"]);
        Assert.Equal(128u, entry.Properties["RequiredMinimumSize"]);
        Assert.Equal("Truncated SimConnect message.", entry.Properties["FailureReason"]);
        Assert.DoesNotContain("Private fixture airport name", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("12.3456789", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("-98.7654321", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Latitude", entry.Properties.Keys);
        Assert.DoesNotContain("Longitude", entry.Properties.Keys);
        Assert.DoesNotContain("Payload", entry.Properties.Keys);
        Assert.Equal(SimulatorConnectionState.Connected, connection.Current.State);
        Assert.Equal(1, api.Attempts);
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
    public async Task FacilityLookupCancellationDoesNotStartCooldownOrBreakConnection()
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

        Task<AirportDataObservation?> retry = source.FindAirportObservationAsync("KAAA");
        await Until(() => api.FacilityRequests.Count == 2);
        var retryRequest = api.FacilityRequests.Last();

        api.Enqueue(SimConnectPackets.AirportFacility(
            retryRequest.RequestId,
            601,
            "Retry Airport",
            "KAAA"));
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
        Assert.Equal("KAAA", observation.Airport.Icao);
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
        TimeProvider? clock = null,
        ILogger<SimConnectConnection>? logger = null) =>
        new(
            api,
            logger ?? NullLogger<SimConnectConnection>.Instance,
            new SimConnectConnectionOptions
            {
                InitialRetryDelay = TimeSpan.FromMilliseconds(10),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(10),
                DispatchInterval = TimeSpan.FromMilliseconds(5)
            },
            clock ?? TimeProvider.System);

    private static SimConnectFacilityDecodeDiagnostic ReadDecodeDiagnostic(byte[] packet)
    {
        SimConnectFacilityDecodeDiagnostic? diagnostic = null;
        SimConnectPackets.WithPointer(
            packet,
            (data, size) =>
            {
                InvalidDataException exception = Assert.Throws<InvalidDataException>(
                    () => SimConnectMessageDecoder.Decode(data, size));
                Assert.True(SimConnectMessageDecoder.TryCreateFacilityDecodeDiagnostic(
                    data,
                    size,
                    exception.Message,
                    out diagnostic));
            });

        return Assert.IsType<SimConnectFacilityDecodeDiagnostic>(diagnostic);
    }

    private static void EnqueueTruncatedAirportFacility(
        SimConnectTestTransport api,
        uint requestId,
        string icao)
    {
        byte[] packet = SimConnectPackets.AirportFacility(
            requestId,
            uniqueRequestId: 1,
            name: "Truncated Airport",
            icao: icao);
        Array.Resize(ref packet, packet.Length - 1);
        BitConverter.GetBytes((uint)packet.Length).CopyTo(packet, 0);
        api.Enqueue(packet);
    }

    private static void CompleteValidAirportFacility(
        SimConnectTestTransport api,
        uint requestId,
        string icao)
    {
        api.Enqueue(SimConnectPackets.AirportFacility(
            requestId,
            uniqueRequestId: 10,
            name: $"Fixture {icao}",
            icao: icao));
        api.Enqueue(SimConnectPackets.RunwayFacility(
            requestId,
            uniqueRequestId: 11,
            parentUniqueRequestId: 10,
            itemIndex: 0,
            listSize: 1,
            lengthMeters: 3000,
            widthMeters: 45,
            surface: 4,
            primaryNumber: 4,
            primaryDesignator: 0,
            secondaryNumber: 22,
            secondaryDesignator: 0));
        api.Enqueue(SimConnectPackets.FacilityDataEnd(requestId));
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

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state as IEnumerable<KeyValuePair<string, object?>>;
            Entries.Enqueue(new(
                formatter(state, exception),
                properties?.ToDictionary(static pair => pair.Key, static pair => pair.Value)
                    ?? new Dictionary<string, object?>()));
        }
    }

    private sealed record LogEntry(
        string Message,
        IReadOnlyDictionary<string, object?> Properties);

    private static async Task Until(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(5, deadline.Token);
    }
}
