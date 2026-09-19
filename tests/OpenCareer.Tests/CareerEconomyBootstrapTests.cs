using OpenCareer.Application.Economy;
using OpenCareer.Domain.Economy;
using OpenCareer.Infrastructure.Economy;

namespace OpenCareer.Tests;

public sealed class CareerEconomyBootstrapTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.CareerEconomyBootstrapTests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OpeningBalancePostsExactlyOnceAndPersists()
    {
        string databasePath =
            Path.Combine(
                _directory,
                "career.db");

        Guid careerId = Guid.NewGuid();
        DateTimeOffset createdAt =
            new(
                2026,
                9,
                18,
                12,
                0,
                0,
                TimeSpan.Zero);

        var firstStore =
            new SqliteEconomyLedgerStore(
                databasePath);
        var firstService =
            new CareerEconomyBootstrapService(
                firstStore);

        CareerEconomyBootstrapResult first =
            await firstService.InitializeAsync(
                careerId,
                openingCash: 5_000m,
                createdAt);

        CareerEconomyBootstrapResult duplicate =
            await firstService.InitializeAsync(
                careerId,
                openingCash: 5_000m,
                createdAt);

        Assert.True(first.WasNewlyInitialized);
        Assert.False(duplicate.WasNewlyInitialized);
        Assert.Equal(5_000m, first.CashBalanceAfter);
        Assert.Equal(5_000m, duplicate.CashBalanceAfter);

        var reopened =
            new SqliteEconomyLedgerStore(
                databasePath);

        Assert.Equal(
            5_000m,
            await reopened.ReadCashBalanceAsync());

        EconomyLedgerTransaction transaction =
            Assert.Single(
                await reopened.ReadRecentAsync(10));

        Assert.Contains(
            transaction.Postings,
            posting =>
                posting.Account
                    == LedgerAccountCode.OpeningEquity
                && posting.Credit == 5_000m);
    }

    [Fact]
    public async Task ReusingCareerOpeningIdentityWithDifferentCashIsRejected()
    {
        string databasePath =
            Path.Combine(
                _directory,
                "career.db");

        Guid careerId = Guid.NewGuid();
        DateTimeOffset createdAt =
            new(
                2026,
                9,
                18,
                12,
                0,
                0,
                TimeSpan.Zero);

        var service =
            new CareerEconomyBootstrapService(
                new SqliteEconomyLedgerStore(
                    databasePath));

        await service.InitializeAsync(
            careerId,
            5_000m,
            createdAt);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.InitializeAsync(
                    careerId,
                    10_000m,
                    createdAt));

        Assert.Equal(
            5_000m,
            (await new SqliteEconomyLedgerStore(databasePath)
                .ReadCashBalanceAsync()));
    }

    [Fact]
    public void OpeningBalanceRejectsFractionalCents()
    {
        Assert.Throws<ArgumentException>(
            () =>
                CareerOpeningBalanceEngine.Create(
                    Guid.NewGuid(),
                    1.001m,
                    DateTimeOffset.UtcNow));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(
                _directory,
                recursive: true);
        }
    }
}
