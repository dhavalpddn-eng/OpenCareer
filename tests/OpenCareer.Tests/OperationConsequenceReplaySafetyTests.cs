using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class OperationConsequenceReplaySafetyTests
{
    private static readonly DateTimeOffset ResolvedAt =
        new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompletedFlightReplayAfterReconnectDoesNotReapplyConsequences()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));

            OperationResolutionInput input = ValidInput();
            OperationConsequenceState initialState = BaselineState();

            var initialCoordinator =
                new PersistedOperationConsequenceCoordinator(
                    CreateOrchestrator(new OperationResolver()),
                    CreateStore(options));

            OperationConsequenceStoreRecord first =
                await initialCoordinator.ApplyAsync(
                    input,
                    initialState,
                    ResolvedAt.AddSeconds(5));

            OperationConsequenceStoreRecord? beforeReplay =
                await CreateStore(options).LoadAsync(
                    first.ResolutionKey);

            Assert.NotNull(beforeReplay);

            var countingResolver = new CountingResolver();

            var reconnectedCoordinator =
                new PersistedOperationConsequenceCoordinator(
                    CreateOrchestrator(countingResolver),
                    CreateStore(options));

            await Assert.ThrowsAsync<DuplicateOperationResolutionException>(
                () => reconnectedCoordinator.ApplyAsync(
                    input,
                    StateFrom(first.Result),
                    ResolvedAt.AddMinutes(1)));

            Assert.Equal(0, countingResolver.ResolveCount);

            OperationConsequenceStoreRecord? afterReplay =
                await CreateStore(options).LoadAsync(
                    first.ResolutionKey);

            Assert.NotNull(afterReplay);
            Assert.Equal(beforeReplay, afterReplay);

            Assert.Equal(
                0.54,
                afterReplay.Result.FactionInfluence.FriendlyInfluence,
                precision: 10);
            Assert.Equal(
                0.56,
                afterReplay.Result.CampaignProgress.FriendlyProgress,
                precision: 10);
            Assert.Equal(
                0.10,
                afterReplay.Result.TerritoryPressure.State.AccumulatedFriendlyPressure,
                precision: 10);
            Assert.Equal(
                0.52,
                afterReplay.Result.MilitaryCareer.Trust,
                precision: 10);
            Assert.Equal(
                3,
                afterReplay.Result.MilitaryCareer.SuccessfulOperations);
            Assert.Equal(
                0.54,
                afterReplay.Result.Resources.FriendlySupply,
                precision: 10);
            Assert.Equal(
                0.53,
                afterReplay.Result.Resources.FriendlyOperationalReadiness,
                precision: 10);
            Assert.Equal(
                0.47,
                afterReplay.Result.Resources.HostileSupply,
                precision: 10);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task RepeatedReconnectReplaysRemainNoOps()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));

            OperationResolutionInput input = ValidInput();

            var initialCoordinator =
                new PersistedOperationConsequenceCoordinator(
                    CreateOrchestrator(new OperationResolver()),
                    CreateStore(options));

            OperationConsequenceStoreRecord first =
                await initialCoordinator.ApplyAsync(
                    input,
                    BaselineState(),
                    ResolvedAt.AddSeconds(5));

            for (int replay = 0; replay < 3; replay++)
            {
                var countingResolver = new CountingResolver();

                var reconnectedCoordinator =
                    new PersistedOperationConsequenceCoordinator(
                        CreateOrchestrator(countingResolver),
                        CreateStore(options));

                await Assert.ThrowsAsync<DuplicateOperationResolutionException>(
                    () => reconnectedCoordinator.ApplyAsync(
                        input,
                        StateFrom(first.Result),
                        ResolvedAt.AddMinutes(replay + 1)));

                Assert.Equal(0, countingResolver.ResolveCount);
            }

            OperationConsequenceStoreRecord? persisted =
                await CreateStore(options).LoadAsync(
                    first.ResolutionKey);

            Assert.NotNull(persisted);
            Assert.Equal(first, persisted);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static OperationConsequenceOrchestrator CreateOrchestrator(
        IOperationResolver resolver) =>
        new(
            new IdempotentOperationResolver(
                resolver,
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

    private static OperationResolutionInput ValidInput() =>
        new(
            Guid.Parse("e7b3565a-1d46-4d9f-aef2-748c91990113"),
            "support-013",
            MissionExecutionResult.Completed,
            ObjectivesCompleted: 1,
            ObjectivesRequired: 1,
            AircraftSurvived: true,
            CrewSurvived: true,
            MissionDuration: TimeSpan.FromMinutes(50),
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
