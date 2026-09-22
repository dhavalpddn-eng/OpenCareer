using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Events;

namespace OpenCareer.Tests;

public sealed class JobContractLifecycleServiceTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AcceptUsesPersistedContractAndExpectedVersion()
    {
        JobContract offered =
            BaseContract();

        var store =
            new FakeStore(
                new PersistedJobContract(
                    offered,
                    Version: 4));

        var service =
            new JobContractLifecycleService(store);

        PersistedJobContract result =
            await service.AcceptAsync(
                offered.ContractId,
                DispatchContext(
                    OfferedAt.AddMinutes(10)));

        Assert.Equal(
            ContractStatus.Accepted,
            result.Contract.Status);
        Assert.Equal(
            OfferedAt.AddMinutes(10),
            result.Contract.AcceptedAt);
        Assert.Equal(
            5,
            result.Version);
        Assert.Equal<long?>(
            4L,
            store.LastExpectedVersion);
        Assert.Equal(
            1,
            store.UpdateCount);
    }

    [Fact]
    public async Task StartRechecksDispatchAgainstPersistedAcceptedContract()
    {
        JobContract accepted =
            BaseContract() with
            {
                Status = ContractStatus.Accepted,
                AcceptedAt = OfferedAt.AddMinutes(5)
            };

        var store =
            new FakeStore(
                new PersistedJobContract(
                    accepted,
                    Version: 1));

        var service =
            new JobContractLifecycleService(store);

        PersistedJobContract result =
            await service.StartAsync(
                accepted.ContractId,
                DispatchContext(
                    OfferedAt.AddMinutes(15)));

        Assert.Equal(
            ContractStatus.InProgress,
            result.Contract.Status);
        Assert.Equal(
            OfferedAt.AddMinutes(15),
            result.Contract.StartedAt);
        Assert.Equal(
            2,
            result.Version);
    }

    [Fact]
    public async Task StartPublishesDurableInProgressStateToRuntime()
    {
        JobContract accepted =
            BaseContract() with
            {
                Status = ContractStatus.Accepted,
                AcceptedAt = OfferedAt.AddMinutes(5)
            };

        var store =
            new FakeStore(
                new PersistedJobContract(
                    accepted,
                    Version: 1));

        JobContractRuntimeState runtimeState =
            CreateRuntimeState(
                store,
                new PersistedJobContract(
                    accepted,
                    Version: 1));

        await runtimeState.InitializeAsync();

        var service =
            new JobContractLifecycleService(
                store,
                runtimeState);

        PersistedJobContract result =
            await service.StartAsync(
                accepted.ContractId,
                DispatchContext(
                    OfferedAt.AddMinutes(15)));

        PersistedJobContract? runtime =
            runtimeState.Find(
                accepted.ContractId);

        Assert.NotNull(runtime);
        Assert.Equal(
            ContractStatus.InProgress,
            runtime.Contract.Status);
        Assert.Equal(
            result,
            runtime);
        Assert.Equal(
            2,
            runtime.Version);
    }

    [Fact]
    public async Task CompletePublishesDurableCompletedStateToRuntime()
    {
        JobContract inProgress =
            BaseContract() with
            {
                Status = ContractStatus.InProgress,
                AcceptedAt = OfferedAt.AddMinutes(5),
                StartedAt = OfferedAt.AddMinutes(15)
            };

        var store =
            new FakeStore(
                new PersistedJobContract(
                    inProgress,
                    Version: 2));

        JobContractRuntimeState runtimeState =
            CreateRuntimeState(
                store,
                new PersistedJobContract(
                    inProgress,
                    Version: 2));

        await runtimeState.InitializeAsync();

        var service =
            new JobContractLifecycleService(
                store,
                runtimeState);

        DateTimeOffset completedAt =
            OfferedAt.AddHours(1);

        PersistedJobContract result =
            await service.CompleteAsync(
                inProgress.ContractId,
                completedAt,
                flightCompletionVerified:
                    true);

        PersistedJobContract? runtime =
            runtimeState.Find(
                inProgress.ContractId);

        Assert.NotNull(runtime);
        Assert.Equal(
            ContractStatus.Completed,
            result.Contract.Status);
        Assert.Equal(
            completedAt,
            result.Contract.CompletedAt);
        Assert.Equal(
            3,
            result.Version);
        Assert.Equal(
            result,
            runtime);
    }

    [Fact]
    public async Task CompleteRejectsUnverifiedFlightBeforePersistence()
    {
        JobContract inProgress =
            BaseContract() with
            {
                Status = ContractStatus.InProgress,
                AcceptedAt = OfferedAt.AddMinutes(5),
                StartedAt = OfferedAt.AddMinutes(15)
            };

        var store =
            new FakeStore(
                new PersistedJobContract(
                    inProgress,
                    Version: 2));

        var service =
            new JobContractLifecycleService(
                store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.CompleteAsync(
                    inProgress.ContractId,
                    OfferedAt.AddHours(1),
                    flightCompletionVerified:
                        false));

        Assert.Equal(
            0,
            store.UpdateCount);
    }

    [Fact]
    public async Task InvalidTransitionIsRejectedBeforePersistence()
    {
        JobContract offered =
            BaseContract();

        var store =
            new FakeStore(
                new PersistedJobContract(
                    offered,
                    Version: 0));

        var service =
            new JobContractLifecycleService(store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(
                offered.ContractId,
                DispatchContext(
                    OfferedAt.AddMinutes(15))));

        Assert.Equal(
            0,
            store.UpdateCount);
    }

    [Fact]
    public async Task FailCancelAndExpireUseDomainTransitions()
    {
        JobContract accepted =
            BaseContract() with
            {
                Status = ContractStatus.Accepted,
                AcceptedAt = OfferedAt.AddMinutes(5)
            };

        var failStore =
            new FakeStore(
                new PersistedJobContract(
                    accepted,
                    Version: 2));

        PersistedJobContract failed =
            await new JobContractLifecycleService(failStore)
                .FailAsync(
                    accepted.ContractId);

        Assert.Equal(
            ContractStatus.Failed,
            failed.Contract.Status);

        var cancelStore =
            new FakeStore(
                new PersistedJobContract(
                    BaseContract(),
                    Version: 0));

        PersistedJobContract cancelled =
            await new JobContractLifecycleService(cancelStore)
                .CancelAsync(
                    accepted.ContractId);

        Assert.Equal(
            ContractStatus.Cancelled,
            cancelled.Contract.Status);

        var expireStore =
            new FakeStore(
                new PersistedJobContract(
                    BaseContract(),
                    Version: 0));

        PersistedJobContract expired =
            await new JobContractLifecycleService(expireStore)
                .ExpireAsync(
                    accepted.ContractId,
                    OfferedAt.AddHours(3));

        Assert.Equal(
            ContractStatus.Expired,
            expired.Contract.Status);
    }

    [Fact]
    public async Task OptimisticConcurrencyFailureIsNotHidden()
    {
        JobContract offered =
            BaseContract();

        var store =
            new FakeStore(
                new PersistedJobContract(
                    offered,
                    Version: 3))
            {
                ThrowConcurrencyConflict = true
            };

        var service =
            new JobContractLifecycleService(store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AcceptAsync(
                offered.ContractId,
                DispatchContext(
                    OfferedAt.AddMinutes(10))));

        Assert.Equal(
            1,
            store.UpdateCount);
    }

    [Fact]
    public async Task MissingPersistedContractCannotTransition()
    {
        var service =
            new JobContractLifecycleService(
                new FakeStore(null));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CancelAsync(
                Guid.Parse(
                    "92000000-0000-0000-0000-000000000001")));
    }

    private static JobContractRuntimeState CreateRuntimeState(
        IJobContractStore store,
        PersistedJobContract recovered)
    {
        var source =
            new SingleRecoverySource(
                recovered);

        return new JobContractRuntimeState(
            new JobContractRecoveryService(
                source,
                store));
    }

    private static ContractDispatchContext DispatchContext(
        DateTimeOffset time) =>
        new(
            time,
            new AircraftCapabilityProfile(
                "cargo-fixture",
                "Cargo fixture",
                AircraftCapability.Cargo,
                AircraftAccess.Civilian,
                MaximumPayloadPounds: 2_000,
                MaximumRangeNauticalMiles: 1_000,
                TypicalCruiseKnots: 150,
                Seats: 4,
                EngineCount: 1,
                IfrCapable: true,
                Pressurized: false,
                RetractableGear: false),
            AircraftAccess.Civilian,
            new WorldEventEffects(),
            QualificationsVerified: true,
            DispatchFeasibilityVerified: true);

    private static JobContract BaseContract() =>
        new(
            ContractId:
                Guid.Parse(
                    "92000000-0000-0000-0000-000000000001"),
            EmployerId: null,
            Kind: ContractKind.Cargo,
            ServiceTrack:
                ServiceTrack.CivilianEmployment,
            OriginIcao: "KRME",
            DestinationIcao: "KSYR",
            Compensation:
                new ContractCompensation(
                    CompensationModel.PilotWage,
                    GrossCustomerRevenue: 1_200m,
                    PilotCompensation: 450m,
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),
            OfferedAt: OfferedAt,
            MustStartBy:
                OfferedAt.AddHours(2),
            MustCompleteBy:
                OfferedAt.AddHours(5),
            AircraftRequirements:
                new AircraftMissionRequirements(
                    RequiredCapabilities:
                        AircraftCapability.Cargo,
                    AllowedAccess:
                        AircraftAccess.Civilian,
                    MinimumPayloadPounds: 500,
                    MinimumRangeNauticalMiles: 400,
                    MinimumSeats: 0));

    private sealed class SingleRecoverySource(
        PersistedJobContract persisted)
        : IJobContractRecoverySource
    {
        public Task<IReadOnlyList<JobContractRecoveryCandidate>>
            ReadRecoveryCandidatesAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset updatedAt =
                persisted.Contract.CompletedAt
                ?? persisted.Contract.StartedAt
                ?? persisted.Contract.AcceptedAt
                ?? persisted.Contract.OfferedAt;

            IReadOnlyList<JobContractRecoveryCandidate> candidates =
                [
                    new JobContractRecoveryCandidate(
                        persisted.Contract.ContractId,
                        persisted.Contract.Status,
                        persisted.Version,
                        updatedAt)
                ];

            return Task.FromResult(candidates);
        }
    }

    private sealed class FakeStore(
        PersistedJobContract? initial)
        : IJobContractStore
    {
        private PersistedJobContract? _current =
            initial;

        public int UpdateCount { get; private set; }
        public long? LastExpectedVersion { get; private set; }
        public bool ThrowConcurrencyConflict { get; init; }

        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PersistedJobContract? result =
                _current?.Contract.ContractId == contractId
                    ? _current
                    : null;

            return Task.FromResult(result);
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
            UpdateCount++;
            LastExpectedVersion =
                expectedVersion;

            if (ThrowConcurrencyConflict)
            {
                throw new InvalidOperationException(
                    "Job contract changed before this update could be saved. Re-read and retry.");
            }

            if (_current is null)
            {
                throw new InvalidOperationException(
                    "Job contract does not exist.");
            }

            if (_current.Contract == contract)
            {
                return Task.FromResult(
                    JobContractSaveResult.AlreadySaved);
            }

            if (_current.Version != expectedVersion)
            {
                throw new InvalidOperationException(
                    "Version conflict.");
            }

            _current =
                new PersistedJobContract(
                    contract,
                    checked(expectedVersion + 1));

            return Task.FromResult(
                JobContractSaveResult.Updated);
        }
    }
}
