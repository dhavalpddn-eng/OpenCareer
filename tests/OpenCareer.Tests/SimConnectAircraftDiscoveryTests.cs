using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.SimConnect;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.Tests;

public sealed class SimConnectAircraftDiscoveryTests
{
    [Fact]
    public void DecoderReadsPagedAircraftAndLiveryEntries()
    {
        byte[] packet = SimConnectPackets.EnumeratedSimObjects(
            SimConnectAircraftCatalog.RequestId,
            entryNumber: 1,
            outOf: 3,
            ("DA62 Asobo", "Default"),
            ("Cabri G2", "Blue"));

        SimConnectMessage? decoded = null;
        SimConnectPackets.WithPointer(
            packet,
            (data, size) => decoded = SimConnectMessageDecoder.Decode(data, size));

        Assert.NotNull(decoded);
        Assert.Equal(SimConnectMessageKind.EnumerateSimObjectAndLiveryList, decoded!.Kind);
        Assert.Equal(SimConnectAircraftCatalog.RequestId, decoded.RequestId);
        Assert.Equal(1u, decoded.ListEntryNumber);
        Assert.Equal(3u, decoded.ListOutOf);
        Assert.Collection(
            decoded.ObjectLiveries!,
            item =>
            {
                Assert.Equal("DA62 Asobo", item.AircraftTitle);
                Assert.Equal("Default", item.LiveryName);
            },
            item =>
            {
                Assert.Equal("Cabri G2", item.AircraftTitle);
                Assert.Equal("Blue", item.LiveryName);
            });
    }

