using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class EmployerCoveredOperatingCostQuoteSourceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CivilianEmploymentProducesZeroPlayerSettlementCosts()
    {
        JobContract contract =
            InProgressContract(
                ServiceTrack.CivilianEmployment,
                CompensationModel.PilotWage,
                coversFuel:
                    true,
                coversMaintenance:
                    true,
                coversAirportFees:
                    true);

        var source =
            new EmployerCoveredOperatingCostQuoteSource();

        CareerJobOperatingCostQuote quote =
            Assert.IsType<CareerJobOperatingCostQuote>(
                await source.QuoteAsync(
                    contract,
                    Basis(
                        contract.ContractId)));

        Assert.Equal(
            EmployerCoveredOperatingCostQuoteSource.AuthorityId,
            quote.PricingAuthorityId);
        Assert.Equal(
            new ContractSettlementCosts(
                0m,
                0m,
                0m,
                0m),
            quote.Costs);
        Assert.Equal(
            Epoch.AddMinutes(45),
            quote.PricedAt);
    }

    [Fact]
    public async Task OwnerPaidCompanyContractRemainsUnpriced()
    {
        JobContract contract =
            InProgressContract(
                ServiceTrack.CompanyContract,
                CompensationModel.CompanyRevenue,
                coversFuel:
                    false,
                coversMaintenance:
                    false,
                coversAirportFees:
                    false);

        var source =
            new EmployerCoveredOperatingCostQuoteSource();

        Assert.Null(
            await source.QuoteAsync(
                contract,
                Basis(
                    contract.ContractId)));
    }

    [Fact]
    public async Task PartialEmployerCoverageRemainsUnpriced()
    {
        JobContract contract =
            InProgressContract(
                ServiceTrack.CivilianEmployment,
                CompensationModel.PilotWage,
                coversFuel:
                    true,
                coversMaintenance:
                    true,
                coversAirportFees:
                    false);

        var source =
            new EmployerCoveredOperatingCostQuoteSource();

        Assert.Null(
            await source.QuoteAsync(
                contract,
                Basis(
                    contract.ContractId)));
    }

    [Fact]
    public async Task ZeroPlayerQuoteIsSettlementEquivalentToEmployerPaidExpenses()
    {
        JobContract inProgress =
            InProgressContract(
                ServiceTrack.CivilianEmployment,
                CompensationModel.PilotWage,
                coversFuel:
                    true,
                coversMaintenance:
                    true,
                coversAirportFees:
                    true);

        var source =
            new EmployerCoveredOperatingCostQuoteSource();

        CareerJobOperatingCostQuote quote =
            Assert.IsType<CareerJobOperatingCostQuote>(
                await source.QuoteAsync(
                    inProgress,
                    Basis(
                        inProgress.ContractId)));

        DateTimeOffset completedAt =
            Epoch.AddMinutes(50);

        JobContract completed =
            inProgress.Complete(
                completedAt,
                flightCompletionVerified:
                    true);

        ContractSettlementSummary zeroPlayerCosts =
            ContractSettlementEngine.Create(
                completed,
                quote.Costs,
                completedAt.AddMinutes(1));

        ContractSettlementSummary employerPaidPhysicalCosts =
            ContractSettlementEngine.Create(
                completed,
                new ContractSettlementCosts(
                    FuelCost:
                        450m,
                    MaintenanceReserveCost:
                        180m,
                    AirportFees:
                        75m),
                completedAt.AddMinutes(1));

        Assert.Equal(
            employerPaidPhysicalCosts.GrossCashReceipt,
            zeroPlayerCosts.GrossCashReceipt);
        Assert.Equal(
            employerPaidPhysicalCosts.PlayerOperatingCosts,
            zeroPlayerCosts.PlayerOperatingCosts);
        Assert.Equal(
            employerPaidPhysicalCosts.NetCashChange,
            zeroPlayerCosts.NetCashChange);
        Assert.Equal(
            0m,
            zeroPlayerCosts.PlayerOperatingCosts);
    }

    private static JobContract InProgressContract(
        ServiceTrack serviceTrack,
        CompensationModel compensationModel,
        bool coversFuel,
        bool coversMaintenance,
        bool coversAirportFees)
    {
        Guid contractId =
            Guid.NewGuid();

        decimal gross =
            compensationModel
                == CompensationModel.SalaryDuty
                    ? 0m
                    : 1_000m;

        decimal pilot =
            compensationModel
                == CompensationModel.CompanyRevenue
                    ? 0m
                    : 750m;

        var contract =
            new JobContract(
                ContractId:
                    contractId,
                EmployerId:
                    Guid.Parse(
                        "a5000000-0000-0000-0000-000000000001"),
                Kind:
                    ContractKind.Ferry,
                ServiceTrack:
                    serviceTrack,
                OriginIcao:
                    "KRME",
                DestinationIcao:
                    "KSYR",
                Compensation:
                    new ContractCompensation(
                        compensationModel,
                        gross,
                        pilot,
                        coversFuel,
                        coversMaintenance,
                        coversAirportFees),
                OfferedAt:
                    Epoch,
                MustStartBy:
                    null,
                MustCompleteBy:
                    Epoch.AddHours(2),
                AircraftRequirements:
                    new AircraftMissionRequirements(
                        AllowedAccess:
                            serviceTrack
                                == ServiceTrack.MilitaryService
                                    ? AircraftAccess.Military
                                    : AircraftAccess.Civilian,
                        MinimumSeats:
                            0),
                Status:
                    ContractStatus.InProgress,
                AcceptedAt:
                    Epoch.AddMinutes(1),
                StartedAt:
                    Epoch.AddMinutes(5));

        contract.Validate();
        return contract;
    }

    private static CareerJobOperatingCostBasis Basis(
        Guid contractId) =>
        new(
            contractId,
            Guid.Parse(
                "a5000000-0000-0000-0000-000000000099"),
            EvidenceAt:
                Epoch.AddMinutes(45),
            StartFuelPounds:
                1_000,
            LastFuelPounds:
                900,
            FuelBurnedPounds:
                100,
            FuelAddedPounds:
                0,
            DistanceNauticalMiles:
                120,
            BlockTime:
                TimeSpan.FromMinutes(50),
            AirborneTime:
                TimeSpan.FromMinutes(40),
            CareerCreditTime:
                TimeSpan.FromMinutes(40));
}
