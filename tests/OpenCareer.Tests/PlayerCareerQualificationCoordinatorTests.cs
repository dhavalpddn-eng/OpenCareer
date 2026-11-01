using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class PlayerCareerQualificationCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 21, 7, 0, 0, TimeSpan.Zero);

    private static readonly Guid CareerId =
        Guid.Parse("e1bcd321-6c2c-4e89-983a-2f7ff2c7d431");

    [Fact]
    public async Task ApplyEarnedPersistsAgainstCurrentRevisionAndPublishesResult()
    {
        PlayerCareerProfileStoreRecord existing =
            ExistingRecord(
                revision: 4,
                PilotLicenseLevel.Private,
                PilotRating.AirplaneSingleEngineLand);
        var store = new FakeStore(existing);
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerQualificationCoordinator(
                store,
                runtime);

        var earned =
            new PilotQualificationState(
                PilotLicenseLevel.Commercial,
                ImmutableHashSet.Create(
                    PilotRating.AirplaneSingleEngineLand,
                    PilotRating.InstrumentAirplane));

        PlayerCareerProfileStoreRecord saved =
            await coordinator.ApplyEarnedAsync(
                earned,
                savedAt: Epoch.AddHours(1));

        Assert.Equal(5, saved.Revision);
        Assert.Equal(
            PilotLicenseLevel.Commercial,
            saved.Profile.Qualifications.License);
        Assert.True(
            saved.Profile.Qualifications.Ratings.SetEquals(
                [
                    PilotRating.AirplaneSingleEngineLand,
                    PilotRating.InstrumentAirplane
                ]));
        Assert.Equal(4, store.LastExpectedRevision);
        Assert.Equal(1, store.SaveCount);
        Assert.Same(saved, runtime.Current);
    }

    [Fact]
    public async Task DuplicateEarnedStateIsIdempotent()
    {
        PlayerCareerProfileStoreRecord existing =
            ExistingRecord(
                revision: 3,
                PilotLicenseLevel.Private,
                PilotRating.AirplaneSingleEngineLand);
        var store = new FakeStore(existing);
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerQualificationCoordinator(
                store,
                runtime);

        PlayerCareerProfileStoreRecord result =
            await coordinator.ApplyEarnedAsync(
                existing.Profile.Qualifications,
                savedAt: Epoch.AddHours(1));

        Assert.Same(existing, result);
        Assert.Same(existing, runtime.Current);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task EarnedProgressionCannotDowngradeLicense()
    {
        PlayerCareerProfileStoreRecord existing =
            ExistingRecord(
                revision: 2,
                PilotLicenseLevel.Commercial,
                PilotRating.AirplaneSingleEngineLand);
        var store = new FakeStore(existing);
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerQualificationCoordinator(
                store,
                runtime);

        var downgrade =
            new PilotQualificationState(
                PilotLicenseLevel.Private,
                existing.Profile.Qualifications.Ratings);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ApplyEarnedAsync(
                downgrade,
                savedAt: Epoch.AddHours(1)));

        Assert.Equal(0, store.SaveCount);
        Assert.Same(existing, runtime.Current);
    }

    [Fact]
    public async Task EarnedProgressionCannotRemoveRatings()
    {
        PlayerCareerProfileStoreRecord existing =
            ExistingRecord(
                revision: 2,
                PilotLicenseLevel.Commercial,
                PilotRating.AirplaneSingleEngineLand,
                PilotRating.InstrumentAirplane);
        var store = new FakeStore(existing);
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerQualificationCoordinator(
                store,
                runtime);

        var removed =
            new PilotQualificationState(
                PilotLicenseLevel.Commercial,
                ImmutableHashSet.Create(
                    PilotRating.AirplaneSingleEngineLand));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ApplyEarnedAsync(
                removed,
                savedAt: Epoch.AddHours(1)));

        Assert.Equal(0, store.SaveCount);
        Assert.Same(existing, runtime.Current);
    }

    [Fact]
    public async Task ApplyEarnedRequiresCompletedOnboarding()
    {
        var store = new FakeStore(null);
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerQualificationCoordinator(
                store,
                runtime);

        var earned =
            new PilotQualificationState(
                PilotLicenseLevel.Private,
                ImmutableHashSet.Create(
                    PilotRating.AirplaneSingleEngineLand));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ApplyEarnedAsync(
                earned,
                savedAt: Epoch));

        Assert.Equal(0, store.SaveCount);
        Assert.True(runtime.OnboardingRequired);
    }

    [Fact]
    public async Task FailedOptimisticWriteDoesNotReplaceRuntime()
    {
        PlayerCareerProfileStoreRecord existing =
            ExistingRecord(
                revision: 6,
                PilotLicenseLevel.Private,
                PilotRating.AirplaneSingleEngineLand);
        var store = new FakeStore(existing)
        {
            FailNextSave = true
        };
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerQualificationCoordinator(
                store,
                runtime);

        var earned =
            new PilotQualificationState(
                PilotLicenseLevel.Private,
                existing.Profile.Qualifications.Ratings.Add(
                    PilotRating.InstrumentAirplane));

        await Assert.ThrowsAsync<PlayerCareerProfileConcurrencyException>(
            () => coordinator.ApplyEarnedAsync(
                earned,
                savedAt: Epoch.AddHours(1)));

        Assert.Equal(6, store.LastExpectedRevision);
        Assert.Equal(1, store.SaveCount);
        Assert.Same(existing, runtime.Current);
    }

    private static PlayerCareerProfileStoreRecord ExistingRecord(
        long revision,
        PilotLicenseLevel license,
        params PilotRating[] ratings)
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                CareerId,
                "KRME",
                Epoch) with
            {
                Qualifications =
                    new PilotQualificationState(
                        license,
                        ratings.ToImmutableHashSet())
            };
        profile.Validate();

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
