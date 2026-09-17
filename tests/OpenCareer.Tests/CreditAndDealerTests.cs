using System.Text.Json;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Finance;

namespace OpenCareer.Tests;

public class CreditAndDealerTests
{
    private static readonly DateTimeOffset Day = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
    private static CareerCreditHistory Good => new(70, 30, 0, 12, 0, 95, 90, 10000, 100, 100000, 4000, false);
    private static AircraftCapabilityProfile Plane => new("fixture-small", "Small test aircraft", AircraftCapability.Cargo,
        AircraftAccess.Civilian, 1000, 500, 120, 4, 1, true, false, false);
    private static DealerStock Used => new("serial-1", Plane, AircraftCondition.Used, 60000, 55000, 80, true);
    private static AircraftLoanRequest Request => new(100000, 100000, 20000, 120, true);

    [Fact]
    public void BetterCareerImprovesScoreAndRate()
    {
        var weaker = Good with { RealFlightHours = 10, CompletedJobs = 5, FailedJobs = 5, OnTimePayments = 0, SafetyScore = 70, EmployerTrust = 50 };
        var good = CareerCredit.Evaluate(Good, InitialLenders.Specialist, Request with { Deposit = 40000 });
        var weak = CareerCredit.Evaluate(weaker, InitialLenders.Specialist, Request with { Deposit = 40000 });
        Assert.True(good.CreditScore > weak.CreditScore);
        Assert.True(good.AnnualRate < weak.AnnualRate);
    }

    [Fact]
    public void AmortizationMatchesIndependentWolframCalculation()
    {
        var lender = InitialLenders.Community with { BaseAnnualRate = .06m, MaximumRiskPremium = 0 };
        var decision = CareerCredit.Evaluate(Good, lender, Request);
        Assert.True(decision.Approved);
        Assert.Equal(888.17m, decision.MonthlyPayment); // Unrounded reference: 888.1640155332152.
        Assert.Equal(80000m, decision.MaximumAvailablePrincipal);
        Assert.Equal(666.67m, CareerCredit.Evaluate(Good, lender with { BaseAnnualRate = 0 }, Request).MonthlyPayment);
    }

    [Theory]
    [InlineData(0)] [InlineData(100)]
    public void ScoreCannotSubstituteForIncome(int income)
    {
        var result = CareerCredit.Evaluate(Good with { VerifiedMonthlyNetIncome = income }, InitialLenders.Community, Request);
        Assert.False(result.Approved);
        Assert.Equal(LoanDeclineReason.NoAffordableCapacity, result.Reason);
    }

    [Fact]
    public void ReservesDefaultsCollateralAndMilitaryRestrictionsAreEnforced()
    {
        Assert.Equal(LoanDeclineReason.InsufficientDeposit, CareerCredit.Evaluate(Good with { AvailableCash = 21000 }, InitialLenders.Community, Request).Reason);
        Assert.Equal(LoanDeclineReason.UnresolvedDefault, CareerCredit.Evaluate(Good with { UnresolvedDefault = true }, InitialLenders.Community, Request).Reason);
        Assert.False(CareerCredit.Evaluate(Good, InitialLenders.Community, Request with { AppraisedValue = 50000 }).Approved);
        var restricted = CareerCredit.Evaluate(Good, InitialLenders.Community, Request with { CivilianOwnershipEligible = false });
        Assert.Equal(LoanDeclineReason.RestrictedAsset, restricted.Reason);
        Assert.Equal(0m, restricted.MaximumAvailablePrincipal);
        Assert.False(CareerCredit.Evaluate(Good with { ExistingMonthlyDebtPayments = 9999 }, InitialLenders.Community, Request).Approved);
    }

    [Fact]
    public void DifferentLendersOfferDifferentTermsAndRateShockRaisesPayments()
    {
        var a = CareerCredit.Evaluate(Good, InitialLenders.Community, Request with { Deposit = 40000 });
        var b = CareerCredit.Evaluate(Good, InitialLenders.Specialist, Request with { Deposit = 40000 });
        Assert.NotEqual(a.AnnualRate, b.AnnualRate);
        var shock = CareerCredit.Evaluate(Good, InitialLenders.Community, Request, .05m);
        Assert.True(shock.MonthlyPayment > a.MonthlyPayment);
    }

