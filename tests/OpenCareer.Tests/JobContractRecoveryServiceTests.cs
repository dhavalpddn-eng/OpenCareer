using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class JobContractRecoveryServiceTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 21, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecoversHydratedContractsInSourceOrder()
    {
        PersistedJobContract accepted =
            Persisted(
                ContractStatus.Accepted,
                1,
                Guid.Parse("b1000000-0000-0000-0000-000000000001"));
        PersistedJobContract completed =
            Persisted(
                ContractStatus.Completed,
                3,
                Guid.Parse("b1000000-0000-0000-0000-000000000002"));

        var store = new FakeContractStore([accepted, completed]);
        var service = new JobContractRecoveryService(
            new FakeRecoverySource(
                [Candidate(completed), Candidate(accepted)]),
            store);

        IReadOnlyList<PersistedJobContract> recovered =
            await service.RecoverAsync();

        Assert.Collection(
            recovered,
            item => Assert.Same(completed, item),
            item => Assert.Same(accepted, item));
        Assert.Equal(2, store.ReadCount);
    }

    [Fact]
    public async Task MissingOrStaleAuthoritativeContractFailsRecovery()
    {
        PersistedJobContract current =
            Persisted(
                ContractStatus.InProgress,
                2,
                Guid.Parse("b1000000-0000-0000-0000-000000000003"));

        var missing = new JobContractRecoveryService(
            new FakeRecoverySource([Candidate(current)]),
            new FakeContractStore([]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => missing.RecoverAsync());

        JobContractRecoveryCandidate stale =
            Candidate(current) with { Version = 1 };

        var staleService = new JobContractRecoveryService(
            new FakeRecoverySource([stale]),
            new FakeContractStore([current]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => staleService.RecoverAsync());
    }

    [Fact]
    public async Task DuplicateCandidateIdentityFailsBeforeSecondRead()
    {
        PersistedJobContract accepted =
            Persisted(
                ContractStatus.Accepted,
                1,
                Guid.Parse("b1000000-0000-0000-0000-000000000004"));
        JobContractRecoveryCandidate candidate =
            Candidate(accepted);
        var store = new FakeContractStore([accepted]);
        var service = new JobContractRecoveryService(
            new FakeRecoverySource([candidate, candidate]),
            store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecoverAsync());

        Assert.Equal(1, store.ReadCount);
    }

    [Fact]
    public async Task MillisecondIndexTimestampMatchesFinerContractTimestamp()
    {
        DateTimeOffset acceptedAt =
            OfferedAt.AddMinutes(10).AddTicks(4_567);

        JobContract contract =
            CreateContract(
                Guid.Parse("b1000000-0000-0000-0000-000000000005"))
            with
            {
                Status = ContractStatus.Accepted,
                AcceptedAt = acceptedAt
            };
        contract.Validate();

        var persisted = new PersistedJobContract(contract, 1);
        var candidate = new JobContractRecoveryCandidate(
            contract.ContractId,
            contract.Status,
            1,
            DateTimeOffset.FromUnixTimeMilliseconds(
                acceptedAt.ToUnixTimeMilliseconds()));

        var service = new JobContractRecoveryService(
            new FakeRecoverySource([candidate]),
            new FakeContractStore([persisted]));

        IReadOnlyList<PersistedJobContract> recovered =
            await service.RecoverAsync();

        Assert.Single(recovered);
        Assert.Same(persisted, recovered[0]);
    }

    private static JobContractRecoveryCandidate Candidate(
        PersistedJobContract persisted) =>
        new(
            persisted.Contract.ContractId,
            persisted.Contract.Status,
            persisted.Version,
            UpdatedAt(persisted.Contract));

    private static PersistedJobContract Persisted(
        ContractStatus status,
        long version,
        Guid contractId)
    {
        JobContract contract =
            CreateContract(contractId);

        contract =
            status switch
            {
                ContractStatus.Accepted =>
                    contract with
                    {
                        Status = ContractStatus.Accepted,
                        AcceptedAt = OfferedAt.AddMinutes(10)
                    },
                ContractStatus.InProgress =>
                    contract with
                    {
                        Status = ContractStatus.InProgress,
                        AcceptedAt = OfferedAt.AddMinutes(10),
                        StartedAt = OfferedAt.AddMinutes(20)
                    },
                ContractStatus.Completed =>
                    contract with
                    {
                        Status = ContractStatus.Completed,
                        AcceptedAt = OfferedAt.AddMinutes(10),
                        StartedAt = OfferedAt.AddMinutes(20),
                        CompletedAt = OfferedAt.AddMinutes(50)
                    },
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };

        contract.Validate();
        var persisted =
            new PersistedJobContract(contract, version);
        persisted.Validate();
        return persisted;
    }

    private static DateTimeOffset UpdatedAt(
        JobContract contract) =>
        contract.CompletedAt
        ?? contract.StartedAt
        ?? contract.AcceptedAt
        ?? contract.OfferedAt;

    private static JobContract CreateContract(
        Guid contractId) =>
        new(
            ContractId: contractId,
            EmployerId:
                Guid.Parse("b1000000-0000-0000-0000-000000000010"),
            Kind: ContractKind.Cargo,
            ServiceTrack: ServiceTrack.CivilianEmployment,
            OriginIcao: "KRME",
            DestinationIcao: "KSYR",
            Compensation:
                new ContractCompensation(
                    CompensationModel.PilotWage,
                    1_250m,
                    450m,
                    true,
                    true,
                    true),
            OfferedAt: OfferedAt,
            MustStartBy: OfferedAt.AddHours(2),
            MustCompleteBy: OfferedAt.AddHours(5),
            AircraftRequirements:
                new AircraftMissionRequirements(
                    AircraftCapability.Cargo,
                    AircraftAccess.Civilian,
                    MinimumPayloadPounds: 500,
                    MinimumRangeNauticalMiles: 400,
                    MinimumSeats: 0),
            ReputationReward: 1.25,
            ReputationPenalty: 2.5,
            MarketId: "KRME:general-cargo");

    private sealed class FakeRecoverySource(
        IReadOnlyList<JobContractRecoveryCandidate> candidates)
        : IJobContractRecoverySource
    {
        public Task<IReadOnlyList<JobContractRecoveryCandidate>>
            ReadRecoveryCandidatesAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(candidates);
        }
    }

    private sealed class FakeContractStore(
        IEnumerable<PersistedJobContract> contracts)
        : IJobContractStore
    {
        private readonly Dictionary<Guid, PersistedJobContract>
            _contracts =
                contracts.ToDictionary(x => x.Contract.ContractId);

        public int ReadCount { get; private set; }

        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            _contracts.TryGetValue(
                contractId,
                out PersistedJobContract? result);
            return Task.FromResult(result);
        }

        public Task<JobContractSaveResult> CreateJobContractAsync(
            JobContract contract,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobContractSaveResult> UpdateJobContractAsync(
            JobContract contract,
            long expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
