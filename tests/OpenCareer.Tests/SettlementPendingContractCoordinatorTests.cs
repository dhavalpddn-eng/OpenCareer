using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class SettlementPendingContractCoordinatorTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PendingContractSettlesThroughExistingEconomyAuthority()
    {
        PersistedJobContract completed =
            CompletedContract();

        var contractStore =
            new FakeContractStore(
                completed);

        var ledger =
            new FakeLedgerStore();

        var pendingSource =
            new SettlementPendingContractSource(
                new FakeRuntimeSource(
                    [completed]),
                ledger);

        SettlementPendingContract pending =
            Assert.Single(
                await pendingSource.ReadPendingAsync());

        var coordinator =
            new SettlementPendingContractCoordinator(
                new EconomySettlementService(
                    contractStore,
                    ledger));

        EconomySettlementResult result =
            await coordinator.SettleAsync(
                new SettlementPendingContractRequest(
                    pending,
                    new ContractSettlementCosts(
                        FuelCost:
                            100m,
                        MaintenanceReserveCost:
                            50m,
                        AirportFees:
                            25m),
                    completed.Contract.CompletedAt!.Value
                        .AddMinutes(1)));

        Assert.True(
            result.WasNewlyPosted);
        Assert.Equal(
            completed.Contract.ContractId,
            result.Settlement.ContractId);
        Assert.Equal(
            pending.SettlementIdempotencyKey,
            result.Settlement.Transaction.IdempotencyKey);
        Assert.Equal(
            825m,
            result.Settlement.NetCashChange);
        Assert.Equal(
            1,
            ledger.UniquePostCount);

        Assert.Empty(
            await pendingSource.ReadPendingAsync());
    }

    [Fact]
    public async Task ReplayUsesLedgerIdempotencyWithoutDuplicateMoney()
    {
        PersistedJobContract completed =
            CompletedContract();

        var ledger =
            new FakeLedgerStore();

        var service =
            new EconomySettlementService(
                new FakeContractStore(
                    completed),
                ledger);

        var coordinator =
            new SettlementPendingContractCoordinator(
                service);

        var pending =
            new SettlementPendingContract(
                completed,
                ContractSettlementEngine.GetIdempotencyKey(
                    completed.Contract.ContractId));

        var request =
            new SettlementPendingContractRequest(
                pending,
                new ContractSettlementCosts(
                    100m,
                    50m,
                    25m),
                completed.Contract.CompletedAt!.Value
                    .AddMinutes(1));

        EconomySettlementResult first =
            await coordinator.SettleAsync(
                request);

        EconomySettlementResult replay =
            await coordinator.SettleAsync(
                request);

        Assert.True(first.WasNewlyPosted);
        Assert.False(replay.WasNewlyPosted);
        Assert.Equal(
            1,
            ledger.UniquePostCount);
        Assert.Equal(
            first.CashBalanceAfter,
            replay.CashBalanceAfter);
    }

    [Fact]
    public async Task MismatchedSettlementKeyIsRejectedBeforeLedgerMutation()
    {
        PersistedJobContract completed =
            CompletedContract();

        var ledger =
            new FakeLedgerStore();

        var coordinator =
            new SettlementPendingContractCoordinator(
                new EconomySettlementService(
                    new FakeContractStore(
                        completed),
                    ledger));

        var pending =
            new SettlementPendingContract(
                completed,
                "contract:wrong:settlement-v1");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SettleAsync(
                new SettlementPendingContractRequest(
                    pending,
                    new ContractSettlementCosts(
                        0m,
                        0m,
                        0m),
                    completed.Contract.CompletedAt!.Value)));

        Assert.Equal(
            0,
            ledger.PostAttempts);
    }

    [Fact]
    public async Task NonCompletedPendingInputCannotReachSettlementService()
    {
        PersistedJobContract inProgress =
            CompletedContract() with
            {
                Contract =
                    CompletedContract().Contract with
                    {
                        Status =
                            ContractStatus.InProgress,
                        CompletedAt =
                            null
                    },
                Version =
                    2
            };

        inProgress.Validate();

        var ledger =
            new FakeLedgerStore();

        var coordinator =
            new SettlementPendingContractCoordinator(
                new EconomySettlementService(
                    new FakeContractStore(
                        inProgress),
                    ledger));

        var pending =
            new SettlementPendingContract(
                inProgress,
                ContractSettlementEngine.GetIdempotencyKey(
                    inProgress.Contract.ContractId));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SettleAsync(
                new SettlementPendingContractRequest(
                    pending,
                    new ContractSettlementCosts(
                        0m,
                        0m,
                        0m),
                    OfferedAt.AddHours(3))));

        Assert.Equal(
            0,
            ledger.PostAttempts);
    }

    [Fact]
    public async Task InvalidActualCostsAreRejectedBeforeLedgerMutation()
    {
        PersistedJobContract completed =
            CompletedContract();

        var ledger =
            new FakeLedgerStore();

        var coordinator =
            new SettlementPendingContractCoordinator(
                new EconomySettlementService(
                    new FakeContractStore(
                        completed),
                    ledger));

        var pending =
            new SettlementPendingContract(
                completed,
                ContractSettlementEngine.GetIdempotencyKey(
                    completed.Contract.ContractId));

        await Assert.ThrowsAsync<ArgumentException>(
            () => coordinator.SettleAsync(
                new SettlementPendingContractRequest(
                    pending,
                    new ContractSettlementCosts(
                        FuelCost:
                            1.001m,
                        MaintenanceReserveCost:
                            0m,
                        AirportFees:
                            0m),
                    completed.Contract.CompletedAt!.Value)));

        Assert.Equal(
            0,
            ledger.PostAttempts);
    }

    private static PersistedJobContract CompletedContract()
    {
        var contract =
            new JobContract(
                ContractId:
                    Guid.Parse(
                        "9a000000-0000-0000-0000-000000000001"),
                EmployerId:
                    null,
                Kind:
                    ContractKind.Cargo,
                ServiceTrack:
                    ServiceTrack.CompanyContract,
                OriginIcao:
                    "KRME",
                DestinationIcao:
                    "KSYR",
                Compensation:
                    new ContractCompensation(
                        CompensationModel.CompanyRevenue,
                        GrossCustomerRevenue:
                            1_000m,
                        PilotCompensation:
                            0m,
                        EmployerCoversFuel:
                            false,
                        EmployerCoversMaintenance:
                            false,
                        EmployerCoversAirportFees:
                            false),
                OfferedAt:
                    OfferedAt,
                MustStartBy:
                    OfferedAt.AddHours(1),
                MustCompleteBy:
                    OfferedAt.AddHours(4),
                AircraftRequirements:
                    new AircraftMissionRequirements(
                        RequiredCapabilities:
                            AircraftCapability.Cargo,
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumSeats:
                            0),
                Status:
                    ContractStatus.Completed,
                AcceptedAt:
                    OfferedAt.AddMinutes(5),
                StartedAt:
                    OfferedAt.AddMinutes(15),
                CompletedAt:
                    OfferedAt.AddHours(2));

        contract.Validate();

        return new(
            contract,
            Version: 3);
    }

    private sealed class FakeRuntimeSource(
        IReadOnlyList<PersistedJobContract> current)
        : IJobContractRuntimeSource
    {
        public bool IsInitialized =>
            true;

        public IReadOnlyList<PersistedJobContract> Current =>
            current;

        public PersistedJobContract? Find(
            Guid contractId) =>
            current.SingleOrDefault(
                item =>
                    item.Contract.ContractId
                    == contractId);
    }

    private sealed class FakeContractStore(
        PersistedJobContract contract)
        : IJobContractStore
    {
        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<PersistedJobContract?>(
                contract.Contract.ContractId
                    == contractId
                        ? contract
                        : null);
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

    private sealed class FakeLedgerStore
        : IEconomyLedgerStore
    {
        private readonly Dictionary<string, EconomyLedgerTransaction>
            _transactions =
                new(StringComparer.Ordinal);

        public int PostAttempts { get; private set; }

        public int UniquePostCount =>
            _transactions.Count;

        public Task<LedgerPostResult> PostAsync(
            EconomyLedgerTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Validate();
            PostAttempts++;

            if (_transactions.TryGetValue(
                    transaction.IdempotencyKey,
                    out EconomyLedgerTransaction? existing))
            {
                if (existing
                    != transaction)
                {
                    throw new InvalidOperationException(
                        "Conflicting duplicate settlement.");
                }

                return Task.FromResult(
                    LedgerPostResult.AlreadyPosted);
            }

            _transactions.Add(
                transaction.IdempotencyKey,
                transaction);

            return Task.FromResult(
                LedgerPostResult.Posted);
        }

        public Task<EconomyLedgerTransaction?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _transactions.TryGetValue(
                idempotencyKey,
                out EconomyLedgerTransaction? result);

            return Task.FromResult(
                result);
        }

        public Task<decimal> ReadCashBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _transactions.Values.Sum(
                    transaction =>
                        transaction.CashChange));
        }

        public Task<IReadOnlyList<LedgerAccountBalance>>
            ReadAccountBalancesAsync(
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EconomyLedgerTransaction>>
            ReadRecentAsync(
                int limit,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
