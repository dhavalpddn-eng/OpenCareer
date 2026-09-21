using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Economy;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteEconomyLedgerStoreTests : IAsyncLifetime
{
    private string _root = string.Empty;
    private string _databasePath = string.Empty;

    public Task InitializeAsync()
    {
        _root =
            Path.Combine(
                Path.GetTempPath(),
                "OpenCareer.Tests",
                Guid.NewGuid().ToString("N"));

        _databasePath =
            Path.Combine(
                _root,
                "opencareer.db");

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Test cleanup should not mask the actual assertion result.
        }

        return Task.CompletedTask;
    }

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
    public async Task BalancedTransactionPostsExactlyOnceAndSurvivesReopen()
    {
        EconomyLedgerTransaction transaction =
            BalancedTransaction(
                Guid.Parse("80000000-0000-0000-0000-000000000001"),
                "fixture:income",
                500m,
                "Income");

        SqliteEconomyLedgerStore first =
            CreateStore();

        Assert.Equal(
            LedgerPostResult.Posted,
            await first.PostAsync(transaction));

        Assert.Equal(
            LedgerPostResult.AlreadyPosted,
            await first.PostAsync(transaction));

        SqliteEconomyLedgerStore reopened =
            CreateStore();

        Assert.Equal(
            500m,
            await reopened.ReadCashBalanceAsync());

        EconomyLedgerTransaction? found =
            await reopened.FindByIdempotencyKeyAsync(
                "fixture:income");

        Assert.Equal(transaction, found);
    }

    [Fact]
    public async Task ConflictingDuplicateCannotChangeMoney()
    {
        EconomyLedgerTransaction original =
            BalancedTransaction(
                Guid.Parse("80000000-0000-0000-0000-000000000002"),
                "fixture:conflict",
                250m,
                "Original");

        SqliteEconomyLedgerStore store =
            CreateStore();

        await store.PostAsync(original);

        EconomyLedgerTransaction conflicting =
            original with
            {
                Description = "Changed"
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.PostAsync(conflicting));

        Assert.Equal(
            250m,
            await store.ReadCashBalanceAsync());
    }

    [Fact]
    public async Task SameIdempotencyKeyWithDifferentIdIsRejected()
    {
        SqliteEconomyLedgerStore store =
            CreateStore();

        EconomyLedgerTransaction first =
            BalancedTransaction(
                Guid.Parse("80000000-0000-0000-0000-000000000003"),
                "fixture:stable",
                100m,
                "First");

        EconomyLedgerTransaction second =
            BalancedTransaction(
                Guid.Parse("80000000-0000-0000-0000-000000000004"),
                "fixture:stable",
                100m,
                "Second");

        await store.PostAsync(first);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.PostAsync(second));

        Assert.Equal(
            100m,
            await store.ReadCashBalanceAsync());
    }

    [Fact]
    public async Task RecentTransactionsAreNewestFirstAndBalancesReconcile()
    {
        SqliteEconomyLedgerStore store =
            CreateStore();

        EconomyLedgerTransaction first =
            BalancedTransaction(
                Guid.Parse("80000000-0000-0000-0000-000000000005"),
                "fixture:first",
                100m,
                "First",
                new DateTimeOffset(
                    2026,
                    9,
                    21,
                    8,
                    0,
                    0,
                    TimeSpan.Zero));

        EconomyLedgerTransaction second =
            new(
                Guid.Parse("80000000-0000-0000-0000-000000000006"),
                "fixture:expense",
                new DateTimeOffset(
                    2026,
                    9,
                    21,
                    9,
                    0,
                    0,
                    TimeSpan.Zero),
                "Expense",
                "Test",
                "expense",
                [
                    LedgerPosting.DebitTo(
                        LedgerAccountCode.FuelExpense,
                        25m,
                        "Fuel"),
                    LedgerPosting.CreditTo(
                        LedgerAccountCode.Cash,
                        25m,
                        "Cash")
                ]);

        await store.PostAsync(first);
        await store.PostAsync(second);

        IReadOnlyList<EconomyLedgerTransaction> recent =
            await store.ReadRecentAsync(10);

        Assert.Equal(
            [second.TransactionId, first.TransactionId],
            recent.Select(x => x.TransactionId).ToArray());

        Assert.Equal(
            75m,
            await store.ReadCashBalanceAsync());

        IReadOnlyList<LedgerAccountBalance> balances =
            await store.ReadAccountBalancesAsync();

        Assert.Equal(
            100m,
            balances.Single(
                x => x.Account == LedgerAccountCode.ContractRevenue)
                .NetCredit);

        Assert.Equal(
            25m,
            balances.Single(
                x => x.Account == LedgerAccountCode.FuelExpense)
                .NetDebit);
    }

    [Fact]
    public async Task ConcurrentDuplicatePostsCreditCashOnlyOnce()
    {
        SqliteEconomyLedgerStore store =
            CreateStore();

        EconomyLedgerTransaction transaction =
            BalancedTransaction(
                Guid.Parse("80000000-0000-0000-0000-000000000007"),
                "fixture:concurrent",
                750m,
                "Concurrent");

        LedgerPostResult[] results =
            await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(
                        _ => store.PostAsync(transaction)));

        Assert.Single(
            results,
            x => x == LedgerPostResult.Posted);

        Assert.Equal(
            7,
            results.Count(
                x => x == LedgerPostResult.AlreadyPosted));

        Assert.Equal(
            750m,
            await store.ReadCashBalanceAsync());
    }

    private SqliteEconomyLedgerStore CreateStore() =>
        new(
            new OpenCareerDatabaseOptions(_databasePath),
            NullLogger<SqliteEconomyLedgerStore>.Instance);

    private static EconomyLedgerTransaction BalancedTransaction(
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
                    21,
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
