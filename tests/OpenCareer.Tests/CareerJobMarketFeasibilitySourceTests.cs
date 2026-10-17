using OpenCareer.Application.Careers;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class CareerJobMarketFeasibilitySourceTests
{
    [Fact]
    public async Task RetainsOnlyDestinationsWithinInstalledCivilianCapability()
    {
        var registry = new StubRegistry(
            ("aircraft-a", Resolution(
                "aircraft-a",
                AircraftInstallationStatus.Installed,
                AircraftAccess.Civilian,
                maximumRangeNauticalMiles: 100)));
        var source = Source(
            AvailableDiscovery(Observation("aircraft-a")),
            registry);

        CareerJobMarketFeasibilitySnapshot result = await source.FilterAsync(
            "KORG",
            [Destination("KAAA", 75), Destination("KBBB", 125)]);

        Assert.Equal(CareerJobMarketFeasibilityStatus.Ready, result.Status);
        JobMarketDestination feasible = Assert.Single(result.Destinations);
        Assert.Equal("KAAA", feasible.Icao);
        Assert.Equal(["aircraft-a"], registry.Requests);
    }

    [Fact]
    public async Task DiscoveryUnavailableFailsClosedWithoutRegistryReads()
    {
        var registry = new StubRegistry();
        var source = Source(
            InstalledAircraftDiscoverySnapshot.Unavailable,
            registry);

        CareerJobMarketFeasibilitySnapshot result = await source.FilterAsync(
            "KORG",
            [Destination("KAAA", 75)]);

        Assert.Equal(
            CareerJobMarketFeasibilityStatus.AircraftDiscoveryUnavailable,
            result.Status);
        Assert.Empty(result.Destinations);
        Assert.Empty(registry.Requests);
    }

    [Fact]
    public async Task MissingRangeEvidenceCannotCreateAFeasibleRoute()
    {
        var source = Source(
            AvailableDiscovery(Observation("aircraft-a")),
            new StubRegistry(("aircraft-a", Resolution(
                "aircraft-a",
                AircraftInstallationStatus.Installed,
                AircraftAccess.Civilian,
                maximumRangeNauticalMiles: null))));

        CareerJobMarketFeasibilitySnapshot result = await source.FilterAsync(
            "KORG",
            [Destination("KAAA", 1)]);

        Assert.Equal(
            CareerJobMarketFeasibilityStatus.NoFeasibleDestinations,
            result.Status);
        Assert.Empty(result.Destinations);
    }

    [Fact]
    public async Task NonInstalledOrNonCivilianRegistryEvidenceCannotQualify()
    {
        var source = Source(
            AvailableDiscovery(
                Observation("known-only"),
                Observation("military")),
            new StubRegistry(
                ("known-only", Resolution(
                    "known-only",
                    AircraftInstallationStatus.KnownOnly,
                    AircraftAccess.Civilian,
                    500)),
                ("military", Resolution(
                    "military",
                    AircraftInstallationStatus.Installed,
                    AircraftAccess.Military,
                    500))));

        CareerJobMarketFeasibilitySnapshot result = await source.FilterAsync(
            "KORG",
            [Destination("KAAA", 75)]);

        Assert.Equal(
            CareerJobMarketFeasibilityStatus.NoFeasibleDestinations,
            result.Status);
        Assert.Empty(result.Destinations);
    }

    [Fact]
    public async Task RegistryIdentityMismatchIsRejected()
    {
        var source = Source(
            AvailableDiscovery(Observation("aircraft-a")),
            new StubRegistry(("aircraft-a", Resolution(
                "aircraft-b",
                AircraftInstallationStatus.Installed,
                AircraftAccess.Civilian,
                500))));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => source.FilterAsync("KORG", [Destination("KAAA", 75)]));
    }

    [Fact]
    public async Task DestinationWithoutCompatibleRunwayIsFilteredBeforeGeneration()
    {
        var source = Source(
            AvailableDiscovery(Observation("aircraft-a")),
            new StubRegistry(("aircraft-a", Resolution(
                "aircraft-a",
                AircraftInstallationStatus.Installed,
                AircraftAccess.Civilian,
                500))),
            Airport("KORG", 5000),
            Airport("KAAA", 1500),
            Airport("KBBB", 5000));

        CareerJobMarketFeasibilitySnapshot result = await source.FilterAsync(
            "KORG",
            [Destination("KAAA", 50), Destination("KBBB", 75)]);

        Assert.Equal(CareerJobMarketFeasibilityStatus.Ready, result.Status);
        Assert.Equal("KBBB", Assert.Single(result.Destinations).Icao);
    }

    [Fact]
    public async Task MissingRunwayProvenanceFailsClosed()
    {
        var source = Source(
            AvailableDiscovery(Observation("aircraft-a")),
            new StubRegistry(("aircraft-a", Resolution(
                "aircraft-a",
                AircraftInstallationStatus.Installed,
                AircraftAccess.Civilian,
                500,
                includeRunwayProvenance: false))));

        CareerJobMarketFeasibilitySnapshot result = await source.FilterAsync(
            "KORG",
            [Destination("KAAA", 50)]);

        Assert.Equal(
            CareerJobMarketFeasibilityStatus.NoFeasibleDestinations,
            result.Status);
        Assert.Empty(result.Destinations);
    }

    [Fact]
    public async Task MissingOriginAirportDataFailsClosed()
    {
        var source = Source(
            AvailableDiscovery(Observation("aircraft-a")),
            new StubRegistry(("aircraft-a", Resolution(
                "aircraft-a",
                AircraftInstallationStatus.Installed,
                AircraftAccess.Civilian,
                500))),
            Airport("KAAA", 5000));

        CareerJobMarketFeasibilitySnapshot result = await source.FilterAsync(
            "KORG",
            [Destination("KAAA", 50)]);

        Assert.Equal(
            CareerJobMarketFeasibilityStatus.AirportDataUnavailable,
            result.Status);
        Assert.Empty(result.Destinations);
    }

    private static CareerJobMarketFeasibilitySource Source(
        InstalledAircraftDiscoverySnapshot discovery,
        IAircraftRegistrySource registry,
        params AirportRecord[] airports) =>
        new(
            new CareerJobAircraftSelectionSource(new StubDiscovery(discovery)),
            registry,
            new StubAirportDataSource(
                airports.Length == 0
                    ?
                    [
                        Airport("KORG", 5000),
                        Airport("KAAA", 5000),
                        Airport("KBBB", 5000)
                    ]
                    : airports));

    private static InstalledAircraftDiscoverySnapshot AvailableDiscovery(
        params AircraftRegistryObservation[] observations) =>
        new(
            InstalledAircraftDiscoveryAvailability.Available,
            observations);

    private static AircraftRegistryObservation Observation(string aircraftId) =>
        new(
            aircraftId,
            "test-discovery",
            aircraftId,
            AircraftDataConfidence.Verified,
            IsInstalled: true,
            DisplayName: aircraftId);

    private static AircraftRegistryResolution Resolution(
        string aircraftId,
        AircraftInstallationStatus installation,
        AircraftAccess access,
        double? maximumRangeNauticalMiles,
        bool includeRunwayProvenance = true) =>
        new(
            aircraftId,
            installation,
            new(
                aircraftId,
                null,
                access,
                MaximumPayloadPounds: 0,
                MaximumRangeNauticalMiles: maximumRangeNauticalMiles,
                TypicalCruiseKnots: null,
                Seats: null,
                EngineCount: null,
                IfrCapable: null,
                Pressurized: null,
                RetractableGear: null),
            new(
                MinimumTakeoffRunwayFeet: 1800,
                MinimumLandingRunwayFeet: 1600,
                MinimumRunwayWidthFeet: 50,
                SupportedSurfaces:
                    RunwaySurfaceSupport.Asphalt
                    | RunwaySurfaceSupport.Concrete,
                Confidence: AircraftDataConfidence.Verified,
                Source: "test-registry"),
            null,
            null,
            null,
            null,
            [
                AircraftRegistryField.Capabilities,
                AircraftRegistryField.TypicalCruiseKnots,
                AircraftRegistryField.Seats,
                AircraftRegistryField.EngineCount,
                AircraftRegistryField.IfrCapable,
                AircraftRegistryField.Pressurized,
                AircraftRegistryField.RetractableGear
            ],
            includeRunwayProvenance
                ? new Dictionary<AircraftRegistryField, AircraftRegistryFieldProvenance>
                {
                    [AircraftRegistryField.RunwayPerformance] = new(
                        AircraftRegistryField.RunwayPerformance,
                        "test-registry",
                        aircraftId,
                        AircraftDataConfidence.Verified,
                        InstalledEvidence: true)
                }
                : new Dictionary<AircraftRegistryField, AircraftRegistryFieldProvenance>());

    private static AirportRecord Airport(
        string icao,
        double runwayLengthFeet) =>
        new(
            icao,
            $"{icao} Fixture",
            [
                new(
                    "01",
                    runwayLengthFeet,
                    WidthFeet: 75,
                    Surface: RunwaySurface.Asphalt)
            ]);

    private static JobMarketDestination Destination(
        string icao,
        double distance) =>
        new(
            icao,
            distance,
            MarketAttractiveness: 1,
            EstimatedFlightHours: Math.Max(0.25, distance / 120));

    private sealed class StubDiscovery(InstalledAircraftDiscoverySnapshot current)
        : IInstalledAircraftDiscoverySource
    {
        public InstalledAircraftDiscoverySnapshot Current { get; } = current;
    }

    private sealed class StubRegistry(
        params (string Id, AircraftRegistryResolution Resolution)[] resolutions)
        : IAircraftRegistrySource
    {
        private readonly IReadOnlyDictionary<string, AircraftRegistryResolution> _resolutions =
            resolutions.ToDictionary(
                static item => item.Id,
                static item => item.Resolution,
                StringComparer.OrdinalIgnoreCase);

        public List<string> Requests { get; } = [];

        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(aircraftId);
            _resolutions.TryGetValue(aircraftId, out AircraftRegistryResolution? resolution);
            return Task.FromResult(resolution);
        }
    }

    private sealed class StubAirportDataSource(
        IReadOnlyList<AirportRecord> airports)
        : IAirportDataSource
    {
        private readonly IReadOnlyDictionary<string, AirportRecord> _airports =
            airports.ToDictionary(
                static airport => airport.Icao,
                StringComparer.OrdinalIgnoreCase);

        public Task<AirportRecord?> FindAirportAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _airports.TryGetValue(icao, out AirportRecord? airport);
            return Task.FromResult(airport);
        }
    }
}
