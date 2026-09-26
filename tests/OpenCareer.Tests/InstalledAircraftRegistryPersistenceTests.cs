using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Infrastructure.Aircraft;

namespace OpenCareer.Tests;

public class InstalledAircraftRegistryPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialAvailableSnapshotCannotEraseRetainedRequestedAircraft(bool containsOtherAircraft)
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "career.db");
        const string id = AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId;
        var retained = Observation(id, "C172SP Classic Passengers");
        try
        {
            var store = new SqliteInstalledAircraftRegistryStore(path);
            await store.ReplaceAllAsync([retained]);
            var source = new PersistentInstalledAircraftObservationSource(new StubDiscoverySource(
                new(InstalledAircraftDiscoveryAvailability.Available,
                    containsOtherAircraft ? [Observation("other-aircraft", "Other Aircraft")] : [])), store);

            Assert.Equal(retained, Assert.Single(await source.FindAircraftObservationsAsync(id)));
            Assert.Equal(retained, Assert.Single(await new SqliteInstalledAircraftRegistryStore(path).FindAsync(id)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AvailableDiscoveryPersistsCurrentAircraftWithoutErasingOtherObservations()
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
        Assert.Equal(0, store.ReplaceCount);
        Assert.Equal(1, store.UpsertCount);
        Assert.Equal(2, store.Observations.Count);
        Assert.Equal(oldAircraft, Assert.Single(await store.FindAsync("old-aircraft")));
        Assert.Equal(currentAircraft, Assert.Single(await store.FindAsync("current-aircraft")));
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
            await Assert.ThrowsAsync<ArgumentException>(
                () => store.UpsertAsync([observation]));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SqliteUpsertRetainsOtherAircraftAndUpdatesOnlyObservedProviderRecordsAcrossRestart()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "career.db");
        try
        {
            var store = new SqliteInstalledAircraftRegistryStore(path);
            var a = Observation("aircraft-a", "Aircraft A");
            var b = Observation("aircraft-b", "Aircraft B");
            await store.UpsertAsync([a, b]);
            var updated = b with { DisplayName = "Updated B" };
            await store.UpsertAsync([updated]);
            await store.UpsertAsync([]);
            var restarted = new SqliteInstalledAircraftRegistryStore(path);
            Assert.Equal(a, Assert.Single(await restarted.FindAsync("aircraft-a")));
            Assert.Equal(updated, Assert.Single(await restarted.FindAsync("aircraft-b")));
            Assert.Empty(await restarted.FindAsync("unknown-aircraft"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PartialDiscoveryCannotMakeReferenceOnlyOrUnknownAircraftInstalled()
    {
        var store = new RecordingStore([]);
        var source = new PersistentInstalledAircraftObservationSource(new StubDiscoverySource(
            new(InstalledAircraftDiscoveryAvailability.Available, [])), store);
        var catalog = new AircraftRegistryCatalogService(
            [source, new PlayableLoopReferenceAircraftObservationSource()]);

        var reference = await catalog.FindAircraftAsync(AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId);
        Assert.NotNull(reference);
        Assert.Equal(AircraftInstallationStatus.KnownOnly, reference.InstallationStatus);
        Assert.Null(await catalog.FindAircraftAsync("unseeded-aircraft"));
        Assert.Empty(store.Observations);
        Assert.Equal(0, store.UpsertCount);
        Assert.Equal(0, store.ReplaceCount);
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
        public int UpsertCount { get; private set; }

        public Task UpsertAsync(
            IReadOnlyList<AircraftRegistryObservation> observations,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retained = Observations.ToDictionary(o => (o.ProviderId, o.ProviderRecordId));
            foreach (var observation in observations)
                retained[(observation.ProviderId, observation.ProviderRecordId)] = observation;
            Observations = retained.Values.ToArray();
            UpsertCount++;
            return Task.CompletedTask;
        }

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
