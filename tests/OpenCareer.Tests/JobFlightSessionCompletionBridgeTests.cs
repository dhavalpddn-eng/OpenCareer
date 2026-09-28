using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class JobFlightSessionCompletionBridgeTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MatchingCoreSequenceCompletesThroughExistingFlightSessionAuthority()
    {
        Guid contractId =
            Guid.Parse(
                "98000000-0000-0000-0000-000000000001");

        TestContext context =
            await CreateAtShutdownAsync(
                contractId);

        FlightSession completed =
            await context.Bridge.CompleteAsync(
                new JobFlightSessionCompletionRequest(
                    contractId,
                    Epoch.AddSeconds(10),
                    MissionConditionsVerified:
                        true,
                    PostFlightTasksVerified:
                        true));

        Assert.Equal(
            FlightSessionStatus.Completed,
            completed.Status);
        Assert.Equal(
            FlightOperationState.Complete,
            completed.OperationState);
        Assert.Equal(
            FlightTrackingState.Complete,
            completed.Tracking.State);
        Assert.Equal(
            contractId,
            completed.ContractId);
    }

    [Fact]
    public async Task MissingCoreFlightSequenceCannotBeOverriddenByMissionApproval()
    {
        Guid contractId =
            Guid.Parse(
                "98000000-0000-0000-0000-000000000002");

        var coordinator =
            new FlightSessionCoordinator();
        var store =
            new MemoryStore();
        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        await persistence.StartAsync(
            Epoch,
            contractId);

        await persistence.AdvanceAsync(
            Update(
                1,
                stable:
                    true,
                validAircraft:
                    true));

        var evidence =
            new FakeEvidenceSource();

        var tracker =
            new JobFlightCompletionEvidenceTracker(
                coordinator,
                evidence);

        var bridge =
            new JobFlightSessionCompletionBridge(
                tracker,
                coordinator,
                new FlightSessionCompletionService(
                    coordinator,
                    persistence));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.CompleteAsync(
                new JobFlightSessionCompletionRequest(
                    contractId,
                    Epoch.AddSeconds(10),
                    MissionConditionsVerified:
                        true,
                    PostFlightTasksVerified:
                        true)));

        Assert.Equal(
            FlightSessionStatus.Active,
            coordinator.Current?.Status);
    }

    [Fact]
    public async Task WrongContractIdentityCannotUseAnotherSessionsEvidence()
    {
        Guid contractId =
            Guid.Parse(
                "98000000-0000-0000-0000-000000000003");

        TestContext context =
            await CreateAtShutdownAsync(
                contractId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Bridge.CompleteAsync(
                new JobFlightSessionCompletionRequest(
                    Guid.Parse(
                        "98000000-0000-0000-0000-000000000099"),
                    Epoch.AddSeconds(10),
                    MissionConditionsVerified:
                        true,
                    PostFlightTasksVerified:
                        true)));

        Assert.Equal(
            FlightSessionStatus.Active,
            context.Coordinator.Current?.Status);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task MissionAndPostFlightApprovalsRemainOwnedByCompletionCaller(
        bool missionVerified,
        bool postFlightVerified)
    {
        Guid contractId =
            Guid.Parse(
                "98000000-0000-0000-0000-000000000004");

        TestContext context =
            await CreateAtShutdownAsync(
                contractId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Bridge.CompleteAsync(
                new JobFlightSessionCompletionRequest(
                    contractId,
                    Epoch.AddSeconds(10),
                    missionVerified,
                    postFlightVerified)));

        Assert.Equal(
            FlightSessionStatus.Active,
            context.Coordinator.Current?.Status);
    }

    [Fact]
    public async Task CompletedReplayReturnsSameSessionWithoutAdditionalPersistence()
    {
        Guid contractId =
            Guid.Parse(
                "98000000-0000-0000-0000-000000000005");

        TestContext context =
            await CreateAtShutdownAsync(
                contractId);

        FlightSession completed =
            await context.Bridge.CompleteAsync(
                new JobFlightSessionCompletionRequest(
                    contractId,
                    Epoch.AddSeconds(10),
                    MissionConditionsVerified:
                        true,
                    PostFlightTasksVerified:
                        true));

        int saveCount =
            context.Store.SaveCount;

        FlightSession replay =
            await context.Bridge.CompleteAsync(
                new JobFlightSessionCompletionRequest(
                    contractId,
                    Epoch.AddSeconds(11),
                    MissionConditionsVerified:
                        true,
                    PostFlightTasksVerified:
                        true));

        Assert.Equal(
            completed,
            replay);
        Assert.Equal(
            saveCount,
            context.Store.SaveCount);
    }

    [Fact]
    public async Task RecoveredShutdownSequenceCanCompleteWithoutReplayedLiveEvidence()
    {
        Guid contractId =
            Guid.Parse(
                "98000000-0000-0000-0000-000000000006");

        TestContext original =
            await CreateAtShutdownAsync(
                contractId);

        FlightSession recovered =
            original.Coordinator.Current!;

        var coordinator =
            new FlightSessionCoordinator();
        coordinator.Restore(recovered);

        var store =
            new MemoryStore
            {
                Checkpoint =
                    recovered
            };

        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        var tracker =
            new JobFlightCompletionEvidenceTracker(
                coordinator,
                new FakeEvidenceSource());

        Assert.True(
            tracker.Current?.CoreFlightSequenceObserved);
        Assert.Null(
            tracker.Current?.LatestEvidenceAt);

        var bridge =
            new JobFlightSessionCompletionBridge(
                tracker,
                coordinator,
                new FlightSessionCompletionService(
                    coordinator,
                    persistence));

        FlightSession completed =
            await bridge.CompleteAsync(
                new JobFlightSessionCompletionRequest(
                    contractId,
                    Epoch.AddSeconds(10),
                    MissionConditionsVerified:
                        true,
                    PostFlightTasksVerified:
                        true));

        Assert.Equal(
            FlightSessionStatus.Completed,
            completed.Status);
    }

    private static async Task<TestContext> CreateAtShutdownAsync(
        Guid contractId)
    {
        var coordinator =
            new FlightSessionCoordinator();

        var store =
            new MemoryStore();

        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                store);

        await persistence.StartAsync(
            Epoch,
            contractId);

        await AdvanceToShutdownAsync(
            persistence);

        var evidence =
            new FakeEvidenceSource();

        var tracker =
            new JobFlightCompletionEvidenceTracker(
                coordinator,
                evidence);

        var bridge =
            new JobFlightSessionCompletionBridge(
                tracker,
                coordinator,
                new FlightSessionCompletionService(
                    coordinator,
                    persistence));

        return new(
            coordinator,
            store,
            bridge);
    }

    private static async Task AdvanceToShutdownAsync(
        FlightSessionPersistenceService persistence)
    {
        await persistence.AdvanceAsync(
            Update(
                1,
                stable:
                    true,
                validAircraft:
                    true));

        await persistence.AdvanceAsync(
            Update(
                2,
                movement:
                    true));

        await persistence.AdvanceAsync(
            Update(
                3,
                takeoffCandidate:
                    true));

        await persistence.AdvanceAsync(
            Update(
                4,
                airborne:
                    true));

        await persistence.AdvanceAsync(
            Update(
                5,
                touchdown:
                    true));

        await persistence.AdvanceAsync(
            Update(
                6,
                rollout:
                    true));

        await persistence.AdvanceAsync(
            Update(
                7,
                parking:
                    true,
                shutdown:
                    true));
    }

    private static FlightSessionAdvance Update(
        int seconds,
        bool stable = false,
        bool validAircraft = false,
        bool movement = false,
        bool takeoffCandidate = false,
        bool airborne = false,
        bool touchdown = false,
        bool rollout = false,
        bool parking = false,
        bool shutdown = false) =>
        new(
            new FlightStateEvidence(
                Epoch.AddSeconds(seconds),
                Connected: true,
                StableTelemetry:
                    stable,
                ValidLoadedAircraft:
                    validAircraft,
                ContinuityPlausible:
                    true,
                SelfPoweredMovementForFlight:
                    movement,
                TakeoffCandidate:
                    takeoffCandidate,
                AirborneConfirmed:
                    airborne,
                TouchdownConfirmed:
                    touchdown,
                LandingRolloutConfirmed:
                    rollout,
                ParkingConfirmed:
                    parking),
            ShutdownConfirmed:
                shutdown);

    private sealed record TestContext(
        FlightSessionCoordinator Coordinator,
        MemoryStore Store,
        JobFlightSessionCompletionBridge Bridge);

    private sealed class FakeEvidenceSource
        : IFlightStateEvidenceSource
    {
        public FlightStateEvidence? Current { get; private set; }

        public event Action<FlightStateEvidence?>? EvidenceChanged;

        public void Publish(
            FlightStateEvidence? evidence)
        {
            Current = evidence;
            EvidenceChanged?.Invoke(evidence);
        }
    }

    private sealed class MemoryStore
        : IFlightSessionCheckpointStore
    {
        public FlightSession? Checkpoint { get; set; }

        public int SaveCount { get; private set; }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            Checkpoint = session;
            return Task.CompletedTask;
        }

        public Task<FlightSession?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Checkpoint);
        }

        public Task ClearAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checkpoint = null;
            return Task.CompletedTask;
        }
    }
}
