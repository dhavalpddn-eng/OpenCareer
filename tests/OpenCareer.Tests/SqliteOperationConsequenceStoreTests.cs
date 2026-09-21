using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteOperationConsequenceStoreTests
{
    private static readonly DateTimeOffset ResolvedAt =
        new(2026, 9, 21, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SaveAndLoadRoundTripsAcrossStoreRestart()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));

            OperationConsequenceResult result =
                BuildResult(OperationOutcomeStatus.Success);

            var firstStore = CreateStore(options);

            OperationConsequenceStoreRecord saved =
                await firstStore.SaveAsync(
                    result,
                    ResolvedAt.AddSeconds(5));

            var restartedStore = CreateStore(options);

            OperationConsequenceStoreRecord? loaded =
                await restartedStore.LoadAsync(
                    saved.ResolutionKey);

            Assert.NotNull(loaded);
            Assert.Equal(saved, loaded);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task DuplicateSaveAfterRestartIsRejected()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));

            OperationConsequenceResult result =
                BuildResult(OperationOutcomeStatus.PartialSuccess);

            await CreateStore(options).SaveAsync(
                result,
                ResolvedAt.AddSeconds(5));

            var restartedStore = CreateStore(options);

            OperationConsequenceAlreadyExistsException exception =
                await Assert.ThrowsAsync<OperationConsequenceAlreadyExistsException>(
                    () => restartedStore.SaveAsync(
                        result,
                        ResolvedAt.AddSeconds(10)));

            Assert.Equal(
                OperationResolutionKey.Create(
                    result.Outcome.OperationId,
                    result.Outcome.MissionId),
                exception.ResolutionKey);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task PersistedCoordinatorRejectsDuplicateBeforeResolverRunsAfterRestart()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));

            OperationResolutionInput input = ValidInput();
            OperationConsequenceState state = BaselineState();

            var firstCoordinator =
                new PersistedOperationConsequenceCoordinator(
                    CreateOrchestrator(new OperationResolver()),
                    CreateStore(options));

            await firstCoordinator.ApplyAsync(
                input,
                state,
                ResolvedAt.AddSeconds(5));

            var countingResolver = new CountingResolver();

            var restartedCoordinator =
                new PersistedOperationConsequenceCoordinator(
                    CreateOrchestrator(countingResolver),
                    CreateStore(options));

            await Assert.ThrowsAsync<DuplicateOperationResolutionException>(
                () => restartedCoordinator.ApplyAsync(
                    input,
                    state,
                    ResolvedAt.AddSeconds(10)));

            Assert.Equal(0, countingResolver.ResolveCount);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task LatestHistoryQueriesSelectCampaignAndSectorIndependently()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));
            var store = CreateStore(options);

            OperationConsequenceResult first =
                BuildResult(
                    OperationOutcomeStatus.Success,
                    campaignId: "campaign:test",
                    sectorId: "sector-alpha",
                    operationId: "support-history-1",
                    missionId: Guid.Parse(
                        "11111111-1111-1111-1111-111111111111"),
                    completedAt: ResolvedAt);

            OperationConsequenceResult second =
                BuildResult(
                    OperationOutcomeStatus.Failure,
                    campaignId: "campaign:test",
                    sectorId: "sector-bravo",
                    operationId: "support-history-2",
                    missionId: Guid.Parse(
                        "22222222-2222-2222-2222-222222222222"),
                    completedAt: ResolvedAt.AddMinutes(10));

            OperationConsequenceResult otherCampaign =
                BuildResult(
                    OperationOutcomeStatus.Success,
                    campaignId: "campaign:other",
                    sectorId: "sector-alpha",
                    operationId: "support-history-3",
                    missionId: Guid.Parse(
                        "33333333-3333-3333-3333-333333333333"),
                    completedAt: ResolvedAt.AddMinutes(20));

            await store.SaveAsync(
                first,
                first.Outcome.CompletedAt.AddSeconds(5));
            await store.SaveAsync(
                second,
                second.Outcome.CompletedAt.AddSeconds(5));
            await store.SaveAsync(
                otherCampaign,
                otherCampaign.Outcome.CompletedAt.AddSeconds(5));

            OperationConsequenceStoreRecord? latestCampaign =
                await store.LoadLatestForCampaignAsync(
                    "campaign:test");

            OperationConsequenceStoreRecord? latestSector =
                await store.LoadLatestForSectorAsync(
                    "campaign:test",
                    "sector-alpha");

            Assert.NotNull(latestCampaign);
            Assert.NotNull(latestSector);
            Assert.Equal(
                second.Outcome.MissionId,
                latestCampaign.Result.Outcome.MissionId);
            Assert.Equal(
                first.Outcome.MissionId,
                latestSector.Result.Outcome.MissionId);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task EmptyDatabaseReturnsNoOperationConsequence()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));

            OperationResolutionKey key =
                OperationResolutionKey.Create(
                    "support-011",
                    Guid.Parse("e061e733-540c-4aa6-a70d-56398453b2da"));

            Assert.Null(
                await CreateStore(options).LoadAsync(key));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static SqliteOperationConsequenceStore CreateStore(
        OpenCareerDatabaseOptions options) =>
        new(
            options,
            NullLogger<SqliteOperationConsequenceStore>.Instance);

    private static OperationConsequenceOrchestrator CreateOrchestrator(
        IOperationResolver resolver) =>
        new(
            new IdempotentOperationResolver(
                resolver,
                new InMemoryOperationResolutionRegistry()),
            new MilitaryReputationConsequence(
                new InMemoryMilitaryReputationConsequenceRegistry()));

    private static OperationConsequenceResult BuildResult(
        OperationOutcomeStatus status,
        string campaignId = "campaign:test",
        string sectorId = "sector-alpha",
        string operationId = "support-011",
        Guid? missionId = null,
        DateTimeOffset? completedAt = null)
    {
        var outcome = new OperationOutcome(
            missionId
                ?? Guid.Parse("e061e733-540c-4aa6-a70d-56398453b2da"),
            operationId,
            status,
            completedAt ?? ResolvedAt);

        return new OperationConsequenceResult(
            outcome,
            new FactionInfluenceState(
                "faction:ast",
                "faction:ghc",
                FriendlyInfluence: 0.54,
                HostileInfluence: 0.46),
            new CampaignProgressState(
                campaignId,
                FriendlyProgress: 0.56),
            new TerritoryPressureResult(
                new TerritoryPressureState(
                    sectorId,
                    AccumulatedFriendlyPressure: 0.10),
                FriendlyControlDelta: 0),
            new MilitaryCareerState(
                MilitaryAffiliation.Reserve,
                MilitaryQualification.MilitaryFlight,
                Trust: 0.52,
                SuccessfulOperations: 3,
                FailedOperations: 1),
            new ConflictResourceState(
                campaignId,
                FriendlySupply: 0.54,
                FriendlyOperationalReadiness: 0.53,
                HostileSupply: 0.47));
    }

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

    private static OperationResolutionInput ValidInput() =>
        new(
            Guid.Parse("e061e733-540c-4aa6-a70d-56398453b2da"),
            "support-011",
            MissionExecutionResult.Completed,
            ObjectivesCompleted: 1,
            ObjectivesRequired: 1,
            AircraftSurvived: true,
            CrewSurvived: true,
            MissionDuration: TimeSpan.FromMinutes(35),
            ResolvedAt);

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDirectory(
        string directory)
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

    private sealed class CountingResolver : IOperationResolver
    {
        public int ResolveCount { get; private set; }

        public OperationOutcome Resolve(
            OperationResolutionInput input)
        {
            ResolveCount++;
            return new OperationResolver().Resolve(input);
        }
    }
}
