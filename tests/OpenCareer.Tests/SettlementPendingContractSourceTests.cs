using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class SettlementPendingContractSourceTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(
            2026,
            9,
            21,
            8,
            0,
            0,
            TimeSpan.Zero);

    [Fact]
    public async Task ExposesOnlyCompletedRuntimeContractsWithoutPostedSettlement()
    {
        PersistedJobContract completed =
            Persisted(
                ContractStatus.Completed,
                version:
                    3);

        PersistedJobContract accepted =
            Persisted(
                ContractStatus.Accepted,
                version:
                    1,
                contractId:
                    Guid.Parse(
                        "94000000-0000-0000-0000-000000000002"));

        var ledger =
            new FakeLedgerStore();

        var source =
            new SettlementPendingContractSource(
                new FakeRuntimeSource(
                    isInitialized:
                        true,
                    [
                        accepted,
                        completed
                    ]),
                ledger);

        IReadOnlyList<SettlementPendingContract> pending =
            await source.ReadPendingAsync();

        SettlementPendingContract item =
            Assert.Single(pending);

        Assert.Same(
            completed,
            item.PersistedContract);
        Assert.Equal(
            ContractSettlementEngine
                .GetIdempotencyKey(
                    completed.Contract.ContractId),
            item.SettlementIdempotencyKey);
        Assert.Equal(
            1,
            ledger.LookupCount);
        Assert.Equal(
            0,
            ledger.PostCount);
    }

    [Fact]
    public async Task AlreadySettledCompletedContractIsNotPending()
    {
        PersistedJobContract completed =
            Persisted(
                ContractStatus.Completed,
                version:
                    3);

        string key =
            ContractSettlementEngine
                .GetIdempotencyKey(
                    completed.Contract.ContractId);

        var ledger =
            new FakeLedgerStore(
                new EconomyLedgerTransaction(
                    TransactionId:
                        completed.Contract.ContractId,
                    IdempotencyKey:
                        key,
                    OccurredAt:
                        completed.Contract.CompletedAt!.Value,
                    Description:
                        "Existing settlement",
                    ReferenceType:
                        nameof(JobContract),
                    ReferenceId:
                        completed.Contract.ContractId.ToString("D"),
                    Postings:
                        [
                            LedgerPosting.DebitTo(
                                LedgerAccountCode.Cash,
                                1m,
                                "Existing"),
                            LedgerPosting.CreditTo(
                                LedgerAccountCode.ContractRevenue,
                                1m,
                                "Existing")
                        ]));

        var source =
            new SettlementPendingContractSource(
                new FakeRuntimeSource(
                    isInitialized:
                        true,
                    [completed]),
                ledger);

        IReadOnlyList<SettlementPendingContract> pending =
            await source.ReadPendingAsync();

        Assert.Empty(pending);
        Assert.Equal(
            1,
            ledger.LookupCount);
        Assert.Equal(
            0,
            ledger.PostCount);
    }

    [Fact]
    public async Task RuntimeSourceMustBeInitialized()
    {
        var ledger =
            new FakeLedgerStore();

        var source =
            new SettlementPendingContractSource(
                new FakeRuntimeSource(
                    isInitialized:
                        false,
                    Array.Empty<PersistedJobContract>()),
                ledger);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                source.ReadPendingAsync());

        Assert.Equal(
            0,
            ledger.LookupCount);
    }

    private static PersistedJobContract Persisted(
        ContractStatus status,
        long version,
        Guid? contractId = null)
    {
        DateTimeOffset acceptedAt =
            OfferedAt.AddMinutes(5);
        DateTimeOffset startedAt =
            OfferedAt.AddMinutes(15);
        DateTimeOffset completedAt =
            OfferedAt.AddHours(2);

        JobContract contract =
            new(
                ContractId:
                    contractId
                    ?? Guid.Parse(
                        "94000000-0000-0000-0000-000000000001"),
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
                    status,
                AcceptedAt:
                    status is ContractStatus.Accepted
                        or ContractStatus.InProgress
                        or ContractStatus.Completed
                            ? acceptedAt
                            : null,
                StartedAt:
                    status is ContractStatus.InProgress
                        or ContractStatus.Completed
                            ? startedAt
                            : null,
                CompletedAt:
                    status == ContractStatus.Completed
                        ? completedAt
                        : null);

        contract.Validate();

        var persisted =
            new PersistedJobContract(
                contract,
                version);

        persisted.Validate();
        return persisted;
    }

    private sealed class FakeRuntimeSource(
        bool isInitialized,
        IReadOnlyList<PersistedJobContract> current)
        : IJobContractRuntimeSource
    {
        public bool IsInitialized =>
            isInitialized;

        public IReadOnlyList<PersistedJobContract> Current =>
            current;

        public PersistedJobContract? Find(
            Guid contractId) =>
            current.SingleOrDefault(
                item =>
                    item.Contract.ContractId
                    == contractId);
    }

    private sealed class FakeLedgerStore(
        EconomyLedgerTransaction? existing = null)
        : IEconomyLedgerStore
    {
        public int LookupCount { get; private set; }

        public int PostCount { get; private set; }

        public Task<LedgerPostResult> PostAsync(
            EconomyLedgerTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PostCount++;

            return Task.FromResult(
                LedgerPostResult.Posted);
        }

        public Task<EconomyLedgerTransaction?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LookupCount++;

            EconomyLedgerTransaction? result =
                existing is not null
                && string.Equals(
                    existing.IdempotencyKey,
                    idempotencyKey,
                    StringComparison.Ordinal)
                    ? existing
                    : null;

            return Task.FromResult(result);
        }

        public Task<decimal> ReadCashBalanceAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
