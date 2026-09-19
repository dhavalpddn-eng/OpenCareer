using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class ContractEconomicSnapshotTests
{
    private static readonly DateTimeOffset QuotedAt =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AcceptedContractPreservesQuotedEconomicsEvenWhenMarketLaterChanges()
    {
        var request =
            new ContractPayQuoteRequest(
                ServiceTrack.CivilianEmployment,
                ContractKind.Cargo,
                EstimatedFlightHours: 2,
                DistanceNauticalMiles: 250,
                PayloadPounds: 600,
                DemandAttractiveness: 1,
                Urgency: 0.1,
                Difficulty: 0.1,
                RelationshipStrength: 0.2);

        ContractPayQuote originalQuote =
            ContractPayQuoteEngine.Quote(request);

        ContractEconomicSnapshot snapshot =
            ContractEconomicSnapshot.FromQuote(
                request,
                originalQuote,
                QuotedAt);

        var contract =
            new JobContract(
                Guid.NewGuid(),
                EmployerId: Guid.NewGuid(),
                ContractKind.Cargo,
                ServiceTrack.CivilianEmployment,
                "KRME",
                "KSYR",
                originalQuote.Compensation,
                QuotedAt,
                MustStartBy: QuotedAt.AddHours(2),
                MustCompleteBy: QuotedAt.AddHours(5),
                AircraftRequirements:
                    new AircraftMissionRequirements(
                        RequiredCapabilities:
                            AircraftCapability.Cargo,
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumSeats: 0),
                EconomicSnapshot: snapshot);

        contract.Validate();

        ContractPayQuote laterQuote =
            ContractPayQuoteEngine.Quote(
                request with
                {
                    DemandAttractiveness = 4,
                    Urgency = 1
                });

        Assert.NotEqual(
            laterQuote.PilotCashCompensation,
            contract.Compensation.PilotCompensation);
        Assert.Equal(
            originalQuote.PilotCashCompensation,
            contract.EconomicSnapshot!.Compensation.PilotCompensation);
        Assert.Equal(
            originalQuote.CoreServiceValue,
            contract.EconomicSnapshot.CoreServiceValue);
    }

    [Fact]
    public void ContractRejectsCompensationThatDoesNotMatchEconomicSnapshot()
    {
        var request =
            new ContractPayQuoteRequest(
                ServiceTrack.CivilianEmployment,
                ContractKind.Cargo,
                2,
                250,
                600,
                1,
                0,
                0,
                0);

        ContractPayQuote quote =
            ContractPayQuoteEngine.Quote(request);
        ContractEconomicSnapshot snapshot =
            ContractEconomicSnapshot.FromQuote(
                request,
                quote,
                QuotedAt);

        var contract =
            new JobContract(
                Guid.NewGuid(),
                null,
                ContractKind.Cargo,
                ServiceTrack.CivilianEmployment,
                "KRME",
                "KSYR",
                quote.Compensation with
                {
                    PilotCompensation =
                        quote.Compensation.PilotCompensation + 1m
                },
                QuotedAt,
                QuotedAt.AddHours(2),
                QuotedAt.AddHours(5),
                new AircraftMissionRequirements(
                    RequiredCapabilities:
                        AircraftCapability.Cargo,
                    AllowedAccess:
                        AircraftAccess.Civilian,
                    MinimumSeats: 0),
                EconomicSnapshot: snapshot);

        Assert.Throws<ArgumentException>(
            contract.Validate);
    }
}
