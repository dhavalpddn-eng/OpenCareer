using System.Collections.Immutable;
using OpenCareer.App.Services;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Settings;
using OpenCareer.Application.Tutorials;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class FirstJobTutorialAutoTriggerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DisabledAutomaticPreferenceDoesNotTrigger()
    {
        TriggerFixture fixture = await CreateAsync(
            automaticallyOffer: false,
            board: BoardWithOffer());

        Assert.False(await fixture.Trigger.TryStartAsync());
        Assert.False(fixture.Coordinator.Current.IsActive);
        Assert.Equal(0, fixture.Progress.SaveCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletedOrSkippedCurrentVersionDoesNotTrigger(bool completed)
    {
        var progress = new MemoryProgressStore();
        await progress.SaveAsync(new TutorialProgress(
            AppTutorialCatalog.FirstJobId,
            CompletedVersion: completed ? 1 : 0,
            SkippedVersion: completed ? 0 : 1));
        progress.ResetSaveCount();
        TriggerFixture fixture = await CreateAsync(
            progress: progress,
            board: BoardWithOffer());

        Assert.False(await fixture.Trigger.TryStartAsync());
        Assert.False(fixture.Coordinator.Current.IsActive);
        Assert.Equal(0, progress.SaveCount);
    }

    [Fact]
    public async Task ActiveAppIntroPreventsCompetingFirstJobTrigger()
    {
        TriggerFixture fixture = await CreateAsync(board: BoardWithOffer());
        await fixture.Coordinator.InitializeAsync();
        fixture.Progress.ResetSaveCount();

        Assert.False(await fixture.Trigger.TryStartAsync());
        Assert.True(fixture.Coordinator.Current.IsActive);
        Assert.Equal(AppTutorialCatalog.AppIntroId, fixture.Coordinator.Current.TutorialId);
        Assert.Equal(0, fixture.Progress.SaveCount);
    }

    [Fact]
    public async Task EligibleBoardTriggersOnceAndRepeatedChecksDoNotRestart()
    {
        TriggerFixture fixture = await CreateAsync(board: BoardWithOffer());

        Assert.True(await fixture.Trigger.TryStartAsync());
        TutorialSnapshot started = fixture.Coordinator.Current;
        Assert.Equal(AppTutorialCatalog.FirstJobId, started.TutorialId);
        Assert.Equal("job-find", started.Step?.Id);
        Assert.Equal(1, fixture.Progress.SaveCount);

        Assert.False(await fixture.Trigger.TryStartAsync());
        Assert.Equal(started, fixture.Coordinator.Current);
        Assert.Equal(1, fixture.Progress.SaveCount);
        Assert.Equal(1, fixture.Boards.ReadCount);
        Assert.Equal(0, fixture.Boards.SaveCount);
        Assert.Equal(0, fixture.ProfileStore.SaveCount);
    }

    [Fact]
    public async Task AcceptedContractIsRelevantWithoutBoardOffers()
    {
        TriggerFixture fixture = await CreateAsync(
            board: JobBoardState.Empty("KDFW", Now),
            contracts: [Contract(ContractStatus.Accepted)]);

        Assert.True(await fixture.Trigger.TryStartAsync());
        Assert.Equal(AppTutorialCatalog.FirstJobId, fixture.Coordinator.Current.TutorialId);
        Assert.Equal(0, fixture.Boards.ReadCount);
    }

    [Fact]
    public async Task MissingCareerOrJobsStateDoesNotTrigger()
    {
        TriggerFixture noCareer = await CreateAsync(
            hasCareer: false,
            board: BoardWithOffer());
        Assert.False(await noCareer.Trigger.TryStartAsync());

        TriggerFixture noJobs = await CreateAsync(
            board: JobBoardState.Empty("KDFW", Now));
        Assert.False(await noJobs.Trigger.TryStartAsync());
        Assert.False(noJobs.Coordinator.Current.IsActive);
    }

    [Fact]
    public async Task ManualReplayRemainsAvailableAfterAutomaticSuppression()
    {
        var progress = new MemoryProgressStore();
        await progress.SaveAsync(new TutorialProgress(
            AppTutorialCatalog.FirstJobId,
            CompletedVersion: 1));
        TriggerFixture fixture = await CreateAsync(
            progress: progress,
            board: BoardWithOffer());

        Assert.False(await fixture.Trigger.TryStartAsync());

        await fixture.Coordinator.RestartAsync(AppTutorialCatalog.FirstJobId);

        Assert.True(fixture.Coordinator.Current.IsActive);
        Assert.Equal(AppTutorialCatalog.FirstJobId, fixture.Coordinator.Current.TutorialId);
        Assert.Equal("job-find", fixture.Coordinator.Current.Step?.Id);
    }

    [Fact]
    public void ProductionReadinessMarksJobsAndFirstJobAvailable()
    {
        var readiness = new CurrentTutorialFeatureReadiness();

        Assert.Equal(TutorialFeatureState.Available, readiness.GetState("jobs"));
        Assert.Equal(TutorialFeatureState.Available, readiness.GetState("first-job"));
        Assert.Equal(
            TutorialFeatureState.ComingLater,
            readiness.GetState("mission-banner-tow"));
    }

    private static async Task<TriggerFixture> CreateAsync(
        bool automaticallyOffer = true,
        bool hasCareer = true,
        JobBoardState? board = null,
        IReadOnlyList<PersistedJobContract>? contracts = null,
        MemoryProgressStore? progress = null)
    {
        PlayerCareerProfileStoreRecord? record = hasCareer ? Profile() : null;
        var profileStore = new FakeProfileStore(record);
        var career = new PlayerCareerRuntimeState(profileStore);
        await career.InitializeAsync();
        var boards = new FakeBoardStore(board);
        progress ??= new MemoryProgressStore();
        var catalog = new AppTutorialCatalog();
        var coordinator = new TutorialCoordinator(
            catalog,
            new AvailableReadiness(),
            progress);
        var trigger = new FirstJobTutorialAutoTrigger(
            new FakeSettings(automaticallyOffer),
            catalog,
            progress,
            coordinator,
            career,
            boards,
            new FakeContracts(contracts ?? []));

        return new TriggerFixture(
            trigger,
            coordinator,
            progress,
            boards,
            profileStore);
    }

    private static PlayerCareerProfileStoreRecord Profile()
    {
        PlayerCareerProfile profile = PlayerCareerProfile.Start(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "KDFW",
            Now);
        return new PlayerCareerProfileStoreRecord(1, profile, Now);
    }

    private static JobBoardState BoardWithOffer() =>
        new(
            "KDFW",
            Now,
            ImmutableArray.Create(
                new JobMarketOfferDraft(
                    Guid.Parse("20000000-0000-0000-0000-000000000001"),
                    ServiceTrack.CivilianEmployment,
                    ContractKind.Cargo,
                    JobScenarioKind.Standard,
                    "KDFW",
                    "KAUS",
                    170,
                    1.2,
                    Now,
                    Now.AddHours(4),
                    IsLockedPreview: false,
                    RouteStrength: 0.5,
                    RelationshipStrength: 0.5,
                    MarketSelectionWeight: 1)),
            ImmutableHashSet<Guid>.Empty);

    private static PersistedJobContract Contract(ContractStatus status)
    {
        var contract = new JobContract(
            ContractId: Guid.Parse("30000000-0000-0000-0000-000000000001"),
            EmployerId: null,
            Kind: ContractKind.Cargo,
            ServiceTrack: ServiceTrack.CivilianEmployment,
            OriginIcao: "KDFW",
            DestinationIcao: "KAUS",
            Compensation: new ContractCompensation(
                CompensationModel.PilotWage,
                1_000m,
                400m,
                true,
                true,
                true),
            OfferedAt: Now.AddHours(-1),
            MustStartBy: null,
            MustCompleteBy: Now.AddHours(4),
            AircraftRequirements: new AircraftMissionRequirements(
                AircraftCapability.Cargo,
                AircraftAccess.Civilian),
            Status: status,
            AcceptedAt: Now);
        contract.Validate();
        return new PersistedJobContract(contract, 1);
    }

    private sealed record TriggerFixture(
        FirstJobTutorialAutoTrigger Trigger,
        TutorialCoordinator Coordinator,
        MemoryProgressStore Progress,
        FakeBoardStore Boards,
        FakeProfileStore ProfileStore);

    private sealed class FakeSettings(bool enabled) : IAppSettingsService
    {
        public AppPreferences Current { get; } =
            AppPreferences.Default with { AutomaticallyOfferTutorials = enabled };

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(
            AppPreferences preferences,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ResetAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class AvailableReadiness : ITutorialFeatureReadiness
    {
        public TutorialFeatureState GetState(string featureKey) =>
            TutorialFeatureState.Available;
    }

    private sealed class MemoryProgressStore : ITutorialProgressStore
    {
        private readonly Dictionary<string, TutorialProgress> _items =
            new(StringComparer.Ordinal);

        public int SaveCount { get; private set; }

        public Task<TutorialProgress> GetAsync(
            string tutorialId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _items.TryGetValue(tutorialId, out TutorialProgress? progress)
                    ? progress
                    : new TutorialProgress(tutorialId));

        public Task SaveAsync(
            TutorialProgress progress,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            _items[progress.TutorialId] = progress;
            return Task.CompletedTask;
        }

        public void ResetSaveCount() => SaveCount = 0;
    }

    private sealed class FakeBoardStore(JobBoardState? board) : IJobBoardStateStore
    {
        public int ReadCount { get; private set; }
        public int SaveCount { get; private set; }

        public Task SaveAsync(
            JobBoardState state,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            throw new InvalidOperationException("Tutorial checks must not write job boards.");
        }

        public Task<JobBoardState?> GetAsync(
            string airportIcao,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(board);
        }

        public Task<IReadOnlyList<JobBoardState>> LoadAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobBoardState>>(board is null ? [] : [board]);
    }

    private sealed class FakeContracts(
        IReadOnlyList<PersistedJobContract> contracts)
        : IJobContractRuntimeSource
    {
        public bool IsInitialized => true;
        public IReadOnlyList<PersistedJobContract> Current => contracts;

        public PersistedJobContract? Find(Guid contractId) =>
            contracts.SingleOrDefault(item => item.Contract.ContractId == contractId);
    }

    private sealed class FakeProfileStore(PlayerCareerProfileStoreRecord? record)
        : IPlayerCareerProfileStore
    {
        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
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
            throw new InvalidOperationException("Tutorial checks must not write career state.");
        }
    }
}
