using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class PlayerCareerExperienceCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid CareerId =
        Guid.Parse("0a73f2c7-bdb7-42fa-9621-0502c6454453");

    [Fact]
    public async Task ApplyCommittedAddsExperienceAndPersistsDebriefIdentity()
    {
        PlayerCareerProfileStoreRecord existing =
            ExistingRecord(revision: 3);
        var store = new FakeStore(existing);
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerExperienceCoordinator(
                store,
                runtime);
        LogbookEntry entry =
            CreateCommittedEntry(
                Guid.Parse("166051cc-7acf-4465-b145-c61376706a63"));

        PlayerCareerProfileStoreRecord saved =
            await coordinator.ApplyCommittedAsync(
                entry,
                savedAt: Epoch.AddHours(3));

        Assert.Equal(4, saved.Revision);
        Assert.Equal(1, saved.Profile.Experience.FlightCount);
        Assert.Equal(
            TimeSpan.FromMinutes(90),
            saved.Profile.Experience.CareerCreditTime);
        Assert.Equal(
            TimeSpan.FromMinutes(30),
            saved.Profile.Experience.NightCareerCreditTime);
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            saved.Profile.Experience.ActualInstrumentCareerCreditTime);
        Assert.Equal(2, saved.Profile.Experience.TakeoffCount);
        Assert.Equal(2, saved.Profile.Experience.LandingEpisodeCount);
        Assert.Contains(
            entry.Debrief.DebriefId,
            saved.Profile.AppliedExperienceDebriefIds);
        Assert.Equal(3, store.LastExpectedRevision);
        Assert.Equal(1, store.SaveCount);
        Assert.Same(saved, runtime.Current);
    }

    [Fact]
    public async Task ReapplyingSameDebriefIsIdempotent()
    {
        PlayerCareerProfileStoreRecord existing =
            ExistingRecord(revision: 2);
        var store = new FakeStore(existing);
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerExperienceCoordinator(
                store,
                runtime);
        LogbookEntry entry =
            CreateCommittedEntry(
                Guid.Parse("91ffab95-7215-4553-b985-9cdacfb6541b"));

        PlayerCareerProfileStoreRecord first =
            await coordinator.ApplyCommittedAsync(
                entry,
                savedAt: Epoch.AddHours(3));

        PlayerCareerProfileStoreRecord second =
            await coordinator.ApplyCommittedAsync(
                entry,
                savedAt: Epoch.AddHours(4));

        Assert.Same(first, second);
        Assert.Equal(1, second.Profile.Experience.FlightCount);
        Assert.Equal(1, store.SaveCount);
        Assert.Same(first, runtime.Current);
    }

    [Fact]
    public async Task FailedOptimisticWriteDoesNotApplyExperienceIdentity()
    {
        PlayerCareerProfileStoreRecord existing =
            ExistingRecord(revision: 7);
        var store = new FakeStore(existing)
        {
            FailNextSave = true
        };
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerExperienceCoordinator(
                store,
                runtime);
        LogbookEntry entry =
            CreateCommittedEntry(
                Guid.Parse("0a52495b-01db-4698-98f9-790a8257c4f7"));

        await Assert.ThrowsAsync<PlayerCareerProfileConcurrencyException>(
            () => coordinator.ApplyCommittedAsync(
                entry,
                savedAt: Epoch.AddHours(3)));

        Assert.Equal(7, store.LastExpectedRevision);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(PilotExperienceTotals.Empty, existing.Profile.Experience);
        Assert.DoesNotContain(
            entry.Debrief.DebriefId,
            existing.Profile.AppliedExperienceDebriefIds);
        Assert.Same(existing, runtime.Current);
    }

    [Fact]
    public async Task ApplyCommittedRequiresCompletedOnboarding()
    {
        var store = new FakeStore(null);
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerExperienceCoordinator(
                store,
                runtime);
        LogbookEntry entry =
            CreateCommittedEntry(
                Guid.Parse("c4e1a4ab-b656-41ed-8a50-06b1dd13deed"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ApplyCommittedAsync(
                entry,
                savedAt: Epoch.AddHours(3)));

        Assert.Equal(0, store.SaveCount);
        Assert.True(runtime.OnboardingRequired);
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

    private static LogbookEntry CreateCommittedEntry(
        Guid debriefId)
    {
        DateTimeOffset startedAt = Epoch.AddHours(1);
        DateTimeOffset endedAt = Epoch.AddHours(2);

        var time =
            FlightTimeLedger.Empty with
            {
                CareerCreditTime = TimeSpan.FromMinutes(90),
                NightCareerCreditTime = TimeSpan.FromMinutes(30),
                ActualInstrumentCareerCreditTime =
                    TimeSpan.FromMinutes(15)
            };

        FlightTrackingSnapshot tracking =
            FlightTrackingSnapshot.Start(endedAt) with
            {
                TakeoffCount = 2,
                LandingEpisodeCount = 2
            };

        var debrief =
            new FlightDebrief(
                debriefId,
                Guid.Parse("c5052c48-6272-4372-8192-b2e4100d933e"),
                ContractId: null,
                EntryKind: LogbookEntryKind.FreeFlight,
                StartedAt: startedAt,
                EndedAt: endedAt,
                Route:
                    new FlightRouteDebrief(
                        "KRME",
                        "KALB",
                        "KRME",
                        "KALB",
                        null,
                        72),
                Aircraft:
                    new AircraftDebrief("Test Aircraft"),
                Time: time,
                Tracking: tracking,
                Legs: Array.Empty<FlightLegDebrief>(),
                Fuel:
                    new FlightFuelDebrief(
                        null,
                        null,
                        null,
                        EvidenceQuality.Unavailable),
                Payload:
                    new PayloadDebrief(
                        null,
                        null,
                        null,
                        null,
                        EvidenceQuality.Unavailable),
                Assistance:
                    new FlightAssistanceDebrief(
                        false,
                        false,
                        false,
                        false,
                        false),
                SafetyOutcome:
                    FlightSafetyOutcome.CompletedNormally,
                MissionOutcome:
                    MissionOutcome.NotApplicable,
                Landings: Array.Empty<LandingDebrief>(),
                Events: Array.Empty<FlightDebriefEvent>(),
                Settlement:
                    FlightSettlementRecord.NotApplicable);

        return LogbookEntry.Commit(
            Guid.Parse("03bab805-d22e-46b4-aa3a-c5935b46e529"),
            debrief,
            committedAt: endedAt.AddMinutes(1),
            LogbookCommitKind.ManualPilotLog);
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
