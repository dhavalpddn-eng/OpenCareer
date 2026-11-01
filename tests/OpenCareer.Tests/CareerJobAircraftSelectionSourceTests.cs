using OpenCareer.Application.Careers;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Tests;

public sealed class CareerJobAircraftSelectionSourceTests
{
    [Fact]
    public async Task AvailableInstalledCatalogProducesDeterministicPlayerOptions()
    {
        var source =
            new CareerJobAircraftSelectionSource(
                new FakeDiscovery(
                    new InstalledAircraftDiscoverySnapshot(
                        InstalledAircraftDiscoveryAvailability.Available,
                        [
                            Observation(
                                "msfs-title:zulu",
                                "Zulu"),
                            Observation(
                                "msfs-title:alpha",
                                "Alpha"),
                            Observation(
                                "msfs-title:alpha",
                                "Alpha Duplicate")
                        ])));

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        Assert.True(
            snapshot.IsAvailable);
        Assert.Equal(
            2,
            snapshot.Aircraft.Count);
        Assert.Equal(
            "Alpha",
            snapshot.Aircraft[0].DisplayName);
        Assert.Equal(
            "Zulu",
            snapshot.Aircraft[1].DisplayName);
    }

    [Fact]
    public async Task SimConnectAndLocalDuplicateCollapseToOneCanonicalPickerOption()
    {
        AircraftRegistryObservation live =
            Observation(
                "msfs-title:fixture",
                "Fixture Live",
                "msfs-simconnect");

        AircraftRegistryObservation local =
            Observation(
                "msfs-title:fixture",
                "Fixture Local",
                "msfs-package-cfg");

        var source =
            new CareerJobAircraftSelectionSource(
                new CompositeInstalledAircraftDiscoverySource(
                    new FakeDiscovery(
                        new(
                            InstalledAircraftDiscoveryAvailability.Available,
                            [live])),
                    new FakeDiscovery(
                        new(
                            InstalledAircraftDiscoveryAvailability.Available,
                            [local]))));

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        CareerJobAircraftOption option =
            Assert.Single(
                snapshot.Aircraft);

        Assert.Equal(
            "msfs-title:fixture",
            option.AircraftId);
    }

    [Fact]
    public async Task LocalInstalledAircraftRemainVisibleWhenSimConnectIsUnavailable()
    {
        AircraftRegistryObservation local =
            Observation(
                "msfs-title:local",
                "Local Aircraft",
                "msfs-package-cfg");

        var source =
            new CareerJobAircraftSelectionSource(
                new CompositeInstalledAircraftDiscoverySource(
                    new FakeDiscovery(
                        InstalledAircraftDiscoverySnapshot.Unavailable),
                    new FakeDiscovery(
                        new(
                            InstalledAircraftDiscoveryAvailability.Available,
                            [local]))));

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        Assert.True(snapshot.IsAvailable);
        Assert.Equal(
            "msfs-title:local",
            Assert.Single(snapshot.Aircraft).AircraftId);
    }

    [Fact]
    public async Task UnavailableCatalogFailsClosed()
    {
        var source =
            new CareerJobAircraftSelectionSource(
                new FakeDiscovery(
                    InstalledAircraftDiscoverySnapshot.Unavailable));

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        Assert.False(
            snapshot.IsAvailable);
        Assert.Empty(
            snapshot.Aircraft);
    }

    private static AircraftRegistryObservation Observation(
        string aircraftId,
        string displayName,
        string providerId = "fixture") =>
        new(
            aircraftId,
            ProviderId:
                providerId,
            ProviderRecordId:
                displayName,
            AircraftDataConfidence.Verified,
            IsInstalled:
                true,
            DisplayName:
                displayName);

    private sealed class FakeDiscovery(
        InstalledAircraftDiscoverySnapshot snapshot)
        : IInstalledAircraftDiscoverySource
    {
        public InstalledAircraftDiscoverySnapshot Current =>
            snapshot;
    }
}
