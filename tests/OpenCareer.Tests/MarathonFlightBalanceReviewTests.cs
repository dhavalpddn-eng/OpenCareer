using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class MarathonFlightBalanceReviewTests
{
    [Fact]
    public void FifteenHourOptionalMissionPaysLargeTotalWithoutPunishingHourlyRate()
    {
        ContractPayQuote shortMission =
            ContractPayQuoteEngine.Quote(
                new ContractPayQuoteRequest(
                    ServiceTrack.CivilianEmployment,
                    ContractKind.Ferry,
                    EstimatedFlightHours: 1,
                    DistanceNauticalMiles: 120,
                    PayloadPounds: 500,
                    DemandAttractiveness: 1,
                    Urgency: 0.15,
                    Difficulty: 0.15,
                    RelationshipStrength: 0.20));

        ContractPayQuote marathonMission =
            ContractPayQuoteEngine.Quote(
                new ContractPayQuoteRequest(
                    ServiceTrack.CivilianEmployment,
                    ContractKind.Ferry,
                    EstimatedFlightHours: 15,
                    DistanceNauticalMiles: 1_500,
                    PayloadPounds: 500,
                    DemandAttractiveness: 1,
                    Urgency: 0.15,
                    Difficulty: 0.15,
                    RelationshipStrength: 0.20));

        decimal shortHourly =
            shortMission.PilotCashCompensation;
        decimal marathonHourly =
            marathonMission.PilotCashCompensation / 15m;

        Assert.True(
            marathonMission.PilotCashCompensation
            >= shortMission.PilotCashCompensation * 14m,
            "A 15-hour optional mission should pay dramatically more total cash than a 1-hour mission.");

        Assert.True(
            marathonHourly >= shortHourly * 0.90m,
            "Long optional missions should not suffer a major hourly-pay penalty simply because they are long.");

        Assert.True(
            JobMarketPolicy.Default.DurationSuitability(
                ContractKind.Ferry,
                estimatedFlightHours: 15,
                careerLevel: 25) > 0);
    }
}
