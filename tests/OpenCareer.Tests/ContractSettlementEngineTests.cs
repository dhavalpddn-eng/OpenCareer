using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class ContractSettlementEngineTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EmployeeWageDoesNotChargeEmployerCoveredCosts()
    {
        JobContract contract =
            CompletedContract(
                new ContractCompensation(
                    CompensationModel.PilotWage,
                    GrossCustomerRevenue: 4_000m,
                    PilotCompensation: 1_250m,
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),
                ServiceTrack.CivilianEmployment);

        ContractSettlementSummary settlement =
            ContractSettlementEngine.Create(
                contract,
                new ContractSettlementCosts(
                    FuelCost: 500m,
                    MaintenanceReserveCost: 150m,
                    AirportFees: 75m),
                contract.CompletedAt!.Value.AddMinutes(1));

        Assert.Equal(
            1_250m,
            settlement.GrossCashReceipt);
        Assert.Equal(
            0m,
            settlement.PlayerOperatingCosts);
        Assert.Equal(
            1_250m,
            settlement.NetCashChange);
        Assert.Equal(
            1_250m,
            settlement.Transaction.CashChange);

        Assert.Contains(
            settlement.Transaction.Postings,
            posting =>
                posting.Account
                    == LedgerAccountCode.WageIncome
                && posting.Credit == 1_250m);

        Assert.DoesNotContain(
            settlement.Transaction.Postings,
            posting =>
                posting.Account
                    == LedgerAccountCode.FuelExpense);
    }

    [Fact]
    public void CompanyRevenueChargesPlayerOperatingCosts()
    {
        JobContract contract =
            CompletedContract(
                new ContractCompensation(
                    CompensationModel.CompanyRevenue,
                    GrossCustomerRevenue: 5_000m,
                    PilotCompensation: 0m,
                    EmployerCoversFuel: false,
                    EmployerCoversMaintenance: false,
                    EmployerCoversAirportFees: false),
                ServiceTrack.CompanyContract);

        ContractSettlementSummary settlement =
            ContractSettlementEngine.Create(
                contract,
                new ContractSettlementCosts(
                    FuelCost: 1_000m,
                    MaintenanceReserveCost: 300m,
                    AirportFees: 200m,
                    OtherOperatingCosts: 50m),
                contract.CompletedAt!.Value.AddMinutes(1));

        Assert.Equal(
            5_000m,
            settlement.GrossCashReceipt);
        Assert.Equal(
            1_550m,
            settlement.PlayerOperatingCosts);
        Assert.Equal(
            3_450m,
            settlement.NetCashChange);

        Assert.Equal(
            settlement.Transaction.TotalDebits,
            settlement.Transaction.TotalCredits);

        Assert.Contains(
            settlement.Transaction.Postings,
            posting =>
                posting.Account
                    == LedgerAccountCode.ContractRevenue
                && posting.Credit == 5_000m);

        Assert.Contains(
            settlement.Transaction.Postings,
            posting =>
                posting.Account
                    == LedgerAccountCode.FuelExpense
                && posting.Debit == 1_000m);
    }

    [Fact]
    public void MissionFeeUsesQuotedMissionRevenueWhenPresent()
    {
        JobContract contract =
            CompletedContract(
                new ContractCompensation(
                    CompensationModel.MissionFee,
                    GrossCustomerRevenue: 2_400m,
                    PilotCompensation: 900m,
                    EmployerCoversFuel: false,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),
                ServiceTrack.IndependentContract);

        ContractSettlementSummary settlement =
            ContractSettlementEngine.Create(
                contract,
                new ContractSettlementCosts(
                    FuelCost: 250m,
                    MaintenanceReserveCost: 100m,
                    AirportFees: 50m),
                contract.CompletedAt!.Value.AddMinutes(1));

        Assert.Equal(
            2_400m,
            settlement.GrossCashReceipt);
        Assert.Equal(
            250m,
            settlement.PlayerOperatingCosts);
        Assert.Equal(
            2_150m,
            settlement.NetCashChange);
    }

    [Fact]
    public void SettlementRequiresCompletedContract()
    {
        JobContract offered =
            BaseContract(
                new ContractCompensation(
                    CompensationModel.PilotWage,
                    GrossCustomerRevenue: 1_000m,
                    PilotCompensation: 500m,
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),
                ServiceTrack.CivilianEmployment);

        Assert.Throws<InvalidOperationException>(
            () => ContractSettlementEngine.Create(
                offered,
                new ContractSettlementCosts(
                    0m,
                    0m,
                    0m),
                OfferedAt.AddHours(2)));
    }

    [Fact]
    public void SettlementCannotPrecedeCompletion()
    {
        JobContract contract =
            CompletedContract(
                new ContractCompensation(
                    CompensationModel.PilotWage,
                    GrossCustomerRevenue: 1_000m,
                    PilotCompensation: 500m,
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),
                ServiceTrack.CivilianEmployment);

        Assert.Throws<ArgumentException>(
            () => ContractSettlementEngine.Create(
                contract,
                new ContractSettlementCosts(
                    0m,
                    0m,
                    0m),
                contract.CompletedAt!.Value.AddTicks(-1)));
    }

    [Fact]
    public void SettlementIdentityAndPostingsAreDeterministic()
    {
        JobContract contract =
            CompletedContract(
                new ContractCompensation(
                    CompensationModel.PilotWage,
                    GrossCustomerRevenue: 1_000m,
                    PilotCompensation: 500m,
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),
                ServiceTrack.CivilianEmployment);

        DateTimeOffset settledAt =
            contract.CompletedAt!.Value.AddMinutes(1);

        ContractSettlementSummary first =
            ContractSettlementEngine.Create(
                contract,
                new ContractSettlementCosts(
                    0m,
                    0m,
                    0m),
                settledAt);

        ContractSettlementSummary second =
            ContractSettlementEngine.Create(
                contract,
                new ContractSettlementCosts(
                    0m,
                    0m,
                    0m),
                settledAt);

        Assert.Equal(
            contract.ContractId,
            first.Transaction.TransactionId);
        Assert.Equal(
            first.Transaction.IdempotencyKey,
            second.Transaction.IdempotencyKey);
        Assert.Equal(
            first.Transaction.Postings,
            second.Transaction.Postings);
    }

    [Fact]
    public void ActualCostsMustUseWholeCents()
    {
        JobContract contract =
            CompletedContract(
                new ContractCompensation(
                    CompensationModel.PilotWage,
                    GrossCustomerRevenue: 1_000m,
                    PilotCompensation: 500m,
                    EmployerCoversFuel: false,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),
                ServiceTrack.CivilianEmployment);

        Assert.Throws<ArgumentException>(
            () => ContractSettlementEngine.Create(
                contract,
                new ContractSettlementCosts(
                    FuelCost: 1.001m,
                    MaintenanceReserveCost: 0m,
                    AirportFees: 0m),
                contract.CompletedAt!.Value.AddMinutes(1)));
    }

    private static JobContract CompletedContract(
        ContractCompensation compensation,
        ServiceTrack serviceTrack)
    {
        JobContract contract =
            BaseContract(
                compensation,
                serviceTrack);

        return contract with
        {
            Status = ContractStatus.Completed,
            AcceptedAt =
                OfferedAt.AddMinutes(5),
            StartedAt =
                OfferedAt.AddMinutes(15),
            CompletedAt =
                OfferedAt.AddHours(2)
        };
    }

    private static JobContract BaseContract(
        ContractCompensation compensation,
        ServiceTrack serviceTrack) =>
        new(
            ContractId:
                Guid.Parse(
                    "90000000-0000-0000-0000-000000000001"),
            EmployerId: null,
            Kind: ContractKind.Cargo,
            ServiceTrack: serviceTrack,
            OriginIcao: "KRME",
            DestinationIcao: "KSYR",
            Compensation: compensation,
            OfferedAt: OfferedAt,
            MustStartBy:
                OfferedAt.AddHours(1),
            MustCompleteBy:
                OfferedAt.AddHours(4),
            AircraftRequirements:
                new AircraftMissionRequirements(
                    RequiredCapabilities:
                        AircraftCapability.Cargo,
                    AllowedAccess:
                        AircraftAccess.Civilian,
                    MinimumSeats: 0));
}