    [Fact]
    public async Task ConnectionEnumeratesFullAircraftCatalogOnItsOwnedWorker()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectInstalledAircraftObservationSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == OpenCareer.Application.Simulator.SimulatorConnectionState.Connected);
        await Until(() => api.AircraftEnumerations.Count == 1);

        var request = Assert.Single(api.AircraftEnumerations);
        Assert.Equal(SimConnectAircraftCatalog.RequestId, request.RequestId);
        Assert.Equal(SimConnectSimObjectType.Aircraft, request.Type);
        Assert.Equal(InstalledAircraftDiscoveryAvailability.Unavailable, source.Current.Availability);

        api.Enqueue(SimConnectPackets.EnumeratedSimObjects(
            request.RequestId,
            entryNumber: 1,
            outOf: 2,
            ("DA62 Asobo", "Red"),
            ("A320neo", "Default")));

        api.Enqueue(SimConnectPackets.EnumeratedSimObjects(
            request.RequestId,
            entryNumber: 0,
            outOf: 2,
            ("DA62 Asobo", "Default")));

        await Until(() => source.Current.Availability == InstalledAircraftDiscoveryAvailability.Available);

        Assert.Collection(
            source.Current.Observations,
            observation =>
            {
                Assert.Equal("msfs-title:A320neo", observation.CanonicalAircraftId);
                Assert.Equal("A320neo", observation.DisplayName);
                Assert.True(observation.IsInstalled);
                Assert.Equal(AircraftDataConfidence.Verified, observation.Confidence);
            },
            observation =>
            {
                Assert.Equal("msfs-title:DA62 Asobo", observation.CanonicalAircraftId);
                Assert.Equal("DA62 Asobo", observation.DisplayName);
            });

        Assert.False(api.OverlapDetected);
        Assert.Single(api.ThreadIds);
    }

    [Fact]
    public async Task CurrentAircraftTitleMakesDiscoveryAvailableWhenCatalogNeverResponds()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectInstalledAircraftObservationSource(connection);

        connection.Start();

        await Until(() =>
            api.StringDataDefinitions.Any(
                static item =>
                    item.DefinitionId == SimConnectCurrentAircraftDefinition.DefinitionId
                    && item.DatumName == SimConnectCurrentAircraftDefinition.TitleSimVar));

        api.Enqueue(
            SimConnectPackets.StringSimObjectData(
                SimConnectCurrentAircraftDefinition.RequestId,
                SimConnectCurrentAircraftDefinition.DefinitionId,
                "Cessna 172 Skyhawk"));

        await Until(() =>
            source.Current.Availability
                == InstalledAircraftDiscoveryAvailability.Available);

        AircraftRegistryObservation aircraft =
            Assert.Single(source.Current.Observations);

        Assert.Equal(
            "msfs-title:Cessna 172 Skyhawk",
            aircraft.CanonicalAircraftId);
        Assert.Equal(
            "Cessna 172 Skyhawk",
            aircraft.DisplayName);
        Assert.True(aircraft.IsInstalled);
        Assert.Equal(
            AircraftDataConfidence.Verified,
            aircraft.Confidence);
        Assert.Equal(
            OpenCareer.Application.Simulator.SimulatorConnectionState.Connected,
            connection.Current.State);
    }

    [Fact]
    public async Task LiveriesDoNotCreateDuplicateAircraftRegistryEntries()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectInstalledAircraftObservationSource(connection);

        connection.Start();
        await Until(() => api.AircraftEnumerations.Count == 1);

        api.Enqueue(SimConnectPackets.EnumeratedSimObjects(
            SimConnectAircraftCatalog.RequestId,
            0,
            1,
            ("A320neo", "Default"),
            ("A320neo", "Airline A"),
            ("A320neo", "Airline B")));

        await Until(() => source.Current.Availability == InstalledAircraftDiscoveryAvailability.Available);
        Assert.Single(source.Current.Observations);
    }

    [Fact]
    public async Task DisconnectMakesInstalledAircraftDiscoveryUnavailable()
    {
        var api = new SimConnectTestTransport();
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectInstalledAircraftObservationSource(connection);

        connection.Start();
        await Until(() => api.AircraftEnumerations.Count == 1);
        api.Enqueue(SimConnectPackets.EnumeratedSimObjects(
            SimConnectAircraftCatalog.RequestId,
            0,
            1,
            ("DA62 Asobo", "Default")));
        await Until(() => source.Current.Availability == InstalledAircraftDiscoveryAvailability.Available);

        api.Enqueue(SimConnectPackets.Header(3));
        await Until(() => source.Current.Availability == InstalledAircraftDiscoveryAvailability.Unavailable);
    }

    [Fact]
    public async Task EnumerationRequestFailureDoesNotBreakTelemetryConnection()
    {
        var api = new SimConnectTestTransport
        {
            AircraftEnumerationResult = SimConnectTestTransport.Failure
        };
        api.Enqueue(SimConnectPackets.Open());

        await using var connection = Create(api);
        var source = new SimConnectInstalledAircraftObservationSource(connection);

        connection.Start();
        await Until(() => connection.Current.State == OpenCareer.Application.Simulator.SimulatorConnectionState.Connected);
        await Until(() => api.AircraftEnumerations.Count == 1);

        Assert.Equal(InstalledAircraftDiscoveryAvailability.Unavailable, source.Current.Availability);
        Assert.Equal(OpenCareer.Application.Simulator.SimulatorConnectionState.Connected, connection.Current.State);
    }

    [Fact]
    public void DecoderRejectsInvalidEnumerationPagination()
    {
        byte[] packet = SimConnectPackets.EnumeratedSimObjects(
            SimConnectAircraftCatalog.RequestId,
            entryNumber: 1,
            outOf: 1,
            ("DA62 Asobo", "Default"));

        Assert.Throws<InvalidDataException>(
            () => SimConnectPackets.WithPointer(
                packet,
                (data, size) => SimConnectMessageDecoder.Decode(data, size)));
    }

    private static SimConnectConnection Create(SimConnectTestTransport api) =>
        new(
            api,
            NullLogger<SimConnectConnection>.Instance,
            new SimConnectConnectionOptions
            {
                InitialRetryDelay = TimeSpan.FromMilliseconds(10),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(10),
                DispatchInterval = TimeSpan.FromMilliseconds(5)
            },
            TimeProvider.System);

    private static async Task Until(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(5, deadline.Token);
    }
}
