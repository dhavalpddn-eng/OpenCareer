using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class OperationConsequenceAtomicityTests
{
    private static readonly DateTimeOffset ResolvedAt =
        new(2026, 9, 21, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PersistenceFailureReleasesReservationsAndAllowsCleanRetry()
    {
        var store = new ThrowOnceOperationConsequenceStore();
        OperationConsequenceOrchestrator orchestrator =
            CreateOrchestrator();

        var coordinator =
            new PersistedOperationConsequenceCoordinator(
                orchestrator,
                store);

        OperationResolutionInput input = ValidInput();
        OperationConsequenceState current = BaselineState();

        await Assert.ThrowsAsync<IOException>(
            () => coordinator.ApplyAsync(
                input,
                current,
                ResolvedAt.AddSeconds(5)));

        OperationConsequenceStoreRecord saved =
            await coordinator.ApplyAsync(
                input,
                current,
                ResolvedAt.AddSeconds(10));

        Assert.Equal(2, store.SaveCalls);
        Assert.Equal(
            OperationOutcomeStatus.Success,
            saved.Result.Outcome.Status);
        Assert.Equal(
            0.54,
            saved.Result.FactionInfluence.FriendlyInfluence,
            precision: 10);
        Assert.Equal(
            0.56,
            saved.Result.CampaignProgress.FriendlyProgress,
            precision: 10);
        Assert.Equal(
            0.10,
            saved.Result.TerritoryPressure.State.AccumulatedFriendlyPressure,
            precision: 10);
        Assert.Equal(
            0.52,
            saved.Result.MilitaryCareer.Trust,
            precision: 10);
        Assert.Equal(
            3,
            saved.Result.MilitaryCareer.SuccessfulOperations);
        Assert.Equal(
            0.54,
            saved.Result.Resources.FriendlySupply,
            precision: 10);
    }

    [Fact]
    public void ConsequenceFailureReleasesResolutionReservationForRetry()
    {
        OperationConsequenceOrchestrator orchestrator =
            CreateOrchestrator();
        OperationResolutionInput input = ValidInput();

        OperationConsequenceState overflowing =
            BaselineState() with
            {
                MilitaryCareer =
                    BaselineState().MilitaryCareer with
                    {
                        SuccessfulOperations = int.MaxValue
                    }
            };

        Assert.Throws<OverflowException>(
            () => orchestrator.Apply(
                input,
                overflowing));

        OperationConsequenceResult retried =
            orchestrator.Apply(
                input,
                BaselineState());

        Assert.Equal(
            OperationOutcomeStatus.Success,
            retried.Outcome.Status);
        Assert.Equal(
            3,
            retried.MilitaryCareer.SuccessfulOperations);
    }

    [Fact]
    public async Task SuccessfulPersistenceStillRejectsLaterDuplicate()
    {
        var store = new ThrowOnceOperationConsequenceStore(
            throwOnFirstSave: false);

        var coordinator =
            new PersistedOperationConsequenceCoordinator(
                CreateOrchestrator(),
                store);

        OperationResolutionInput input = ValidInput();
        OperationConsequenceState current = BaselineState();

        await coordinator.ApplyAsync(
            input,
            current,
            ResolvedAt.AddSeconds(5));

        await Assert.ThrowsAsync<DuplicateOperationResolutionException>(
            () => coordinator.ApplyAsync(
                input,
                current,
                ResolvedAt.AddSeconds(10)));

        Assert.Equal(1, store.SaveCalls);
    }

    private static OperationConsequenceOrchestrator CreateOrchestrator()
    {
        IOperationResolver resolver =
            new IdempotentOperationResolver(
                new OperationResolver(),
                new InMemoryOperationResolutionRegistry());

        var reputation =
            new MilitaryReputationConsequence(
                new InMemoryMilitaryReputationConsequenceRegistry());

        return new OperationConsequenceOrchestrator(
            resolver,
            reputation);
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
            Guid.Parse("02d864b4-cb60-462f-aa20-45ae134cbbcf"),
            "support-012",
            MissionExecutionResult.Completed,
            ObjectivesCompleted: 1,
            ObjectivesRequired: 1,
            AircraftSurvived: true,
            CrewSurvived: true,
            MissionDuration: TimeSpan.FromMinutes(40),
            ResolvedAt);

    private sealed class ThrowOnceOperationConsequenceStore
        : IOperationConsequenceStore
    {
        private bool _throwOnNextSave;
        private OperationConsequenceStoreRecord? _record;

        public ThrowOnceOperationConsequenceStore(
            bool throwOnFirstSave = true)
        {
            _throwOnNextSave = throwOnFirstSave;
        }

        public int SaveCalls { get; private set; }

        public Task<OperationConsequenceStoreRecord?> LoadAsync(
            OperationResolutionKey key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationConsequenceStoreRecord? result =
                _record is not null
                && _record.ResolutionKey == key
                    ? _record
                    : null;

            return Task.FromResult(result);
        }

        public Task<OperationConsequenceStoreRecord> SaveAsync(
            OperationConsequenceResult result,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;

            if (_throwOnNextSave)
            {
                _throwOnNextSave = false;
                throw new IOException("Simulated persistence failure.");
            }

            OperationResolutionKey key =
                OperationResolutionKey.Create(
                    result.Outcome.OperationId,
                    result.Outcome.MissionId);

            if (_record is not null)
                throw new OperationConsequenceAlreadyExistsException(key);

            _record =
                new OperationConsequenceStoreRecord(
                    key,
                    result,
                    savedAt);

            _record.Validate();
            return Task.FromResult(_record);
        }
    }
}
