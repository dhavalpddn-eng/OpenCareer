using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Finance;

namespace OpenCareer.Tests;

public sealed class EconomyAcquisitionTests
{
    private static readonly DateTimeOffset Day =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly AircraftCapabilityProfile Plane =
        new(
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

    private static readonly DealerStock Stock =
        new(
            "listing-1",
            Plane,
            AircraftCondition.Used,
            AskingPrice: 60_000m,
            AppraisedValue: 60_000m,
            ConditionPercent: 80m,
            CivilianSaleAuthorized: true);

    private static readonly DealerOffer Offer =
        new(
            "dealer-1",
            Stock.ListingId,
            Plane.AircraftId,
            Stock.Condition,
            ListPrice: 60_000m,
            DiscountRate: 0m,
            SalePrice: 60_000m,
            IssuedAt: Day,
            ExpiresAt: Day.AddDays(1));

    [Fact]
    public void OpeningBalanceIsBalancedAndCreditsCash()
    {
        Guid id = Guid.NewGuid();

        CareerOpeningBalanceSettlement settlement =
            CareerOpeningBalanceEngine.Create(
                id,
                "career-1",
                10_000m,
                Day);

        Assert.Equal(10_000m, settlement.Transaction.CashChange);
        Assert.Equal(
            settlement.Transaction.TotalDebits,
            settlement.Transaction.TotalCredits);
        Assert.Contains(
            settlement.Transaction.Postings,
            posting =>
                posting.Account == LedgerAccountCode.OpeningEquity
                && posting.Credit == 10_000m);
    }

    [Fact]
    public void CashAircraftPurchaseDebitsCashAndCreatesAsset()
    {
        Guid purchaseId = Guid.NewGuid();

        AircraftPurchaseSettlement settlement =
            AircraftPurchaseAccountingEngine.Create(
                new AircraftPurchaseAccountingRequest(
                    purchaseId,
                    "ownership-1",
                    Offer,
                    Stock,
                    AircraftPurchaseFunding.Cash,
                    AvailableCash: 70_000m,
                    RequiredOperatingReserve: 5_000m,
                    PurchasedAt: Day.AddHours(1)));

        Assert.Equal(60_000m, settlement.CashPaid);
        Assert.Equal(0m, settlement.FinancedPrincipal);
        Assert.Null(settlement.Loan);
        Assert.Equal(-60_000m, settlement.Transaction.CashChange);
        Assert.Contains(
            settlement.Transaction.Postings,
            posting =>
                posting.Account == LedgerAccountCode.AircraftAsset
                && posting.Debit == 60_000m);
    }

    [Fact]
    public void FinancedAircraftPurchaseCreatesLoanAndOnlyConsumesDepositCash()
    {
        var loanDecision =
            new LoanDecision(
                Approved: true,
                Reason: LoanDeclineReason.None,
                CreditScore: 700,
                AnnualRate: 0.06m,
                MaximumAvailablePrincipal: 48_000m,
                RequestedPrincipal: 48_000m,
                MonthlyPayment: 532.90m);

        AircraftPurchaseSettlement settlement =
            AircraftPurchaseAccountingEngine.Create(
                new AircraftPurchaseAccountingRequest(
                    Guid.NewGuid(),
                    "ownership-2",
                    Offer,
                    Stock,
                    AircraftPurchaseFunding.Financed,
                    AvailableCash: 20_000m,
                    RequiredOperatingReserve: 5_000m,
                    PurchasedAt: Day.AddHours(1),
                    LoanDecision: loanDecision,
                    LoanId: Guid.NewGuid(),
                    LenderId: "community",
                    LoanTermCycles: 120));

        Assert.Equal(12_000m, settlement.CashPaid);
        Assert.Equal(48_000m, settlement.FinancedPrincipal);
        Assert.NotNull(settlement.Loan);
        Assert.Equal(-12_000m, settlement.Transaction.CashChange);
        Assert.Contains(
            settlement.Transaction.Postings,
            posting =>
                posting.Account == LedgerAccountCode.LoanPayable
                && posting.Credit == 48_000m);
    }

    [Fact]
    public void PurchaseCannotBreakOperatingReserve()
    {
        Assert.Throws<InvalidOperationException>(
            () => AircraftPurchaseAccountingEngine.Create(
                new AircraftPurchaseAccountingRequest(
                    Guid.NewGuid(),
                    "ownership-1",
                    Offer,
                    Stock,
                    AircraftPurchaseFunding.Cash,
                    AvailableCash: 62_000m,
                    RequiredOperatingReserve: 5_000m,
                    PurchasedAt: Day.AddHours(1))));
    }

    [Fact]
    public void LoanAmortizationReconcilesPrincipalAndFinalBalance()
    {
        var agreement =
            new AircraftLoanAgreement(
                Guid.NewGuid(),
                "ownership-1",
                "community",
                OriginalPrincipal: 80_000m,
                AnnualRate: 0.06m,
                TermCycles: 120,
                ScheduledPayment: 888.17m,
                OriginatedAt: Day);
        agreement.Validate();

        AircraftLoanCyclePayment first =
            AircraftLoanAmortization.QuoteCycle(
                agreement,
                0);

        Assert.Equal(400m, first.Interest);
        Assert.Equal(488.17m, first.Principal);
        Assert.Equal(79_511.83m, first.BalanceAfter);

        decimal principalPaid = 0m;
        decimal interestPaid = 0m;
        AircraftLoanCyclePayment last = first;

        for (int index = 0; index < agreement.TermCycles; index++)
        {
            last = AircraftLoanAmortization.QuoteCycle(
                agreement,
                index);
            principalPaid += last.Principal;
            interestPaid += last.Interest;
        }

        Assert.Equal(80_000m, principalPaid);
        Assert.True(interestPaid > 0m);
        Assert.Equal(0m, last.BalanceAfter);
        Assert.True(last.IsFinalPayment);
    }

    [Fact]
    public void OwnershipScheduleFeedsExactLoanPrincipalInterestAndFixedCosts()
    {
        var agreement =
            new AircraftLoanAgreement(
                Guid.NewGuid(),
                "ownership-1",
                "community",
                OriginalPrincipal: 10_000m,
                AnnualRate: 0.06m,
                TermCycles: 12,
                ScheduledPayment: 860.67m,
                OriginatedAt: Day);

        IReadOnlyList<RecurringOwnershipCostCycle> schedule =
            OwnershipCostScheduleBuilder.Build(
                agreement,
                new OwnershipFixedCostProfile(
                    InsurancePerCycle: 200m,
                    StoragePerCycle: 300m),
                totalCycles: 18);

        Assert.Equal(18, schedule.Count);
        Assert.Equal(
            AircraftLoanAmortization.QuoteCycle(agreement, 0).Principal,
            schedule[0].LoanPrincipal);
        Assert.Equal(
            AircraftLoanAmortization.QuoteCycle(agreement, 0).Interest,
            schedule[0].LoanInterest);
        Assert.Equal(200m, schedule[0].Insurance);
        Assert.Equal(300m, schedule[0].Storage);
        Assert.Equal(0m, schedule[12].LoanPrincipal);
        Assert.Equal(0m, schedule[12].LoanInterest);
    }
}
