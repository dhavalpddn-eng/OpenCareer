using OpenCareer.Application.Economy;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Finance;
using OpenCareer.Infrastructure.Economy;

namespace OpenCareer.Tests;

public sealed class AircraftPurchasePersistenceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Purchase.Tests",
            Guid.NewGuid().ToString("N"));

    private static readonly DateTimeOffset Day =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OpeningCashAndCashPurchasePersistAtomically()
    {
        string databasePath = Path.Combine(_directory, "career.db");
        var store = new SqliteEconomyLedgerStore(databasePath);

        var initializer =
            new CareerEconomyInitializationService(store);

        CareerEconomyInitializationResult opened =
            await initializer.EnsureOpeningBalanceAsync(
                Guid.NewGuid(),
                "career-1",
                70_000m,
                Day);

        Assert.True(opened.WasNewlyPosted);
        Assert.Equal(70_000m, opened.CashBalanceAfter);

        var service =
            new AircraftPurchaseService(store);

        (DealerOffer offer, DealerStock stock) =
            CreateOfferAndStock();

        AircraftPurchaseResult purchase =
            await service.PurchaseWithCashAsync(
                Guid.NewGuid(),
                "ownership-1",
                offer,
                stock,
                requiredOperatingReserve: 5_000m,
                purchasedAt: Day.AddHours(1));

        Assert.True(purchase.WasNewlyPosted);
        Assert.Equal(10_000m, purchase.CashBalanceAfter);

        AircraftPurchaseSettlement? persisted =
            await store.ReadAircraftPurchaseAsync("ownership-1");

        Assert.NotNull(persisted);
        Assert.Equal("listing-1", persisted.ListingId);
        Assert.Equal(60_000m, persisted.SalePrice);
        Assert.Null(persisted.Loan);

        var reopened =
            new SqliteEconomyLedgerStore(databasePath);
        Assert.Equal(10_000m, await reopened.ReadCashBalanceAsync());
        Assert.NotNull(await reopened.ReadAircraftPurchaseAsync("ownership-1"));
    }

    [Fact]
    public async Task SamePurchaseRetryDoesNotChargeCashTwice()
    {
        string databasePath = Path.Combine(_directory, "career.db");
        var store = new SqliteEconomyLedgerStore(databasePath);
        var initializer =
            new CareerEconomyInitializationService(store);

        await initializer.EnsureOpeningBalanceAsync(
            Guid.NewGuid(),
            "career-1",
            70_000m,
            Day);

        (DealerOffer offer, DealerStock stock) =
            CreateOfferAndStock();

        Guid purchaseId = Guid.NewGuid();
        var service = new AircraftPurchaseService(store);

        AircraftPurchaseResult first =
            await service.PurchaseWithCashAsync(
                purchaseId,
                "ownership-1",
                offer,
                stock,
                5_000m,
                Day.AddHours(1));

        AircraftPurchaseResult retry =
            await service.PurchaseWithCashAsync(
                purchaseId,
                "ownership-1",
                offer,
                stock,
                5_000m,
                Day.AddHours(1));

        Assert.True(first.WasNewlyPosted);
        Assert.False(retry.WasNewlyPosted);
        Assert.Equal(10_000m, retry.CashBalanceAfter);
    }

    [Fact]
    public async Task ConsumedListingCannotBeBoughtBySecondOwnership()
    {
        string databasePath = Path.Combine(_directory, "career.db");
        var store = new SqliteEconomyLedgerStore(databasePath);
        var initializer =
            new CareerEconomyInitializationService(store);

        await initializer.EnsureOpeningBalanceAsync(
            Guid.NewGuid(),
            "career-1",
            130_000m,
            Day);

        (DealerOffer offer, DealerStock stock) =
            CreateOfferAndStock();

        var service = new AircraftPurchaseService(store);
        await service.PurchaseWithCashAsync(
            Guid.NewGuid(),
            "ownership-1",
            offer,
            stock,
            5_000m,
            Day.AddHours(1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.PurchaseWithCashAsync(
                    Guid.NewGuid(),
                    "ownership-2",
                    offer,
                    stock,
                    5_000m,
                    Day.AddHours(1)));

        Assert.Equal(70_000m, await store.ReadCashBalanceAsync());
    }

    [Fact]
    public async Task FinancedPurchasePersistsImmutableLoanTerms()
    {
        string databasePath = Path.Combine(_directory, "career.db");
        var store = new SqliteEconomyLedgerStore(databasePath);
        var initializer =
            new CareerEconomyInitializationService(store);

        await initializer.EnsureOpeningBalanceAsync(
            Guid.NewGuid(),
            "career-1",
            20_000m,
            Day);

        (DealerOffer offer, DealerStock stock) =
            CreateOfferAndStock();

        var decision =
            new LoanDecision(
                Approved: true,
                Reason: LoanDeclineReason.None,
                CreditScore: 700,
                AnnualRate: 0.06m,
                MaximumAvailablePrincipal: 48_000m,
                RequestedPrincipal: 48_000m,
                MonthlyPayment: 532.90m);

        Guid loanId = Guid.NewGuid();
        var service = new AircraftPurchaseService(store);

        AircraftPurchaseResult result =
            await service.PurchaseWithFinancingAsync(
                Guid.NewGuid(),
                "ownership-financed",
                offer,
                stock,
                requiredOperatingReserve: 5_000m,
                purchasedAt: Day.AddHours(1),
                loanDecision: decision,
                loanId: loanId,
                lenderId: "community",
                loanTermCycles: 120);

        Assert.Equal(8_000m, result.CashBalanceAfter);

        AircraftPurchaseSettlement persisted =
            Assert.IsType<AircraftPurchaseSettlement>(
                await store.ReadAircraftPurchaseAsync(
                    "ownership-financed"));

        Assert.NotNull(persisted.Loan);
        Assert.Equal(loanId, persisted.Loan.LoanId);
        Assert.Equal(48_000m, persisted.Loan.OriginalPrincipal);
        Assert.Equal(0.06m, persisted.Loan.AnnualRate);
        Assert.Equal(120, persisted.Loan.TermCycles);
        Assert.Equal(532.90m, persisted.Loan.ScheduledPayment);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static (DealerOffer Offer, DealerStock Stock)
        CreateOfferAndStock()
    {
        var plane =
            new AircraftCapabilityProfile(
                "fixture-small",
                "Small test aircraft",
                AircraftCapability.Cargo,
                AircraftAccess.Civilian,
                1_000,
                500,
                120,
                4,
                1,
                true,
                false,
                false);

        var stock =
            new DealerStock(
                "listing-1",
                plane,
                AircraftCondition.Used,
                AskingPrice: 60_000m,
                AppraisedValue: 60_000m,
                ConditionPercent: 80m,
                CivilianSaleAuthorized: true);

        var offer =
            new DealerOffer(
                "dealer-1",
                stock.ListingId,
                plane.AircraftId,
                stock.Condition,
                ListPrice: 60_000m,
                DiscountRate: 0m,
                SalePrice: 60_000m,
                IssuedAt: Day,
                ExpiresAt: Day.AddDays(1));

        return (offer, stock);
    }
}
