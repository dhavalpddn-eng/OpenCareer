using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class ContractPayQuoteEngineTests
{
    [Fact]
    public void TimeDistanceAndPayloadEachIncreaseQuotedValue()
    {
        ContractPayQuoteRequest baseline =
            Request();

        decimal baseValue =
            ContractPayQuoteEngine
                .Quote(baseline)
                .PilotCashCompensation;

        Assert.True(
            ContractPayQuoteEngine
                .Quote(
                    baseline with
                    {
                        EstimatedFlightHours = 4
                    })
                .PilotCashCompensation
            > baseValue);

        Assert.True(
            ContractPayQuoteEngine
                .Quote(
                    baseline with
                    {
                        DistanceNauticalMiles = 700
                    })
                .PilotCashCompensation
            > baseValue);

        Assert.True(
            ContractPayQuoteEngine
                .Quote(
                    baseline with
                    {
                        PayloadPounds = 5_000
                    })
                .PilotCashCompensation
            > baseValue);
    }

    [Fact]
    public void DemandUrgencyDifficultyAndRelationshipIncreasePay()
    {
        ContractPayQuoteRequest baseline =
            Request();

        decimal baseValue =
            ContractPayQuoteEngine
                .Quote(baseline)
                .PilotCashCompensation;

        Assert.True(
            ContractPayQuoteEngine
                .Quote(
                    baseline with
                    {
                        DemandAttractiveness = 4
                    })
                .PilotCashCompensation
            > baseValue);

        Assert.True(
            ContractPayQuoteEngine
                .Quote(
                    baseline with
                    {
                        Urgency = 1
                    })
                .PilotCashCompensation
            > baseValue);

        Assert.True(
            ContractPayQuoteEngine
                .Quote(
                    baseline with
                    {
                        Difficulty = 1
                    })
                .PilotCashCompensation
            > baseValue);

        Assert.True(
            ContractPayQuoteEngine
                .Quote(
                    baseline with
                    {
                        RelationshipStrength = 1
                    })
                .PilotCashCompensation
            > baseValue);
    }

    [Fact]
    public void OwnerOperatorQuoteRecoversEstimatedOperatingCosts()
    {
        ContractPayQuoteRequest request =
            Request() with
            {
                ServiceTrack =
                    ServiceTrack.IndependentContract,
                EstimatedPlayerOperatingCosts = 1_000m
            };

        ContractPayQuote quote =
            ContractPayQuoteEngine.Quote(request);

        Assert.Equal(
            1_100m,
            quote.OperatingCostRecovery);
        Assert.False(
            quote.Compensation.EmployerCoversFuel);
        Assert.Equal(
            quote.Compensation.GrossCustomerRevenue,
            quote.Compensation.PilotCompensation);
    }

    [Fact]
    public void EmployeeWorkCoversAircraftOperatingCosts()
    {
        ContractPayQuote quote =
            ContractPayQuoteEngine.Quote(
                Request() with
                {
                    EstimatedPlayerOperatingCosts =
                        5_000m
                });

        Assert.Equal(
            0m,
            quote.OperatingCostRecovery);
        Assert.True(
            quote.Compensation.EmployerCoversFuel);
        Assert.True(
            quote.Compensation.EmployerCoversMaintenance);
        Assert.True(
            quote.Compensation.EmployerCoversAirportFees);
    }

    [Fact]
    public void StandardEarlyEmployeeJobTracksCurrentOwnershipProgressionTarget()
    {
        ContractPayQuote quote =
            ContractPayQuoteEngine.Quote(
                new ContractPayQuoteRequest(
                    ServiceTrack.CivilianEmployment,
                    ContractKind.Cargo,
                    EstimatedFlightHours: 3,
                    DistanceNauticalMiles: 400,
                    PayloadPounds: 500,
                    DemandAttractiveness: 1,
                    Urgency: 0.10,
                    Difficulty: 0.10,
                    RelationshipStrength: 0.20));

        decimal payPerFlightHour =
            quote.PilotCashCompensation / 3m;

        // Gameplay target from the existing 50-80 hour acquisition tuning.
        // This is not a statement about real pilot wages.
        Assert.InRange(
            payPerFlightHour,
            800m,
            1_100m);
    }

    [Fact]
    public void OneThreeAndSixHourEmployeeJobsHaveComparableHourlyPay()
    {
        var scenarios = new[]
        {
            (Hours: 1d, Distance: 130d),
            (Hours: 3d, Distance: 400d),
            (Hours: 6d, Distance: 800d)
        };

        decimal[] hourlyPay = scenarios
            .Select(scenario =>
            {
                ContractPayQuote quote =
                    ContractPayQuoteEngine.Quote(
                        new ContractPayQuoteRequest(
                            ServiceTrack.CivilianEmployment,
                            ContractKind.Cargo,
                            EstimatedFlightHours: scenario.Hours,
                            DistanceNauticalMiles: scenario.Distance,
                            PayloadPounds: 500,
                            DemandAttractiveness: 1,
                            Urgency: 0.10,
                            Difficulty: 0.10,
                            RelationshipStrength: 0.20));

                return quote.PilotCashCompensation / (decimal)scenario.Hours;
            })
            .ToArray();

        Assert.All(
            hourlyPay,
            value => Assert.InRange(value, 850m, 950m));
        Assert.True(
            hourlyPay.Max() / hourlyPay.Min() <= 1.02m,
            "Normal 1-6 hour employee jobs should not reward marathon sessions with materially better hourly pay.");
        Assert.False(CareerSessionPolicy.FitsJobDuration(TimeSpan.FromHours(15)));
    }

    [Fact]
    public void MilitaryDutyUsesSalaryModelAndDoesNotCreateCustomerRevenue()
    {
        ContractPayQuote quote =
            ContractPayQuoteEngine.Quote(
                Request() with
                {
                    ServiceTrack =
                        ServiceTrack.MilitaryService
                });

        Assert.Equal(
            CompensationModel.SalaryDuty,
            quote.Compensation.Model);
        Assert.Equal(
            0m,
            quote.Compensation.GrossCustomerRevenue);
        Assert.True(
            quote.Compensation.PilotCompensation > 0m);
        Assert.True(
            quote.Compensation.EmployerCoversFuel);
    }

    [Fact]
    public void MarketDemandMultiplierIsBounded()
    {
        ContractPayQuote low =
            ContractPayQuoteEngine.Quote(
                Request() with
                {
                    DemandAttractiveness = 0.25
                });

        ContractPayQuote high =
            ContractPayQuoteEngine.Quote(
                Request() with
                {
                    DemandAttractiveness = 4
                });

        Assert.InRange(
            low.DemandMultiplier,
            0.70,
            1.50);
        Assert.InRange(
            high.DemandMultiplier,
            0.70,
            1.50);
        Assert.True(
            high.DemandMultiplier
            > low.DemandMultiplier);
    }

    [Fact]
    public void InvalidQuoteInputsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContractPayQuoteEngine.Quote(
                Request() with
                {
                    EstimatedFlightHours = 0
                }));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContractPayQuoteEngine.Quote(
                Request() with
                {
                    DemandAttractiveness = 5
                }));

        Assert.Throws<ArgumentException>(
            () => ContractPayQuoteEngine.Quote(
                Request() with
                {
                    EstimatedPlayerOperatingCosts =
                        10.001m
                }));
    }

    private static ContractPayQuoteRequest Request() =>
        new(
            ServiceTrack.CivilianEmployment,
            ContractKind.Cargo,
            EstimatedFlightHours: 3,
            DistanceNauticalMiles: 400,
            PayloadPounds: 500,
            DemandAttractiveness: 1,
            Urgency: 0,
            Difficulty: 0,
            RelationshipStrength: 0,
            EstimatedPlayerOperatingCosts: 0m);
}
