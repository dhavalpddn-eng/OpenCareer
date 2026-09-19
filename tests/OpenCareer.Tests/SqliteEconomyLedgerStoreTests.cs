using OpenCareer.Application.Economy;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Infrastructure.Economy;

namespace OpenCareer.Tests;

public sealed class SqliteEconomyLedgerStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public void PersistedLedgerAccountCodesRemainStable()
    {
        Assert.Equal(0, (int)LedgerAccountCode.Cash);
        Assert.Equal(8, (int)LedgerAccountCode.InsuranceExpense);
        Assert.Equal(9, (int)LedgerAccountCode.InterestExpense);
        Assert.Equal(10, (int)LedgerAccountCode.AircraftAsset);
        Assert.Equal(11, (int)LedgerAccountCode.LoanPayable);
        Assert.Equal(12, (int)LedgerAccountCode.StorageExpense);
        Assert.Equal(13, (int)LedgerAccountCode.OpeningEquity);
    }

    [Fact]
    public async Task SettlementPostsExactlyOnceAndSurvivesReopen()
    {
        string databasePath =
            Path.Combine(_directory, "career.db");

        JobContract contract =
            EconomySettlementTests.CompletedContract(
                new ContractCompensation(
                    CompensationModel.CompanyRevenue,
                    GrossCustomerRevenue: 3_000m,
                    PilotCompensation: 0m,
                    EmployerCoversFuel: false,
                    EmployerCoversMaintenance: false,
                    EmployerCoversAirportFees: false),
                ServiceTrack.CompanyContract);

        var costs =
            new ContractSettlementCosts(
                FuelCost: 400m,
                MaintenanceReserveCost: 100m,
                AirportFees: 50m);

        DateTimeOffset settledAt =
            contract.CompletedAt!.Value.AddMinutes(2);

        var firstStore =
            new SqliteEconomyLedgerStore(databasePath);
        var service =
            new EconomySettlementService(firstStore);

        EconomySettlementResult first =
            await service.SettleCompletedContractAsync(
                contract,
                costs,
                settledAt);

        EconomySettlementResult duplicate =
            await service.SettleCompletedContractAsync(
                contract,
                costs,
                settledAt);

        Assert.True(first.WasNewlyPosted);
        Assert.False(duplicate.WasNewlyPosted);
        Assert.Equal(2_450m, first.CashBalanceAfter);
        Assert.Equal(2_450m, duplicate.CashBalanceAfter);

        var reopened =
            new SqliteEconomyLedgerStore(databasePath);

        Assert.Equal(
            2_450m,
            await reopened.ReadCashBalanceAsync());

        IReadOnlyList<EconomyLedgerTransaction> recent =
            await reopened.ReadRecentAsync(10);

        EconomyLedgerTransaction transaction =
            Assert.Single(recent);
        Assert.Equal(
            contract.ContractId,
            transaction.TransactionId);
        Assert.Equal(
            2_450m,
            transaction.CashChange);
    }

    [Fact]
    public async Task ConflictingDuplicateIsRejectedInsteadOfSilentlyChangingMoney()
    {
        string databasePath =
            Path.Combine(_directory, "career.db");

        var store =
            new SqliteEconomyLedgerStore(databasePath);

        Guid id = Guid.NewGuid();
        var original =
            CreateBalancedTransaction(
                id,
                "settlement:one",
                500m,
                "Original");

        Assert.Equal(
            LedgerPostResult.Posted,
            await store.PostAsync(original));

        var conflicting =
            original with
            {
                Description = "Changed after posting"
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.PostAsync(conflicting));

        Assert.Equal(
            500m,
            await store.ReadCashBalanceAsync());
    }

    [Fact]
    public async Task SameIdempotencyKeyWithDifferentTransactionIdIsRejected()
    {
        string databasePath =
            Path.Combine(_directory, "career.db");

        var store =
            new SqliteEconomyLedgerStore(databasePath);

        var first =
            CreateBalancedTransaction(
                Guid.NewGuid(),
                "stable-key",
                100m,
                "First");
        var second =
            CreateBalancedTransaction(
                Guid.NewGuid(),
                "stable-key",
                100m,
                "Second");

        await store.PostAsync(first);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.PostAsync(second));

        Assert.Equal(
            100m,
            await store.ReadCashBalanceAsync());
    }

    [Fact]
    public async Task RecentTransactionsReturnNewestFirst()
    {
        string databasePath =
            Path.Combine(_directory, "career.db");

        var store =
            new SqliteEconomyLedgerStore(databasePath);

        var first =
            CreateBalancedTransaction(
                Guid.NewGuid(),
                "first",
                100m,
                "First",
                occurredAt:
                    new DateTimeOffset(
                        2026,
                        9,
                        18,
                        8,
                        0,
                        0,
                        TimeSpan.Zero));

        var second =
            CreateBalancedTransaction(
                Guid.NewGuid(),
                "second",
                200m,
                "Second",
                occurredAt:
                    new DateTimeOffset(
                        2026,
                        9,
                        18,
                        9,
                        0,
                        0,
                        TimeSpan.Zero));

        await store.PostAsync(first);
        await store.PostAsync(second);

        IReadOnlyList<EconomyLedgerTransaction> recent =
            await store.ReadRecentAsync(10);

        Assert.Equal(2, recent.Count);
        Assert.Equal(second.TransactionId, recent[0].TransactionId);
        Assert.Equal(first.TransactionId, recent[1].TransactionId);
        Assert.Equal(300m, await store.ReadCashBalanceAsync());
    }

    [Fact]
    public async Task ZeroValueSettlementCanStillBeRecordedIdempotently()
    {
        string databasePath =
            Path.Combine(_directory, "career.db");

        var store =
            new SqliteEconomyLedgerStore(databasePath);

        var transaction =
            new EconomyLedgerTransaction(
                Guid.NewGuid(),
                "zero-settlement",
                new DateTimeOffset(
                    2026,
                    9,
                    18,
                    10,
                    0,
                    0,
                    TimeSpan.Zero),
                "Zero settlement",
                "Test",
                "zero",
                Array.Empty<LedgerPosting>());

        Assert.Equal(
            LedgerPostResult.Posted,
            await store.PostAsync(transaction));
        Assert.Equal(
            LedgerPostResult.AlreadyPosted,
            await store.PostAsync(transaction));
        Assert.Equal(
            0m,
            await store.ReadCashBalanceAsync());
        Assert.Single(
            await store.ReadRecentAsync(10));
    }

    [Fact]
    public async Task ConcurrentDuplicatePostsCreditCashOnlyOnce()
    {
        string databasePath =
            Path.Combine(_directory, "career.db");

        var store =
            new SqliteEconomyLedgerStore(databasePath);

        var transaction =
            CreateBalancedTransaction(
                Guid.NewGuid(),
                "concurrent-settlement",
                750m,
                "Concurrent settlement");

        LedgerPostResult[] results =
            await Task.WhenAll(
                Enumerable.Range(0, 12)
                    .Select(_ => store.PostAsync(transaction)));

        Assert.Single(
            results,
            result => result == LedgerPostResult.Posted);
        Assert.Equal(
            11,
            results.Count(
                result => result == LedgerPostResult.AlreadyPosted));
        Assert.Equal(
            750m,
            await store.ReadCashBalanceAsync());
        Assert.Single(
            await store.ReadRecentAsync(10));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static EconomyLedgerTransaction CreateBalancedTransaction(
        Guid id,
        string idempotencyKey,
        decimal amount,
        string description,
        DateTimeOffset? occurredAt = null) =>
        new(
            id,
            idempotencyKey,
            occurredAt
                ?? new DateTimeOffset(
                    2026,
                    9,
                    18,
                    8,
                    0,
                    0,
                    TimeSpan.Zero),
            description,
            "Test",
            id.ToString("D"),
            [
                LedgerPosting.DebitTo(
                    LedgerAccountCode.Cash,
                    amount,
                    "Cash"),
                LedgerPosting.CreditTo(
                    LedgerAccountCode.ContractRevenue,
                    amount,
                    "Income")
            ]);
}
