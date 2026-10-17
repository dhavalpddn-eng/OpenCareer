using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class CivilianQualificationAwardServiceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 10, 16, 1, 0, 0, TimeSpan.Zero);

    private static readonly Guid CareerId =
        Guid.Parse("1bed2731-070b-4e42-9e19-90cf2b163493");

    private static readonly CivilianTrainingEvidenceIdentity EvidenceId =
        new("training-evidence-001");

    [Fact]
    public async Task CompletedEvidenceAwardsCatalogDerivedQualificationOnce()
    {
        var store = new FakeStore(Record(PilotQualificationState.Entry));
        TestContext context = Create(
            store,
            Completed(Target("license.student")));

        CivilianQualificationAwardResult result =
            await context.Service.AwardAsync(EvidenceId);

        Assert.Equal(CivilianQualificationAwardStatus.Awarded, result.Status);
        Assert.Equal(PilotLicenseLevel.Student, store.Record!.Profile.Qualifications.License);
        Assert.Empty(store.Record.Profile.Qualifications.Ratings);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task UnavailableEvidenceFailsClosedWithoutWrite()
    {
        var store = new FakeStore(Record(PilotQualificationState.Entry));
        TestContext context = Create(store, evidence: null);

        CivilianQualificationAwardResult result =
            await context.Service.AwardAsync(EvidenceId);

        Assert.Equal(CivilianQualificationAwardStatus.EvidenceUnavailable, result.Status);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ProductionEvidenceSourceIsExplicitlyUnavailable()
    {
        CivilianTrainingCompletionEvidence? evidence =
            await new UnavailableCivilianTrainingCompletionEvidenceSource()
                .ReadAsync(EvidenceId);

        Assert.Null(evidence);
    }

    [Fact]
    public async Task IncompleteEvidenceFailsClosedWithoutWrite()
    {
        var store = new FakeStore(Record(PilotQualificationState.Entry));
        CivilianTrainingCompletionEvidence evidence =
            Completed(Target("license.student")) with
            {
                Status = CivilianTrainingCompletionStatus.Incomplete,
                CompletedAt = null
            };
        TestContext context = Create(store, evidence);

        CivilianQualificationAwardResult result =
            await context.Service.AwardAsync(EvidenceId);

        Assert.Equal(CivilianQualificationAwardStatus.Blocked, result.Status);
        Assert.Contains("does not report completion", result.Detail, StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task UnknownQualificationFailsClosedWithoutWrite()
    {
        var store = new FakeStore(Record(PilotQualificationState.Entry));
        var unknown =
            new CareerQualificationIdentity(
                "license.unknown",
                CareerQualificationKind.License);
        TestContext context = Create(store, Completed(unknown));

        CivilianQualificationAwardResult result =
            await context.Service.AwardAsync(EvidenceId);

        Assert.Equal(CivilianQualificationAwardStatus.Blocked, result.Status);
        Assert.Contains("unknown", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task MismatchedCareerEvidenceFailsClosedWithoutWrite()
    {
        var store = new FakeStore(Record(PilotQualificationState.Entry));
        CivilianTrainingCompletionEvidence evidence =
            Completed(Target("license.student")) with
            {
                CareerId = Guid.Parse("f5398772-8e0c-4770-93c7-28099b7f952d")
            };
        TestContext context = Create(store, evidence);

        CivilianQualificationAwardResult result =
            await context.Service.AwardAsync(EvidenceId);

        Assert.Equal(CivilianQualificationAwardStatus.Blocked, result.Status);
        Assert.Contains("different career", result.Detail, StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task UnmetPrerequisiteFailsClosedWithoutWrite()
    {
        var store = new FakeStore(Record(PilotQualificationState.Entry));
        TestContext context = Create(
            store,
            Completed(Target("license.private")));

        CivilianQualificationAwardResult result =
            await context.Service.AwardAsync(EvidenceId);

        Assert.Equal(CivilianQualificationAwardStatus.Blocked, result.Status);
        Assert.Contains("prerequisites", result.Detail, StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task DuplicateAndRestartReplayDoNotWriteAgain()
    {
        var store = new FakeStore(Record(PilotQualificationState.Entry));
        CivilianTrainingCompletionEvidence evidence =
            Completed(Target("license.student"));
        TestContext first = Create(store, evidence);

        CivilianQualificationAwardResult awarded =
            await first.Service.AwardAsync(EvidenceId);
        CivilianQualificationAwardResult duplicate =
            await first.Service.AwardAsync(EvidenceId);
        TestContext restarted = Create(store, evidence);
        CivilianQualificationAwardResult replay =
            await restarted.Service.AwardAsync(EvidenceId);

        Assert.Equal(CivilianQualificationAwardStatus.Awarded, awarded.Status);
        Assert.Equal(CivilianQualificationAwardStatus.AlreadyEarned, duplicate.Status);
        Assert.Equal(CivilianQualificationAwardStatus.AlreadyEarned, replay.Status);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void AwardServiceHasNoDirectProfilePersistenceDependency()
    {
        Type[] constructorDependencies =
            Assert.Single(typeof(CivilianQualificationAwardService).GetConstructors())
                .GetParameters()
                .Select(static parameter => parameter.ParameterType)
                .ToArray();

        Assert.Contains(typeof(PlayerCareerQualificationCoordinator), constructorDependencies);
        Assert.DoesNotContain(typeof(IPlayerCareerProfileStore), constructorDependencies);
    }

    private static TestContext Create(
        FakeStore store,
        CivilianTrainingCompletionEvidence? evidence)
    {
        var runtime = new PlayerCareerRuntimeState(store);
        var coordinator =
            new PlayerCareerQualificationCoordinator(store, runtime);
        var service =
            new CivilianQualificationAwardService(
                runtime,
                new FakeEvidenceSource(evidence),
                CivilianQualificationCatalog.Default,
                coordinator,
                new FixedTimeProvider(Epoch.AddHours(1)));
        return new TestContext(service);
    }

    private static CivilianTrainingCompletionEvidence Completed(
        CareerQualificationIdentity target) =>
        new(
            EvidenceId,
            OperationIdentity: "training-operation-001",
            CareerId,
            target,
            CivilianTrainingCompletionStatus.Completed,
            CompletedAt: Epoch,
            SourceDetail: "Authoritative test training completion.");

    private static CareerQualificationIdentity Target(string value) =>
        Assert.Single(
            CivilianQualificationCatalog.Default.Definitions,
            definition => definition.Identity.Value == value).Identity;

    private static PlayerCareerProfileStoreRecord Record(
        PilotQualificationState qualifications)
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(CareerId, "KDFW", Epoch) with
            {
                Qualifications = qualifications
            };
        profile.Validate();
        return new PlayerCareerProfileStoreRecord(3, profile, Epoch);
    }

    private sealed record TestContext(
        CivilianQualificationAwardService Service);

    private sealed class FakeEvidenceSource(
        CivilianTrainingCompletionEvidence? evidence)
        : ICivilianTrainingCompletionEvidenceSource
    {
        public Task<CivilianTrainingCompletionEvidence?> ReadAsync(
            CivilianTrainingEvidenceIdentity evidenceIdentity,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(evidence);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStore(
        PlayerCareerProfileStoreRecord? record)
        : IPlayerCareerProfileStore
    {
        public PlayerCareerProfileStoreRecord? Record { get; private set; } = record;
        public int SaveCount { get; private set; }

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Record is null || expectedRevision != Record.Revision)
                throw new PlayerCareerProfileConcurrencyException("Stale test profile.");

            SaveCount++;
            Record =
                new PlayerCareerProfileStoreRecord(
                    checked(Record.Revision + 1),
                    profile,
                    savedAt);
            Record.Validate();
            return Task.FromResult(Record);
        }
    }
}
