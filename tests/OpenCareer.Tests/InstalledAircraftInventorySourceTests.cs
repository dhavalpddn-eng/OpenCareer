using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Tests;

public sealed class InstalledAircraftInventorySourceTests
{
    [Fact]
    public async Task UnavailableDiscoveryFailsClosedWithoutRegistryReads()
    {
        var registry = new StubRegistry();
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(InstalledAircraftDiscoverySnapshot.Unavailable),
            registry);

        InstalledAircraftInventorySnapshot result = await source.ReadAsync();

        Assert.Equal(InstalledAircraftInventoryStatus.DiscoveryUnavailable, result.Status);
        Assert.Empty(result.Aircraft);
        Assert.Empty(registry.Requests);
        Assert.Contains("will not guess", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyAvailableDiscoveryReportsNoInstalledAircraft()
    {
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(new(
                InstalledAircraftDiscoveryAvailability.Available,
                [])),
            new StubRegistry());

        InstalledAircraftInventorySnapshot result = await source.ReadAsync();

        Assert.Equal(InstalledAircraftInventoryStatus.NoInstalledAircraft, result.Status);
        Assert.Empty(result.Aircraft);
    }

    [Fact]
    public async Task InventoryDeduplicatesAndOrdersInstalledCanonicalRecords()
    {
        const string alphaId = "msfs-title:Alpha Livery";
        const string zuluId = "msfs-title:Zulu Livery";
        var registry = new StubRegistry(
            (alphaId, Resolution(
                alphaId,
                "Alpha Aircraft",
                new("icao-type:C172", "C172"),
                new(AircraftEngineType.Piston, 1, AircraftEngineMultiplicity.Single))),
            (zuluId, Resolution(zuluId, "Zulu Aircraft", null, null)));
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(new(
                InstalledAircraftDiscoveryAvailability.Available,
                [
                    Observation(zuluId, "one"),
                    Observation(alphaId, "two"),
                    Observation(alphaId, "three")
                ])),
            registry);

        InstalledAircraftInventorySnapshot result = await source.ReadAsync();

        Assert.Equal(InstalledAircraftInventoryStatus.Ready, result.Status);
        Assert.Equal(2, result.Aircraft.Count);
        Assert.Equal([alphaId, zuluId], result.Aircraft.Select(static item => item.CanonicalAircraftId));
        Assert.Equal([alphaId, zuluId], registry.Requests.Order(StringComparer.Ordinal));

        InstalledAircraftInventoryItem alpha = result.Aircraft[0];
        Assert.Equal("Alpha Aircraft", alpha.DisplayName);
        Assert.Equal("INSTALLED", alpha.InstallationText);
        Assert.Equal("ICAO type C172", alpha.TypeIdentityText);
        Assert.Equal("Single piston engine", alpha.PowerplantText);
        Assert.Contains("access unknown", alpha.AccessText, StringComparison.OrdinalIgnoreCase);

        InstalledAircraftInventoryItem zulu = result.Aircraft[1];
        Assert.Equal("ICAO type unknown", zulu.TypeIdentityText);
        Assert.Equal("Powerplant unknown", zulu.PowerplantText);
    }

    [Fact]
    public async Task MissingRegistryMetadataUsesExactDiscoveredIdentityAndExplicitUnknowns()
    {
        const string id = "msfs-title:Exact Installed Title";
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(new(
                InstalledAircraftDiscoveryAvailability.Available,
                [Observation(id, "one")])),
            new StubRegistry());

        InstalledAircraftInventoryItem item = Assert.Single(
            (await source.ReadAsync()).Aircraft);

        Assert.Equal("Exact Installed Title", item.DisplayName);
        Assert.Equal("Registry metadata unavailable", item.CapabilityText);
        Assert.Equal("ICAO type unknown", item.TypeIdentityText);
        Assert.Equal("Powerplant unknown", item.PowerplantText);
    }

    [Fact]
    public async Task NonInstalledDiscoveryObservationIsRejected()
    {
        var source = new InstalledAircraftInventorySource(
            new StubDiscovery(new(
                InstalledAircraftDiscoveryAvailability.Available,
                [Observation("msfs-title:Not Installed", "one") with { IsInstalled = false }])),
            new StubRegistry());

        await Assert.ThrowsAsync<InvalidDataException>(() => source.ReadAsync());
    }

    private static AircraftRegistryObservation Observation(
        string aircraftId,
        string recordId) =>
        new(
            aircraftId,
            "test-discovery",
            recordId,
            AircraftDataConfidence.Verified,
            IsInstalled: true);

    private static AircraftRegistryResolution Resolution(
        string aircraftId,
        string displayName,
        AircraftTypeIdentity? type,
        AircraftPowerplantClassification? powerplant) =>
        new(
            aircraftId,
            AircraftInstallationStatus.Installed,
            new(
                displayName,
                null,
                null,
                null,
                null,
                null,
                null,
                powerplant?.EngineCount,
                null,
                null,
                null),
            null,
            null,
            type,
            powerplant,
            null,
            [
                AircraftRegistryField.Capabilities,
                AircraftRegistryField.Access
            ],
            new Dictionary<AircraftRegistryField, AircraftRegistryFieldProvenance>());

    private sealed class StubDiscovery(InstalledAircraftDiscoverySnapshot current)
        : IInstalledAircraftDiscoverySource
    {
        public InstalledAircraftDiscoverySnapshot Current { get; } = current;
    }

    private sealed class StubRegistry(
        params (string Id, AircraftRegistryResolution Resolution)[] resolutions)
        : IAircraftRegistrySource
    {
        private readonly Dictionary<string, AircraftRegistryResolution> _resolutions =
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
}
