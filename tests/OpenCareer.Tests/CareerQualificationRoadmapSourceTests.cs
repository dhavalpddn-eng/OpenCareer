using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class CareerQualificationRoadmapSourceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MissingCareerReturnsNoRoadmapWithoutWrites()
    {
        var store = new FakeStore(null);
        var source = Source(store);

        CareerQualificationRoadmap? roadmap =
            await source.ReadAsync();

        Assert.Null(roadmap);
        Assert.Equal(1, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task EntryStateHasOrderedStudentAvailableAndEverythingElseLocked()
    {
        var store = new FakeStore(Record(PilotQualificationState.Entry));
        var source = Source(store);

        CareerQualificationRoadmap roadmap =
            Assert.IsType<CareerQualificationRoadmap>(
                await source.ReadAsync());

        Assert.Equal(
            roadmap.Items.Select(static item => item.Sequence).Order(),
            roadmap.Items.Select(static item => item.Sequence));
        Assert.Equal(
            CareerQualificationRoadmapStatus.Available,
            roadmap.Items[0].Status);
        Assert.All(
            roadmap.Items.Skip(1),
            static item => Assert.Equal(CareerQualificationRoadmapStatus.Locked, item.Status));
        Assert.Contains("only a future authoritative", roadmap.TrainingPolicyDetail, StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task EarnedStateAndPrerequisiteGatesProjectDeterministically()
    {
        var qualifications =
            new PilotQualificationState(
                PilotLicenseLevel.Private,
                ImmutableHashSet.Create(PilotRating.AirplaneSingleEngineLand));
        var store = new FakeStore(Record(qualifications));
        var source = Source(store);

        CareerQualificationRoadmap roadmap =
            Assert.IsType<CareerQualificationRoadmap>(await source.ReadAsync());

        CareerQualificationRoadmapStatus[] expectedStatuses =
            [
                CareerQualificationRoadmapStatus.Earned,
                CareerQualificationRoadmapStatus.Earned,
                CareerQualificationRoadmapStatus.Available,
                CareerQualificationRoadmapStatus.Locked,
                CareerQualificationRoadmapStatus.Earned,
                CareerQualificationRoadmapStatus.Available,
                CareerQualificationRoadmapStatus.Available
            ];
        Assert.Equal(
            expectedStatuses,
            roadmap.Items.Select(static item => item.Status));
        Assert.All(
            roadmap.Items.Where(static item => item.Identity.Kind == CareerQualificationKind.Rating),
            static item => Assert.True(Assert.Single(item.Prerequisites).IsMet));
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RestartProjectsPersistedQualificationsWithoutMutation()
    {
        PlayerCareerProfileStoreRecord persisted = Record(
            new PilotQualificationState(
                PilotLicenseLevel.Commercial,
                ImmutableHashSet.Create(
                    PilotRating.AirplaneSingleEngineLand,
                    PilotRating.InstrumentAirplane)));
        var store = new FakeStore(persisted);

        CareerQualificationRoadmap first =
            Assert.IsType<CareerQualificationRoadmap>(
                await Source(store).ReadAsync());
        CareerQualificationRoadmap restarted =
            Assert.IsType<CareerQualificationRoadmap>(
                await Source(store).ReadAsync());

        Assert.Equal(first.EarnedQualifications, restarted.EarnedQualifications);
        Assert.Equal(
            first.Items.Select(static item => (item.Identity, item.Status)),
            restarted.Items.Select(static item => (item.Identity, item.Status)));
        Assert.Equal(2, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
    }

    private static CareerQualificationRoadmapSource Source(
        FakeStore store) =>
        new(new PlayerCareerRuntimeState(store));

    private static PlayerCareerProfileStoreRecord Record(
        PilotQualificationState qualifications)
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse("73c972b6-bfce-43f5-a42b-47dff67a986c"),
                "KDFW",
                Epoch) with
            {
                Qualifications = qualifications
            };
        profile.Validate();
        return new PlayerCareerProfileStoreRecord(4, profile, Epoch);
    }

    private sealed class FakeStore(
        PlayerCareerProfileStoreRecord? record)
        : IPlayerCareerProfileStore
    {
        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            return Task.FromResult(record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            throw new InvalidOperationException("Roadmap reads must not save.");
        }
    }
}
