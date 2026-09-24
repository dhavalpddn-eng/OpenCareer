using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public sealed class EmployerCoveredOperatingCostQuoteSource
    : ICareerJobOperatingCostQuoteSource
{
    public const string AuthorityId =
        "economy:employer-covered-operating-costs-v1";

    public string SourceId =>
        AuthorityId;

    public Task<CareerJobOperatingCostQuote?> QuoteAsync(
        JobContract contract,
        CareerJobOperatingCostBasis basis,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(basis);

        cancellationToken.ThrowIfCancellationRequested();

        contract.Validate();
        basis.Validate();

        if (basis.ContractId
                != contract.ContractId
            || contract.Status
                is not ContractStatus.InProgress
                    and not ContractStatus.Completed)
        {
            return Task.FromResult<CareerJobOperatingCostQuote?>(
                null);
        }

        ContractCompensation compensation =
            contract.Compensation;

        if (!IsEmployerCoveredTrack(
                contract.ServiceTrack,
                compensation.Model)
            || !compensation.EmployerCoversFuel
            || !compensation.EmployerCoversMaintenance
            || !compensation.EmployerCoversAirportFees)
        {
            return Task.FromResult<CareerJobOperatingCostQuote?>(
                null);
        }

        // The player has no settlement exposure to the three modeled
        // operating-cost categories on these employer/sponsor-paid tracks.
        // Physical employer expenses remain outside the player ledger.
        var costs =
            new ContractSettlementCosts(
                FuelCost:
                    0m,
                MaintenanceReserveCost:
                    0m,
                AirportFees:
                    0m,
                OtherOperatingCosts:
                    0m);

        return Task.FromResult<CareerJobOperatingCostQuote?>(
            new(
                contract.ContractId,
                basis.FlightSessionId,
                basis.EvidenceAt,
                AuthorityId,
                costs));
    }

    private static bool IsEmployerCoveredTrack(
        ServiceTrack serviceTrack,
        CompensationModel compensationModel) =>
        (serviceTrack, compensationModel) switch
        {
            (
                ServiceTrack.CivilianEmployment,
                CompensationModel.PilotWage
            ) => true,

            (
                ServiceTrack.GovernmentContract,
                CompensationModel.MissionFee
            ) => true,

            (
                ServiceTrack.MilitaryService,
                CompensationModel.SalaryDuty
            ) => true,

            _ => false
        };
}
