using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class PlayerCareerLocationCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 21, 5, 0, 0, TimeSpan.Zero);

    private static readonly Guid CareerId =
        Guid.Parse("64677a97-3311-4f0d-85e6-afcd8d372688");

    [Fact]
    public async Task UpdatePersistsAgainstCurrentRevisionAndPublishesResult()
    {
        PlayerCareerProfileStoreRecord existing =
            ExistingRecord(revision: 3);
        var store = new FakeStore(existing);
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerLocationCoordinator(
                store,
                runtime);

        CareerLocation arrived =
            existing.Profile.Location with
            {
                CurrentAirportIcao = "KALB",
                UpdatedAt = Epoch.AddHours(2),
                Connections =
                    existing.Profile.Location.Connections.Add("KALB"),
                AppliedTravelContracts =
                    ImmutableHashSet<Guid>.Empty.Add(
                        Guid.Parse("7b08fc87-a9d4-465c-b88d-498596617077"))
            };
        arrived.Validate();

        PlayerCareerProfileStoreRecord saved =
            await coordinator.UpdateAsync(
                arrived,
                savedAt: Epoch.AddHours(2));

        Assert.Equal(4, saved.Revision);
        Assert.Equal("KRME", saved.Profile.Location.HomeAirportIcao);
        Assert.Equal("KALB", saved.Profile.Location.CurrentAirportIcao);
        Assert.Equal(3, store.LastExpectedRevision);
        Assert.Equal(1, store.SaveCount);
        Assert.Same(saved, runtime.Current);
    }

    [Fact]
    public async Task UpdateRequiresCompletedOnboarding()
    {
        var store = new FakeStore(null);
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerLocationCoordinator(
                store,
                runtime);

        CareerLocation location =
            CareerLocation.Start(
                "KRME",
                Epoch);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.UpdateAsync(
                location,
                savedAt: Epoch));

        Assert.Equal(0, store.SaveCount);
        Assert.True(runtime.OnboardingRequired);
    }

    [Fact]
    public async Task HomeBaseCannotChangeThroughLocationUpdate()
    {
        PlayerCareerProfileStoreRecord existing =
            ExistingRecord(revision: 2);
        var store = new FakeStore(existing);
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerLocationCoordinator(
                store,
                runtime);

        CareerLocation movedHome =
            CareerLocation.Start(
                "KDFW",
                Epoch.AddHours(2));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.UpdateAsync(
                movedHome,
                savedAt: Epoch.AddHours(2)));

        Assert.Equal(0, store.SaveCount);
        Assert.Same(existing, runtime.Current);
    }

    [Fact]
    public async Task LocationCannotMoveBackwardsInTime()
    {
        PlayerCareerProfileStoreRecord existing =
            ExistingRecord(revision: 2);
        var store = new FakeStore(existing);
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerLocationCoordinator(
                store,
                runtime);

        CareerLocation stale =
            existing.Profile.Location with
            {
                UpdatedAt =
                    existing.Profile.Location.UpdatedAt.AddMinutes(-1)
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.UpdateAsync(
                stale,
                savedAt: Epoch.AddHours(2)));

        Assert.Equal(0, store.SaveCount);
        Assert.Same(existing, runtime.Current);
    }

    [Fact]
    public async Task FailedOptimisticWriteDoesNotReplaceRuntime()
    {
        PlayerCareerProfileStoreRecord existing =
            ExistingRecord(revision: 5);
        var store = new FakeStore(existing)
        {
            FailNextSave = true
        };
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerLocationCoordinator(
                store,
                runtime);

        CareerLocation arrived =
            existing.Profile.Location with
            {
                CurrentAirportIcao = "KALB",
                UpdatedAt = Epoch.AddHours(2),
                Connections =
                    existing.Profile.Location.Connections.Add("KALB")
            };

        await Assert.ThrowsAsync<PlayerCareerProfileConcurrencyException>(
            () => coordinator.UpdateAsync(
                arrived,
                savedAt: Epoch.AddHours(2)));

        Assert.Equal(5, store.LastExpectedRevision);
        Assert.Equal(1, store.SaveCount);
        Assert.Same(existing, runtime.Current);
    }

    private static PlayerCareerProfileStoreRecord ExistingRecord(
        long revision)
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                CareerId,
                "KRME",
                Epoch);

        return new PlayerCareerProfileStoreRecord(
            revision,
            profile,
            SavedAt: Epoch.AddMinutes(1));
    }

    private sealed class FakeStore
        : IPlayerCareerProfileStore
    {
        private PlayerCareerProfileStoreRecord? _record;

        public FakeStore(
            PlayerCareerProfileStoreRecord? record)
        {
            _record = record;
        }

        public int SaveCount { get; private set; }

        public long? LastExpectedRevision { get; private set; }

        public bool FailNextSave { get; set; }

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            LastExpectedRevision = expectedRevision;

            if (FailNextSave)
            {
                FailNextSave = false;
                throw new PlayerCareerProfileConcurrencyException(
                    "Synthetic stale career profile revision.");
            }

            if (_record is null
                || expectedRevision != _record.Revision
                || profile.CareerId != _record.Profile.CareerId)
            {
                throw new PlayerCareerProfileConcurrencyException(
                    "Player career profile revision is stale.");
            }

            var saved =
                new PlayerCareerProfileStoreRecord(
                    checked(_record.Revision + 1),
                    profile,
                    savedAt);
            saved.Validate();
            _record = saved;
            return Task.FromResult(saved);
        }
    }
}
