using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class CompletedJobContractBridgeTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MatchingCompletedSessionTransitionsContractExactlyOnce()
    {
        Guid contractId =
            Guid.Parse(
                "99000000-0000-0000-0000-000000000001");

        PersistedJobContract inProgress =
            InProgressContract(
                contractId);

        var store =
            new FakeContractStore(
                inProgress);

        var coordinator =
            new FlightSessionCoordinator();

        FlightSession completedSession =
            CompletedSession(
                contractId);

        coordinator.Restore(
            completedSession);

        var bridge =
            new CompletedJobContractBridge(
                store,
                new JobContractLifecycleService(
                    store),
                coordinator);

        PersistedJobContract completed =
            await bridge.CompleteAsync(
                contractId);

        Assert.Equal(
            ContractStatus.Completed,
            completed.Contract.Status);
        Assert.Equal(
            completedSession.Milestones.CompletedAt,
            completed.Contract.CompletedAt);
        Assert.Equal(1, store.UpdateCount);

        PersistedJobContract replay =
            await bridge.CompleteAsync(
                contractId);

        Assert.Equal(
            completed,
            replay);
        Assert.Equal(1, store.UpdateCount);
    }

    [Fact]
    public async Task CompletionUsesExistingLifecycleRuntimePublication()
    {
        Guid contractId =
            Guid.Parse(
                "99000000-0000-0000-0000-000000000002");

        PersistedJobContract inProgress =
            InProgressContract(
                contractId);

        var store =
            new FakeContractStore(
                inProgress);

        var runtime =
            new JobContractRuntimeState(
                new JobContractRecoveryService(
                    new SingleRecoverySource(
                        inProgress),
                    store));

        await runtime.InitializeAsync();

        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(
            CompletedSession(
                contractId));

        var bridge =
            new CompletedJobContractBridge(
                store,
                new JobContractLifecycleService(
                    store,
                    runtime),
                coordinator);

        PersistedJobContract completed =
            await bridge.CompleteAsync(
                contractId);

        Assert.Equal(
            completed,
            runtime.Find(
                contractId));
        Assert.Equal(
            ContractStatus.Completed,
            runtime.Find(
                contractId)?.Contract.Status);
    }

    [Fact]
    public async Task DifferentContractSessionCannotCompleteContract()
    {
        Guid contractId =
            Guid.Parse(
                "99000000-0000-0000-0000-000000000003");

        var store =
            new FakeContractStore(
                InProgressContract(
                    contractId));

        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(
            CompletedSession(
                Guid.Parse(
                    "99000000-0000-0000-0000-000000000099")));

        var bridge =
            new CompletedJobContractBridge(
                store,
                new JobContractLifecycleService(
                    store),
                coordinator);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.CompleteAsync(
                contractId));

        Assert.Equal(0, store.UpdateCount);
        Assert.Equal(
            ContractStatus.InProgress,
            store.Current.Contract.Status);
    }

    [Fact]
    public async Task IncompleteFlightSessionCannotCompleteContract()
    {
        Guid contractId =
            Guid.Parse(
                "99000000-0000-0000-0000-000000000004");

        var store =
            new FakeContractStore(
                InProgressContract(
                    contractId));

        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(
            FlightSession.Start(
                Epoch,
                contractId,
                sessionId:
                    contractId));

        var bridge =
            new CompletedJobContractBridge(
                store,
                new JobContractLifecycleService(
                    store),
                coordinator);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.CompleteAsync(
                contractId));

        Assert.Equal(0, store.UpdateCount);
    }

    [Fact]
    public async Task CrashedCompletedSessionCannotCompleteContract()
    {
        Guid contractId =
            Guid.Parse(
                "99000000-0000-0000-0000-000000000005");

        var store =
            new FakeContractStore(
                InProgressContract(
                    contractId));

        FlightSession crashed =
            CompletedSession(
                contractId) with
            {
                Tracking =
                    CompletedSession(
                        contractId)
                    .Tracking with
                    {
                        CrashReported =
                            true
                    }
            };

        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(
            crashed);

        var bridge =
            new CompletedJobContractBridge(
                store,
                new JobContractLifecycleService(
                    store),
                coordinator);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.CompleteAsync(
                contractId));

        Assert.Equal(0, store.UpdateCount);
    }

    [Fact]
    public async Task CompletedReplayRequiresSameAuthoritativeCompletionTimestamp()
    {
        Guid contractId =
            Guid.Parse(
                "99000000-0000-0000-0000-000000000006");

        FlightSession session =
            CompletedSession(
                contractId);

        PersistedJobContract completed =
            InProgressContract(
                contractId) with
            {
                Contract =
                    InProgressContract(
                        contractId)
                    .Contract with
                    {
                        Status =
                            ContractStatus.Completed,
                        CompletedAt =
                            session.Milestones.CompletedAt?
                                .AddSeconds(1)
                    },
                Version =
                    3
            };

        completed.Validate();

        var store =
            new FakeContractStore(
                completed);

        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Restore(
            session);

        var bridge =
            new CompletedJobContractBridge(
                store,
                new JobContractLifecycleService(
                    store),
                coordinator);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.CompleteAsync(
                contractId));

        Assert.Equal(0, store.UpdateCount);
    }

    private static PersistedJobContract InProgressContract(
        Guid contractId)
    {
        var contract =
            new JobContract(
                ContractId:
                    contractId,
                EmployerId:
                    null,
                Kind:
                    ContractKind.Cargo,
                ServiceTrack:
                    ServiceTrack.CivilianEmployment,
                OriginIcao:
                    "KRME",
                DestinationIcao:
                    "KSYR",
                Compensation:
                    new ContractCompensation(
                        CompensationModel.PilotWage,
                        1_200m,
                        450m,
                        true,
                        true,
                        true),
                OfferedAt:
                    Epoch.AddHours(-1),
                MustStartBy:
                    null,
                MustCompleteBy:
                    Epoch.AddHours(2),
                AircraftRequirements:
                    new AircraftMissionRequirements(
                        RequiredCapabilities:
                            AircraftCapability.Cargo,
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumPayloadPounds:
                            500,
                        MinimumRangeNauticalMiles:
                            150,
                        MinimumSeats:
                            0),
                Status:
                    ContractStatus.InProgress,
                AcceptedAt:
                    Epoch.AddMinutes(-30),
                StartedAt:
                    Epoch);

        contract.Validate();

        return new(
            contract,
            Version: 2);
    }

    private static FlightSession CompletedSession(
        Guid contractId)
    {
        FlightSession session =
            FlightSession.Start(
                Epoch,
                contractId,
                sessionId:
                    contractId);

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    1,
                    stable:
                        true,
                    validAircraft:
                        true));

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    2,
                    movement:
                        true));

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    3,
                    takeoffCandidate:
                        true));

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    4,
                    airborne:
                        true));

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    5,
                    touchdown:
                        true));

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    6,
                    rollout:
                        true));

        session =
            FlightSessionEngine.Advance(
                session,
                Update(
                    7,
                    parking:
                        true,
                    shutdown:
                        true));

        return FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(8),
                    Connected:
                        true,
                    StableTelemetry:
                        true,
                    ContinuityPlausible:
                        true,
                    OperationCompleteConfirmed:
                        true),
                ShutdownConfirmed:
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
                Connected:
                    true,
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

    private sealed class SingleRecoverySource(
        PersistedJobContract persisted)
        : IJobContractRecoverySource
    {
        public Task<IReadOnlyList<JobContractRecoveryCandidate>>
            ReadRecoveryCandidatesAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<IReadOnlyList<JobContractRecoveryCandidate>>(
            [
                new(
                    persisted.Contract.ContractId,
                    persisted.Contract.Status,
                    persisted.Version,
                    persisted.Contract.StartedAt
                        ?? persisted.Contract.AcceptedAt
                        ?? persisted.Contract.OfferedAt)
            ]);
        }
    }

    private sealed class FakeContractStore(
        PersistedJobContract initial)
        : IJobContractStore
    {
        public PersistedJobContract Current { get; private set; } =
            initial;

        public int UpdateCount { get; private set; }

        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<PersistedJobContract?>(
                Current.Contract.ContractId
                    == contractId
                        ? Current
                        : null);
        }

        public Task<JobContractSaveResult> CreateJobContractAsync(
            JobContract contract,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobContractSaveResult> UpdateJobContractAsync(
            JobContract contract,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            contract.Validate();
            UpdateCount++;

            if (Current.Version
                != expectedVersion)
            {
                throw new InvalidOperationException(
                    "Version conflict.");
            }

            Current =
                new PersistedJobContract(
                    contract,
                    checked(expectedVersion + 1));

            return Task.FromResult(
                JobContractSaveResult.Updated);
        }
    }
}
