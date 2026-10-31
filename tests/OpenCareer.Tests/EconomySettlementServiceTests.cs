using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class EconomySettlementServiceTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SettlesPersistedCompletedContractExactlyOnce()
    {
        JobContract completed =
            CompletedContract();

        var contractStore =
            new FakeContractStore(
                new PersistedJobContract(
                    completed,
                    Version: 3));

        var ledgerStore =
            new FakeLedgerStore();

        var service =
            new EconomySettlementService(
                contractStore,
                ledgerStore);

        DateTimeOffset settledAt =
            completed.CompletedAt!.Value.AddMinutes(1);

        EconomySettlementResult first =
            await service.SettleCompletedContractAsync(
                completed.ContractId,
                new ContractSettlementCosts(
                    FuelCost: 100m,
                    MaintenanceReserveCost: 50m,
                    AirportFees: 25m),
                settledAt);

        EconomySettlementResult duplicate =
            await service.SettleCompletedContractAsync(
                completed.ContractId,
                new ContractSettlementCosts(
                    FuelCost: 100m,
                    MaintenanceReserveCost: 50m,
                    AirportFees: 25m),
                settledAt);

        Assert.True(first.WasNewlyPosted);
        Assert.False(duplicate.WasNewlyPosted);
        Assert.Equal(
            first.Settlement.ContractId,
            duplicate.Settlement.ContractId);
        Assert.Equal(
            first.Settlement.GrossCashReceipt,
            duplicate.Settlement.GrossCashReceipt);
        Assert.Equal(
            first.Settlement.PlayerOperatingCosts,
            duplicate.Settlement.PlayerOperatingCosts);
        Assert.Equal(
            first.Settlement.NetCashChange,
            duplicate.Settlement.NetCashChange);
        Assert.True(
            Equivalent(
                first.Settlement.Transaction,
                duplicate.Settlement.Transaction));
        Assert.Equal(
            first.CashBalanceAfter,
            duplicate.CashBalanceAfter);
        Assert.Equal(
            1,
            ledgerStore.UniquePostCount);
        Assert.Equal(
            2,
            contractStore.ReadCount);
    }

    [Fact]
    public async Task MissingPersistedContractCannotSettle()
    {
        var service =
            new EconomySettlementService(
                new FakeContractStore(null),
                new FakeLedgerStore());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SettleCompletedContractAsync(
                Guid.Parse(
                    "91000000-0000-0000-0000-000000000001"),
                new ContractSettlementCosts(
                    0m,
                    0m,
                    0m),
                OfferedAt.AddHours(3)));
    }

    [Fact]
    public async Task PersistedIncompleteContractCannotSettle()
    {
        JobContract offered =
            BaseContract();

        var ledgerStore =
            new FakeLedgerStore();

        var service =
            new EconomySettlementService(
                new FakeContractStore(
                    new PersistedJobContract(
                        offered,
                        Version: 0)),
                ledgerStore);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SettleCompletedContractAsync(
                offered.ContractId,
                new ContractSettlementCosts(
                    0m,
                    0m,
                    0m),
                OfferedAt.AddHours(3)));

        Assert.Equal(
            0,
            ledgerStore.PostAttempts);
    }

    [Fact]
    public async Task SettlementUsesPersistedContractTerms()
    {
        JobContract completed =
            CompletedContract();

        var ledgerStore =
            new FakeLedgerStore();

        var service =
            new EconomySettlementService(
                new FakeContractStore(
                    new PersistedJobContract(
                        completed,
                        Version: 4)),
                ledgerStore);

        EconomySettlementResult result =
            await service.SettleCompletedContractAsync(
                completed.ContractId,
                new ContractSettlementCosts(
                    FuelCost: 100m,
                    MaintenanceReserveCost: 0m,
                    AirportFees: 0m),
                completed.CompletedAt!.Value.AddMinutes(1));

        Assert.Equal(
            completed,
            result.PersistedContract.Contract);
        Assert.Equal(
            completed.ContractId,
            result.Settlement.Transaction.TransactionId);
        Assert.Equal(
            $"contract:{completed.ContractId:D}:settlement-v1",
            result.Settlement.Transaction.IdempotencyKey);
    }

    [Fact]
    public async Task ConcurrentDuplicatesOnlyCreateOneLedgerEntry()
    {
        JobContract completed =
            CompletedContract();

        var ledgerStore =
            new FakeLedgerStore();

        var service =
            new EconomySettlementService(
                new FakeContractStore(
                    new PersistedJobContract(
                        completed,
                        Version: 2)),
                ledgerStore);

        Task<EconomySettlementResult>[] tasks =
            Enumerable.Range(0, 8)
                .Select(
                    _ => service.SettleCompletedContractAsync(
                        completed.ContractId,
                        new ContractSettlementCosts(
                            0m,
                            0m,
                            0m),
                        completed.CompletedAt!.Value.AddMinutes(1)))
                .ToArray();

        EconomySettlementResult[] results =
            await Task.WhenAll(tasks);

        Assert.Single(
            results,
            x => x.WasNewlyPosted);
        Assert.Equal(
            1,
            ledgerStore.UniquePostCount);
    }

    private static JobContract CompletedContract() =>
        BaseContract() with
        {
            Status = ContractStatus.Completed,
            AcceptedAt =
                OfferedAt.AddMinutes(5),
            StartedAt =
                OfferedAt.AddMinutes(15),
            CompletedAt =
                OfferedAt.AddHours(2)
        };

    private static JobContract BaseContract() =>
        new(
            ContractId:
                Guid.Parse(
                    "91000000-0000-0000-0000-000000000001"),
            EmployerId: null,
            Kind: ContractKind.Cargo,
            ServiceTrack:
                ServiceTrack.CompanyContract,
            OriginIcao: "KRME",
            DestinationIcao: "KSYR",
            Compensation:
                new ContractCompensation(
                    CompensationModel.CompanyRevenue,
                    GrossCustomerRevenue: 1_000m,
                    PilotCompensation: 0m,
                    EmployerCoversFuel: false,
                    EmployerCoversMaintenance: false,
                    EmployerCoversAirportFees: false),
            OfferedAt: OfferedAt,
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
                    MinimumSeats: 0));

    private static bool Equivalent(
        EconomyLedgerTransaction left,
        EconomyLedgerTransaction right) =>
        left.TransactionId == right.TransactionId
        && string.Equals(
            left.IdempotencyKey,
            right.IdempotencyKey,
            StringComparison.Ordinal)
        && left.OccurredAt == right.OccurredAt
        && string.Equals(
            left.Description,
            right.Description,
            StringComparison.Ordinal)
        && string.Equals(
            left.ReferenceType,
            right.ReferenceType,
            StringComparison.Ordinal)
        && string.Equals(
            left.ReferenceId,
            right.ReferenceId,
            StringComparison.Ordinal)
        && left.Postings.SequenceEqual(right.Postings);

    private sealed class FakeContractStore(
        PersistedJobContract? contract)
        : IJobContractStore
    {
        public int ReadCount { get; private set; }

        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;

            PersistedJobContract? result =
                contract?.Contract.ContractId == contractId
                    ? contract
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
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeLedgerStore : IEconomyLedgerStore
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
                if (!Equivalent(existing, transaction))
                {
                    throw new InvalidOperationException(
                        "Conflicting duplicate.");
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
                out EconomyLedgerTransaction? transaction);

            return Task.FromResult(transaction);
        }

        public Task<decimal> ReadCashBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _transactions.Values.Sum(
                    x => x.CashChange));
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
