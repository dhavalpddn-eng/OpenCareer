using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class JobContractRuntimeStateTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 21, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InitializePublishesRecoveredContractsOnlyOnce()
    {
        PersistedJobContract accepted =
            AcceptedContract(
                Guid.Parse(
                    "c1000000-0000-0000-0000-000000000001"));

        var source =
            new FakeRecoverySource(
                [Candidate(accepted)]);
        var store =
            new FakeContractStore(
                [accepted]);

        var state =
            new JobContractRuntimeState(
                new JobContractRecoveryService(
                    source,
                    store));

        IReadOnlyList<PersistedJobContract> first =
            await state.InitializeAsync();
        IReadOnlyList<PersistedJobContract> second =
            await state.InitializeAsync();

        Assert.True(state.IsInitialized);
        Assert.Same(first, second);
        Assert.Same(first, state.Current);
        Assert.Single(first);
        Assert.Same(accepted, first[0]);
        Assert.Equal(1, source.ReadCount);
        Assert.Equal(1, store.ReadCount);

        IJobContractRuntimeSource readOnly = state;
        Assert.Same(first, readOnly.Current);
        Assert.Same(
            accepted,
            readOnly.Find(
                accepted.Contract.ContractId));
        Assert.Null(
            readOnly.Find(
                Guid.Parse(
                    "c1000000-0000-0000-0000-000000000099")));
    }

    [Fact]
    public async Task AuthoritativePublicationAddsAcceptedContractAndIsIdempotent()
    {
        var state =
            new JobContractRuntimeState(
                new JobContractRecoveryService(
                    new FakeRecoverySource(
                        Array.Empty<JobContractRecoveryCandidate>()),
                    new FakeContractStore(
                        Array.Empty<PersistedJobContract>())));

        PersistedJobContract accepted =
            AcceptedContract(
                Guid.Parse(
                    "c1000000-0000-0000-0000-000000000003"));

        await state.PublishAuthoritativeAsync(
            accepted);
        await state.PublishAuthoritativeAsync(
            accepted);

        Assert.True(state.IsInitialized);
        Assert.Single(state.Current);
        Assert.Same(
            accepted,
            state.Find(
                accepted.Contract.ContractId));
    }

    [Fact]
    public async Task FailedInitializationCanRetryWithoutPublishingPartialState()
    {
        PersistedJobContract accepted =
            AcceptedContract(
                Guid.Parse(
                    "c1000000-0000-0000-0000-000000000002"));

        var source =
            new FakeRecoverySource(
                [Candidate(accepted)])
            {
                FailNextRead = true
            };

        var state =
            new JobContractRuntimeState(
                new JobContractRecoveryService(
                    source,
                    new FakeContractStore(
                        [accepted])));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => state.InitializeAsync());

        Assert.False(state.IsInitialized);
        Assert.Empty(state.Current);

        IReadOnlyList<PersistedJobContract> recovered =
            await state.InitializeAsync();

        Assert.True(state.IsInitialized);
        Assert.Single(recovered);
        Assert.Same(accepted, recovered[0]);
        Assert.Equal(2, source.ReadCount);
    }

    private static JobContractRecoveryCandidate Candidate(
        PersistedJobContract persisted) =>
        new(
            persisted.Contract.ContractId,
            persisted.Contract.Status,
            persisted.Version,
            persisted.Contract.AcceptedAt!.Value);

    private static PersistedJobContract AcceptedContract(
        Guid contractId)
    {
        JobContract contract =
            new(
                ContractId: contractId,
                EmployerId:
                    Guid.Parse(
                        "c1000000-0000-0000-0000-000000000010"),
                Kind: ContractKind.Cargo,
                ServiceTrack:
                    ServiceTrack.CivilianEmployment,
                OriginIcao: "KRME",
                DestinationIcao: "KSYR",
                Compensation:
                    new ContractCompensation(
                        CompensationModel.PilotWage,
                        GrossCustomerRevenue: 1_250m,
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
                        MinimumSeats: 0),
                Status: ContractStatus.Accepted,
                ReputationReward: 1.25,
                ReputationPenalty: 2.5,
                MarketId: "KRME:general-cargo",
                AcceptedAt:
                    OfferedAt.AddMinutes(10));

        contract.Validate();

        var persisted =
            new PersistedJobContract(
                contract,
                Version: 1);
        persisted.Validate();
        return persisted;
    }

    private sealed class FakeRecoverySource(
        IReadOnlyList<JobContractRecoveryCandidate> candidates)
        : IJobContractRecoverySource
    {
        public bool FailNextRead { get; set; }

        public int ReadCount { get; private set; }

        public Task<IReadOnlyList<JobContractRecoveryCandidate>>
            ReadRecoveryCandidatesAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;

            if (FailNextRead)
            {
                FailNextRead = false;
                throw new InvalidOperationException(
                    "Synthetic recovery read failure.");
            }

            return Task.FromResult(candidates);
        }
    }

    private sealed class FakeContractStore(
        IEnumerable<PersistedJobContract> contracts)
        : IJobContractStore
    {
        private readonly Dictionary<Guid, PersistedJobContract>
            _contracts =
                contracts.ToDictionary(
                    item => item.Contract.ContractId);

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
