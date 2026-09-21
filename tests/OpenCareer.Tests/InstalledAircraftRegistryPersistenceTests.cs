using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Infrastructure.Aircraft;

namespace OpenCareer.Tests;

public class InstalledAircraftRegistryPersistenceTests
{
    [Fact]
    public async Task AvailableDiscoveryReplacesPersistedSnapshotAndReturnsCurrentAircraft()
    {
        AircraftRegistryObservation oldAircraft = Observation("old-aircraft", "Old Aircraft");
        AircraftRegistryObservation currentAircraft = Observation("current-aircraft", "Current Aircraft");

        var store = new RecordingStore([oldAircraft]);
        var source = new PersistentInstalledAircraftObservationSource(
            new StubDiscoverySource(
                new(
                    InstalledAircraftDiscoveryAvailability.Available,
                    [currentAircraft])),
            store);

        IReadOnlyList<AircraftRegistryObservation> result =
            await source.FindAircraftObservationsAsync("current-aircraft");

        AircraftRegistryObservation match = Assert.Single(result);
        Assert.Equal("Current Aircraft", match.DisplayName);
        Assert.Equal(1, store.ReplaceCount);
        Assert.Single(store.Observations);
        Assert.Equal("current-aircraft", store.Observations[0].CanonicalAircraftId);
    }

    [Fact]
    public async Task UnavailableDiscoveryFallsBackToPersistedSnapshot()
    {
        AircraftRegistryObservation persisted = Observation(
            "cached-aircraft",
            "Cached Aircraft");

        var store = new RecordingStore([persisted]);
        var source = new PersistentInstalledAircraftObservationSource(
            new StubDiscoverySource(InstalledAircraftDiscoverySnapshot.Unavailable),
            store);

        IReadOnlyList<AircraftRegistryObservation> result =
            await source.FindAircraftObservationsAsync("cached-aircraft");

        AircraftRegistryObservation match = Assert.Single(result);
        Assert.Equal("Cached Aircraft", match.DisplayName);
        Assert.Equal(0, store.ReplaceCount);
    }

    [Fact]
    public async Task SqliteStoreReplacesWholeInstalledSnapshot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));
        string database = Path.Combine(root, "aircraft-registry.db");

        try
        {
            var store = new SqliteInstalledAircraftRegistryStore(database);

            await store.ReplaceAllAsync(
            [
                Observation("aircraft-a", "Aircraft A"),
                Observation("aircraft-b", "Aircraft B")
            ]);

            Assert.Single(await store.FindAsync("aircraft-a"));
            Assert.Single(await store.FindAsync("aircraft-b"));

            await store.ReplaceAllAsync(
            [
                Observation("aircraft-b", "Aircraft B")
            ]);

            Assert.Empty(await store.FindAsync("aircraft-a"));
            AircraftRegistryObservation remaining =
                Assert.Single(await store.FindAsync("aircraft-b"));
            Assert.Equal("Aircraft B", remaining.DisplayName);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SqliteStoreRejectsNonInstalledObservation()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var store = new SqliteInstalledAircraftRegistryStore(
                Path.Combine(root, "aircraft-registry.db"));

            AircraftRegistryObservation observation =
                Observation("reference-only", "Reference Only")
                with { IsInstalled = false };

            await Assert.ThrowsAsync<ArgumentException>(
                () => store.ReplaceAllAsync([observation]));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static AircraftRegistryObservation Observation(
        string canonicalAircraftId,
        string displayName) =>
        new(
            canonicalAircraftId,
            "msfs-simconnect",
            displayName,
            AircraftDataConfidence.Verified,
            IsInstalled: true,
            DisplayName: displayName);

    private sealed class StubDiscoverySource(
        InstalledAircraftDiscoverySnapshot current)
        : IInstalledAircraftDiscoverySource
    {
        public InstalledAircraftDiscoverySnapshot Current { get; } = current;
    }

    private sealed class RecordingStore(
        IReadOnlyList<AircraftRegistryObservation> initial)
        : IInstalledAircraftRegistryStore
    {
        public IReadOnlyList<AircraftRegistryObservation> Observations { get; private set; } =
            initial.ToArray();

        public int ReplaceCount { get; private set; }

        public Task ReplaceAllAsync(
            IReadOnlyList<AircraftRegistryObservation> observations,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Observations = observations.ToArray();
            ReplaceCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AircraftRegistryObservation>> FindAsync(
            string canonicalAircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<AircraftRegistryObservation> result =
                Observations
                    .Where(observation => string.Equals(
                        observation.CanonicalAircraftId,
                        canonicalAircraftId,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            return Task.FromResult(result);
        }
    }
}
