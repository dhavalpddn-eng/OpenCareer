using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Finance;

namespace OpenCareer.Tests;

public sealed class EconomyBalanceScenarioTests
{
    private static readonly DateTimeOffset Day =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShortEmployeeFlightsCanReachFirstCashAircraftInsideTargetWindow()
    {
        decimal threeHourPay =
            QuoteEmployeeJob(
                hours: 3,
                distanceNm: 400);

        decimal oneHourPay =
            QuoteEmployeeJob(
                hours: 1,
                distanceNm: 130);

        // 22 x 3h plus one 1h job = 67 career-credit flight hours.
        // No marathon flight is required.
        decimal accumulatedCash =
            22m * threeHourPay
            + oneHourPay;

        Assert.Equal(67m, 22m * 3m + 1m);
        Assert.InRange(
            accumulatedCash,
            64_000m,
            80_000m);

        var aircraft =
            new AircraftCapabilityProfile(
                "first-owned-fixture",
                "First owned fixture",
                AircraftCapability.Cargo,
                AircraftAccess.Civilian,
                MaximumPayloadPounds: 1_000,
                MaximumRangeNauticalMiles: 600,
                TypicalCruiseKnots: 130,
                Seats: 4,
                EngineCount: 1,
                IfrCapable: true,
                Pressurized: false,
                RetractableGear: false);

        var stock =
            new DealerStock(
                "first-owned-listing",
                aircraft,
                AircraftCondition.Used,
                AskingPrice: 60_000m,
                AppraisedValue: 60_000m,
                ConditionPercent: 80m,
                CivilianSaleAuthorized: true);

        var dealer =
            new DealerProfile(
                "first-owned-dealer",
                "First Owned Aircraft Dealer",
                "KRME",
                SellsNew: false,
                SellsUsed: true,
                MaximumListingPrice: 250_000m,
                MarkupRate: 0m,
                PromotionChance: 0m,
                MaximumDiscountRate: 0m);

        CareerCreditHistory history =
            new(
                RealFlightHours: 67m,
                CompletedJobs: 23,
                FailedJobs: 0,
                OnTimePayments: 0,
                MissedPayments: 0,
                SafetyScore: 90m,
                EmployerTrust: 75m,
                VerifiedMonthlyNetIncome: 0m,
                ExistingMonthlyDebtPayments: 0m,
                AvailableCash: accumulatedCash,
                RequiredOperatingReserve: 4_000m,
                UnresolvedDefault: false);

        DealerOffer offer =
            Assert.Single(
                AircraftDealer.QuoteInventory(
                    dealer,
                    [stock],
                    history,
                    dealerRelationship: 30m,
                    careerSeed: 42,
                    Day));

        Assert.True(
            AircraftDealer.CanBuyWithCash(
                offer,
                stock,
                history,
                Day));

        Assert.True(
            accumulatedCash
                - offer.SalePrice
                >= history.RequiredOperatingReserve);
    }

    [Fact]
    public void FifteenHourSessionsDoNotCreateAnHourlyPayAdvantage()
    {
        decimal oneHour =
            QuoteEmployeeJob(
                hours: 1,
                distanceNm: 130);

        decimal threeHours =
            QuoteEmployeeJob(
                hours: 3,
                distanceNm: 400) / 3m;

        Assert.InRange(
            oneHour,
            threeHours - 5m,
            threeHours + 5m);

        Assert.False(
            CareerSessionPolicy.FitsJobDuration(
                TimeSpan.FromHours(15)));
    }

    private static decimal QuoteEmployeeJob(
        double hours,
        double distanceNm)
    {
        ContractPayQuote quote =
            ContractPayQuoteEngine.Quote(
                new ContractPayQuoteRequest(
                    ServiceTrack.CivilianEmployment,
                    ContractKind.Cargo,
                    EstimatedFlightHours: hours,
                    DistanceNauticalMiles: distanceNm,
                    PayloadPounds: 500,
                    DemandAttractiveness: 1,
                    Urgency: 0.10,
                    Difficulty: 0.10,
                    RelationshipStrength: 0.20));

        return quote.PilotCashCompensation;
    }
}
