using OpenCareer.Application.Economy;
using OpenCareer.Domain.Economy;
using OpenCareer.Infrastructure.Economy;

namespace OpenCareer.Tests;

public sealed class DeadheadTravelSettlementTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.DeadheadTravelSettlementTests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PlayerPaidDeadheadChargesCashExactlyOnce()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        await new CareerEconomyBootstrapService(store)
            .InitializeAsync(
                Guid.NewGuid(),
                openingCash: 2_000m,
                createdAt: Now.AddHours(-1));

        var quote =
            new DeadheadTravelQuote(
                Guid.NewGuid(),
                "KRME",
                "KJFK",
                DeadheadPayer.Player,
                Fare: 350m,
                QuotedAt: Now,
                ExpiresAt: Now.AddHours(2));

        var service =
            new DeadheadTravelSettlementService(store);

        DeadheadTravelSettlementResult first =
            await service.SettleAsync(
                quote,
                Now.AddMinutes(5));

        DeadheadTravelSettlementResult duplicate =
            await service.SettleAsync(
                quote,
                Now.AddMinutes(5));

        Assert.True(first.WasNewlyPosted);
        Assert.False(duplicate.WasNewlyPosted);
        Assert.Equal(1_650m, first.CashBalanceAfter);
        Assert.Equal(first.CashBalanceAfter, duplicate.CashBalanceAfter);
        Assert.Equal(350m, first.Settlement.PlayerCashCost);
        Assert.Contains(
            first.Settlement.Transaction.Postings,
            posting =>
                posting.Account == LedgerAccountCode.TravelExpense
                && posting.Debit == 350m);
    }

    [Fact]
    public async Task EmployerDutyDeadheadDoesNotChangePlayerCash()
    {
        string path =
            Path.Combine(_directory, "career.db");
        var store =
            new SqliteEconomyLedgerStore(path);

        await new CareerEconomyBootstrapService(store)
            .InitializeAsync(
                Guid.NewGuid(),
                openingCash: 2_000m,
                createdAt: Now.AddHours(-1));

        var quote =
            new DeadheadTravelQuote(
                Guid.NewGuid(),
                "KRME",
                "KORD",
                DeadheadPayer.Employer,
                Fare: 0m,
                QuotedAt: Now,
                ExpiresAt: Now.AddHours(2));

        DeadheadTravelSettlementResult result =
            await new DeadheadTravelSettlementService(store)
                .SettleAsync(
                    quote,
                    Now.AddMinutes(5));

        Assert.True(result.WasNewlyPosted);
        Assert.Equal(2_000m, result.CashBalanceAfter);
        Assert.Equal(0m, result.Settlement.PlayerCashCost);
        Assert.Empty(result.Settlement.Transaction.Postings);
    }

    [Fact]
    public void ExpiredDeadheadQuoteCannotSettle()
    {
        var quote =
            new DeadheadTravelQuote(
                Guid.NewGuid(),
                "KRME",
                "KJFK",
                DeadheadPayer.Player,
                Fare: 350m,
                QuotedAt: Now,
                ExpiresAt: Now.AddHours(1));

        Assert.Throws<InvalidOperationException>(
            () =>
                DeadheadTravelSettlementEngine.Create(
                    quote,
                    Now.AddHours(1)));
    }

    [Fact]
    public void EmployerPaidQuoteCannotHidePlayerFare()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new DeadheadTravelQuote(
                    Guid.NewGuid(),
                    "KRME",
                    "KJFK",
                    DeadheadPayer.Employer,
                    Fare: 1m,
                    QuotedAt: Now,
                    ExpiresAt: Now.AddHours(1))
                .Validate());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(
                _directory,
                recursive: true);
    }
}