    [Fact]
    public void InvalidInputsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (Good with { SafetyScore = -1 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => CareerCredit.Evaluate(Good, InitialLenders.Community, Request with { TermMonths = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => CareerCredit.Evaluate(Good, InitialLenders.Community, Request with { Deposit = 100000 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => CareerCredit.Evaluate(Good, InitialLenders.Community with { MaximumLoanToValue = 2 }, Request));
    }

    [Fact]
    public void FiftyToEightyHoursSupportsBothOwnershipPaths()
    {
        CareerProgressionPolicy.Default.Validate();
        CareerProgressionPolicy.FinancedLargerAircraft.Validate();
        Assert.Equal(1m, CareerProgressionPolicy.Default.MinimumDownPaymentRate);
        Assert.Equal(64, CareerProgressionPolicy.Default.ExpectedHoursToAcquisition);
        Assert.Equal(64, CareerProgressionPolicy.FinancedLargerAircraft.ExpectedHoursToAcquisition);
    }

    [Fact]
    public void DealersHaveDistinctStockAndExcludeF22FromCivilianSales()
    {
        var f22 = Used with { ListingId = "f22", Aircraft = Plane with { AircraftId = "F-22", Access = AircraftAccess.Military,
            Capabilities = AircraftCapability.Military | AircraftCapability.Fighter }, CivilianSaleAuthorized = false };
        var stock = new[] { Used, Used with { ListingId = "new", Condition = AircraftCondition.New, ConditionPercent = 100 }, f22 };
        Assert.Single(AircraftDealer.QuoteInventory(InitialDealers.UsedLocal, stock, Good, 0, 42, Day));
        Assert.Single(AircraftDealer.QuoteInventory(InitialDealers.Factory, stock, Good, 0, 42, Day));
        Assert.Equal(2, AircraftDealer.QuoteInventory(InitialDealers.FleetBroker, stock, Good, 0, 42, Day).Length);
    }

    [Fact]
    public void DiscountsDoNotRerollOnRefreshReorderingOrReload()
    {
        var stock = new[] { Used, Used with { ListingId = "serial-2" } };
        var a = AircraftDealer.QuoteInventory(InitialDealers.UsedLocal, stock, Good, 80, 42, Day);
        var restored = JsonSerializer.Deserialize<CareerCreditHistory>(JsonSerializer.Serialize(Good))!;
        var b = AircraftDealer.QuoteInventory(InitialDealers.UsedLocal, stock.Reverse(), restored, 80, 42, Day.AddHours(1));
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(JsonSerializer.Deserialize<DealerOffer[]>(JsonSerializer.Serialize(a))));
    }

    [Fact]
    public void StrongStandingGetsAtLeastAsGoodDiscountForSameRandomDraw()
    {
        var dealer = InitialDealers.FleetBroker;
        var weak = Good with { CompletedJobs = 0, RealFlightHours = 0, OnTimePayments = 0, SafetyScore = 40, EmployerTrust = 40 };
        var promotions = 0;
        for (ulong seed = 0; seed < 200; seed++)
        {
            var a = AircraftDealer.QuoteInventory(dealer, [Used], Good, 100, seed, Day)[0];
            var b = AircraftDealer.QuoteInventory(dealer, [Used], weak, 0, seed, Day)[0];
            Assert.True(a.DiscountRate >= b.DiscountRate);
            Assert.InRange(a.DiscountRate, 0m, dealer.MaximumDiscountRate);
            Assert.InRange(a.SalePrice, .75m * a.ListPrice, a.ListPrice);
            if (a.DiscountRate > 0) promotions++;
        }
        Assert.InRange(promotions, 1, 199);
    }

    [Fact]
    public void DiscountedOfferFeedsLoanCalculationAndExpires()
    {
        var offer = AircraftDealer.QuoteInventory(InitialDealers.UsedLocal, [Used], Good, 100, 42, Day)[0];
        var loan = AircraftDealer.QuoteFinancing(offer, Used, Good, InitialLenders.Community, 20000, 120, Day);
        Assert.Equal(offer.SalePrice - 20000, loan.RequestedPrincipal);
        Assert.Throws<InvalidOperationException>(() => AircraftDealer.QuoteFinancing(offer, Used, Good, InitialLenders.Community, 20000, 120, offer.ExpiresAt));
        Assert.Throws<InvalidOperationException>(() => AircraftDealer.QuoteFinancing(offer, Used with { ListingId = "sold" }, Good, InitialLenders.Community, 20000, 120, Day));
    }
    [Fact]
    public void CashPurchaseKeepsReserveWithoutRequiringCreditApproval()
    {
        var offer = AircraftDealer.QuoteInventory(InitialDealers.UsedLocal, [Used], Good, 0, 42, Day)[0];
        Assert.True(AircraftDealer.CanBuyWithCash(offer, Used, Good with { UnresolvedDefault = true }, Day));
        Assert.False(AircraftDealer.CanBuyWithCash(offer, Used, Good with { AvailableCash = offer.SalePrice }, Day));
        Assert.Throws<InvalidOperationException>(() => AircraftDealer.CanBuyWithCash(offer, Used, Good, offer.ExpiresAt));
    }

    [Fact]
    public void NearZeroRateRemainsFiniteAndBadHistoryCannotOverflowScore()
    {
        var result = CareerCredit.Evaluate(Good, InitialLenders.Community with { BaseAnnualRate = .000000000000000000000000001m,
            MaximumRiskPremium = 0 }, Request);
        Assert.Equal(666.67m, result.MonthlyPayment);
        Assert.InRange((Good with { MissedPayments = int.MaxValue, CompletedJobs = int.MaxValue, FailedJobs = int.MaxValue }).CreditScore, 300, 850);
    }
    [Fact]
    public void PurchaseQuotesRejectInconsistentPriceAndInvalidCurrentStock()
    {
        var offer = AircraftDealer.QuoteInventory(InitialDealers.UsedLocal, [Used], Good, 0, 42, Day)[0];
        Assert.Throws<ArgumentException>(() => AircraftDealer.CanBuyWithCash(offer with { SalePrice = 1m }, Used, Good, Day));
        Assert.Throws<ArgumentException>(() => AircraftDealer.CanBuyWithCash(offer, Used with { ConditionPercent = 0 }, Good, Day));
        Assert.Throws<ArgumentException>(() => AircraftDealer.QuoteFinancing(offer, Used with { AppraisedValue = -1 }, Good, InitialLenders.Community, 20000, 120, Day));
    }
}
