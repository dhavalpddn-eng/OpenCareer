using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class OperationConsequenceIntegrationTests
{
    private static readonly DateTimeOffset ResolvedAt =
        new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [MemberData(nameof(OutcomeCases))]
    public async Task ResolvedOperationPersistsExpectedConsequences(
        OperationResolutionInput input,
        OperationOutcomeStatus expectedStatus,
        double expectedFriendlyInfluence,
        double expectedCampaignProgress,
        double expectedTerritoryPressure,
        double expectedTrust,
        int expectedSuccessfulOperations,
        int expectedFailedOperations,
        double expectedFriendlySupply,
        double expectedFriendlyReadiness,
        double expectedHostileSupply)
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));

            var coordinator =
                new PersistedOperationConsequenceCoordinator(
                    CreateOrchestrator(),
                    CreateStore(options));

            OperationConsequenceStoreRecord saved =
                await coordinator.ApplyAsync(
                    input,
                    BaselineState(),
                    ResolvedAt.AddSeconds(5));

            var restartedStore = CreateStore(options);

            OperationConsequenceStoreRecord? loaded =
                await restartedStore.LoadAsync(
                    saved.ResolutionKey);

            Assert.NotNull(loaded);
            Assert.Equal(expectedStatus, loaded.Result.Outcome.Status);
            Assert.Equal(
                expectedFriendlyInfluence,
                loaded.Result.FactionInfluence.FriendlyInfluence,
                precision: 10);
            Assert.Equal(
                expectedCampaignProgress,
                loaded.Result.CampaignProgress.FriendlyProgress,
                precision: 10);
            Assert.Equal(
                expectedTerritoryPressure,
                loaded.Result.TerritoryPressure.State.AccumulatedFriendlyPressure,
                precision: 10);
            Assert.Equal(
                expectedTrust,
                loaded.Result.MilitaryCareer.Trust,
                precision: 10);
            Assert.Equal(
                expectedSuccessfulOperations,
                loaded.Result.MilitaryCareer.SuccessfulOperations);
            Assert.Equal(
                expectedFailedOperations,
                loaded.Result.MilitaryCareer.FailedOperations);
            Assert.Equal(
                expectedFriendlySupply,
                loaded.Result.Resources.FriendlySupply,
                precision: 10);
            Assert.Equal(
                expectedFriendlyReadiness,
                loaded.Result.Resources.FriendlyOperationalReadiness,
                precision: 10);
            Assert.Equal(
                expectedHostileSupply,
                loaded.Result.Resources.HostileSupply,
                precision: 10);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task InvalidMissionIsRejectedBeforePersistence()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));
            var countingStore = new CountingOperationConsequenceStore(
                CreateStore(options));

            var coordinator =
                new PersistedOperationConsequenceCoordinator(
                    CreateOrchestrator(),
                    countingStore);

            OperationResolutionInput invalid =
                SuccessInput() with
                {
                    MissionId = Guid.Empty
                };

            await Assert.ThrowsAsync<ArgumentException>(
                () => coordinator.ApplyAsync(
                    invalid,
                    BaselineState(),
                    ResolvedAt.AddSeconds(5)));

            Assert.Equal(0, countingStore.LoadCalls);
            Assert.Equal(0, countingStore.SaveCalls);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task AlreadyResolvedMissionIsRejectedWithoutChangingPersistedState()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));

            OperationResolutionInput input = SuccessInput();

            var firstCoordinator =
                new PersistedOperationConsequenceCoordinator(
                    CreateOrchestrator(),
                    CreateStore(options));

            OperationConsequenceStoreRecord first =
                await firstCoordinator.ApplyAsync(
                    input,
                    BaselineState(),
                    ResolvedAt.AddSeconds(5));

            var replayCoordinator =
                new PersistedOperationConsequenceCoordinator(
                    CreateOrchestrator(),
                    CreateStore(options));

            await Assert.ThrowsAsync<DuplicateOperationResolutionException>(
                () => replayCoordinator.ApplyAsync(
                    input,
                    StateFrom(first.Result),
                    ResolvedAt.AddMinutes(1)));

            OperationConsequenceStoreRecord? loaded =
                await CreateStore(options).LoadAsync(
                    first.ResolutionKey);

            Assert.NotNull(loaded);
            Assert.Equal(first, loaded);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task PersistedOutcomeSurvivesFreshCoordinatorRestart()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));

            var firstCoordinator =
                new PersistedOperationConsequenceCoordinator(
                    CreateOrchestrator(),
                    CreateStore(options));

            OperationConsequenceStoreRecord first =
                await firstCoordinator.ApplyAsync(
                    PartialSuccessInput(),
                    BaselineState(),
                    ResolvedAt.AddSeconds(5));

            var restartedStore = CreateStore(options);

            OperationConsequenceStoreRecord? loaded =
                await restartedStore.LoadAsync(
                    first.ResolutionKey);

            Assert.NotNull(loaded);
            Assert.Equal(first, loaded);

            var restartedCoordinator =
                new PersistedOperationConsequenceCoordinator(
                    CreateOrchestrator(),
                    restartedStore);

            await Assert.ThrowsAsync<DuplicateOperationResolutionException>(
                () => restartedCoordinator.ApplyAsync(
                    PartialSuccessInput(),
                    StateFrom(first.Result),
                    ResolvedAt.AddMinutes(1)));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task PersistenceFailureRollsBackAndRetryPersistsExactlyOnce()
    {
        var failingStore = new FailOnceOperationConsequenceStore();

        var coordinator =
            new PersistedOperationConsequenceCoordinator(
                CreateOrchestrator(),
                failingStore);

        OperationResolutionInput input = SuccessInput();
        OperationConsequenceState initial = BaselineState();

        await Assert.ThrowsAsync<IOException>(
            () => coordinator.ApplyAsync(
                input,
                initial,
                ResolvedAt.AddSeconds(5)));

        Assert.Null(failingStore.Record);

        OperationConsequenceStoreRecord saved =
            await coordinator.ApplyAsync(
                input,
                initial,
                ResolvedAt.AddSeconds(10));

        Assert.Equal(2, failingStore.SaveCalls);
        Assert.NotNull(failingStore.Record);
        Assert.Equal(saved, failingStore.Record);
        Assert.Equal(0.54, saved.Result.FactionInfluence.FriendlyInfluence, 10);
        Assert.Equal(0.56, saved.Result.CampaignProgress.FriendlyProgress, 10);
        Assert.Equal(
            0.10,
            saved.Result.TerritoryPressure.State.AccumulatedFriendlyPressure,
            10);
        Assert.Equal(0.52, saved.Result.MilitaryCareer.Trust, 10);
        Assert.Equal(3, saved.Result.MilitaryCareer.SuccessfulOperations);
        Assert.Equal(0.54, saved.Result.Resources.FriendlySupply, 10);

        await Assert.ThrowsAsync<DuplicateOperationResolutionException>(
            () => coordinator.ApplyAsync(
                input,
                StateFrom(saved.Result),
                ResolvedAt.AddMinutes(1)));

        Assert.Equal(2, failingStore.SaveCalls);
    }

    public static IEnumerable<object[]> OutcomeCases()
    {
        yield return
        [
            SuccessInput(),
            OperationOutcomeStatus.Success,
            0.54,
            0.56,
            0.10,
            0.52,
            3,
            1,
            0.54,
            0.53,
            0.47
        ];

        yield return
        [
            PartialSuccessInput(),
            OperationOutcomeStatus.PartialSuccess,
            0.52,
            0.53,
            0.05,
            0.51,
            3,
            1,
            0.52,
            0.515,
            0.485
        ];

        yield return
        [
            FailureInput(),
            OperationOutcomeStatus.Failure,
            0.47,
            0.45,
            -0.08,
            0.47,
            2,
            2,
            0.46,
            0.47,
            0.52
        ];

        yield return
        [
            AbortedInput(),
            OperationOutcomeStatus.Aborted,
            0.49,
            0.49,
            -0.02,
            0.495,
            2,
            1,
            0.49,
            0.495,
            0.50
        ];
    }

    private static OperationConsequenceOrchestrator CreateOrchestrator() =>
        new(
            new IdempotentOperationResolver(
                new OperationResolver(),
                new InMemoryOperationResolutionRegistry()),
            new MilitaryReputationConsequence(
                new InMemoryMilitaryReputationConsequenceRegistry()));

    private static OperationConsequenceState BaselineState() =>
        new(
            new FactionInfluenceState(
                "faction:ast",
                "faction:ghc",
                FriendlyInfluence: 0.50,
                HostileInfluence: 0.50),
            new CampaignProgressState(
                "campaign:test",
                FriendlyProgress: 0.50),
            new TerritoryPressureState(
                "sector-alpha",
                AccumulatedFriendlyPressure: 0),
            new MilitaryCareerState(
                MilitaryAffiliation.Reserve,
                MilitaryQualification.MilitaryFlight,
                Trust: 0.50,
                SuccessfulOperations: 2,
                FailedOperations: 1),
            new ConflictResourceState(
                "campaign:test",
                FriendlySupply: 0.50,
                FriendlyOperationalReadiness: 0.50,
                HostileSupply: 0.50));

    private static OperationConsequenceState StateFrom(
        OperationConsequenceResult result) =>
        new(
            result.FactionInfluence,
            result.CampaignProgress,
            result.TerritoryPressure.State,
            result.MilitaryCareer,
            result.Resources);

    private static OperationResolutionInput SuccessInput() =>
        BaseInput() with
        {
            MissionResult = MissionExecutionResult.Completed,
            ObjectivesCompleted = 2,
            ObjectivesRequired = 2,
            AircraftSurvived = true,
            CrewSurvived = true
        };

    private static OperationResolutionInput PartialSuccessInput() =>
        BaseInput() with
        {
            MissionResult = MissionExecutionResult.Completed,
            ObjectivesCompleted = 1,
            ObjectivesRequired = 2,
            AircraftSurvived = true,
            CrewSurvived = true
        };

    private static OperationResolutionInput FailureInput() =>
        BaseInput() with
        {
            MissionResult = MissionExecutionResult.Failed,
            ObjectivesCompleted = 2,
            ObjectivesRequired = 2,
            AircraftSurvived = true,
            CrewSurvived = true
        };

    private static OperationResolutionInput AbortedInput() =>
        BaseInput() with
        {
            MissionResult = MissionExecutionResult.Aborted,
            ObjectivesCompleted = 1,
            ObjectivesRequired = 2,
            AircraftSurvived = true,
            CrewSurvived = true
        };

    private static OperationResolutionInput BaseInput() =>
        new(
            Guid.Parse("b9b1fe44-3ac4-4b90-a943-fc957bb89114"),
            "support-014",
            MissionExecutionResult.Completed,
            ObjectivesCompleted: 2,
            ObjectivesRequired: 2,
            AircraftSurvived: true,
            CrewSurvived: true,
            MissionDuration: TimeSpan.FromMinutes(47),
            ResolvedAt);

    private static SqliteOperationConsequenceStore CreateStore(
        OpenCareerDatabaseOptions options) =>
        new(
            options,
            NullLogger<SqliteOperationConsequenceStore>.Instance);

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Cleanup must not make a passing SQLite assertion platform-specific.
        }
    }

    private sealed class CountingOperationConsequenceStore
        : IOperationConsequenceStore
    {
        private readonly IOperationConsequenceStore _inner;

        public CountingOperationConsequenceStore(
            IOperationConsequenceStore inner)
        {
            _inner = inner;
        }

        public int LoadCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public async Task<OperationConsequenceStoreRecord?> LoadAsync(
            OperationResolutionKey key,
            CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            return await _inner.LoadAsync(key, cancellationToken);
        }

        public async Task<OperationConsequenceStoreRecord> SaveAsync(
            OperationConsequenceResult result,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return await _inner.SaveAsync(
                result,
                savedAt,
                cancellationToken);
        }
    }

    private sealed class FailOnceOperationConsequenceStore
        : IOperationConsequenceStore
    {
        private bool _failNextSave = true;

        public int SaveCalls { get; private set; }

        public OperationConsequenceStoreRecord? Record { get; private set; }

        public Task<OperationConsequenceStoreRecord?> LoadAsync(
            OperationResolutionKey key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                Record is not null && Record.ResolutionKey == key
                    ? Record
                    : null);
        }

        public Task<OperationConsequenceStoreRecord> SaveAsync(
            OperationConsequenceResult result,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;

            if (_failNextSave)
            {
                _failNextSave = false;
                throw new IOException("Simulated persistence failure.");
            }

            OperationResolutionKey key =
                OperationResolutionKey.Create(
                    result.Outcome.OperationId,
                    result.Outcome.MissionId);

            if (Record is not null)
                throw new OperationConsequenceAlreadyExistsException(key);

            Record = new OperationConsequenceStoreRecord(
                key,
                result,
                savedAt);

            Record.Validate();
            return Task.FromResult(Record);
        }
    }
}
