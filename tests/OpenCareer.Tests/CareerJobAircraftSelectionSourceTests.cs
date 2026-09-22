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
        string displayName) =>
        new(
            aircraftId,
            ProviderId:
                "fixture",
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
