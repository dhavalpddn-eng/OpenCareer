using OpenCareer.Domain.Economy;

namespace OpenCareer.Domain.Careers;

public sealed record ContractEconomicSnapshot(
    DateTimeOffset QuotedAt,
    double EstimatedFlightHours,
    double DistanceNauticalMiles,
    double PayloadPounds,
    double DemandAttractiveness,
    double Urgency,
    double Difficulty,
    double RelationshipStrength,
    decimal EstimatedPlayerOperatingCosts,
    decimal CoreServiceValue,
    double DemandMultiplier,
    double UrgencyMultiplier,
    double DifficultyMultiplier,
    double RelationshipMultiplier,
    decimal OperatingCostRecovery,
    ContractCompensation Compensation)
{
    public static ContractEconomicSnapshot FromQuote(
        ContractPayQuoteRequest request,
        ContractPayQuote quote,
        DateTimeOffset quotedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(quote);

        request.Validate();
        quote.Validate();

        if (request.ServiceTrack != CompensationTrack(quote.Compensation))
        {
            throw new ArgumentException(
                "Pay quote compensation model does not match the requested service track.",
                nameof(quote));
        }

        var snapshot = new ContractEconomicSnapshot(
            quotedAt,
            request.EstimatedFlightHours,
            request.DistanceNauticalMiles,
            request.PayloadPounds,
            request.DemandAttractiveness,
            request.Urgency,
            request.Difficulty,
            request.RelationshipStrength,
            request.EstimatedPlayerOperatingCosts,
            quote.CoreServiceValue,
            quote.DemandMultiplier,
            quote.UrgencyMultiplier,
            quote.DifficultyMultiplier,
            quote.RelationshipMultiplier,
            quote.OperatingCostRecovery,
            quote.Compensation);

        snapshot.Validate();
        return snapshot;
    }

    public void Validate()
    {
        if (!double.IsFinite(EstimatedFlightHours)
            || EstimatedFlightHours <= 0
            || !double.IsFinite(DistanceNauticalMiles)
            || DistanceNauticalMiles < 0
            || !double.IsFinite(PayloadPounds)
            || PayloadPounds < 0
            || !double.IsFinite(DemandAttractiveness)
            || DemandAttractiveness is < 0.25 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(ContractEconomicSnapshot));
        }

        ValidateUnit(Urgency, nameof(Urgency));
        ValidateUnit(Difficulty, nameof(Difficulty));
        ValidateUnit(RelationshipStrength, nameof(RelationshipStrength));

        ValidateMultiplier(DemandMultiplier, nameof(DemandMultiplier));
        ValidateMultiplier(UrgencyMultiplier, nameof(UrgencyMultiplier));
        ValidateMultiplier(DifficultyMultiplier, nameof(DifficultyMultiplier));
        ValidateMultiplier(RelationshipMultiplier, nameof(RelationshipMultiplier));

        if (EstimatedPlayerOperatingCosts < 0m
            || CoreServiceValue < 0m
            || OperatingCostRecovery < 0m
            || decimal.Round(EstimatedPlayerOperatingCosts, 2, MidpointRounding.AwayFromZero)
                != EstimatedPlayerOperatingCosts
            || decimal.Round(CoreServiceValue, 2, MidpointRounding.AwayFromZero)
                != CoreServiceValue
            || decimal.Round(OperatingCostRecovery, 2, MidpointRounding.AwayFromZero)
                != OperatingCostRecovery)
        {
            throw new ArgumentOutOfRangeException(nameof(ContractEconomicSnapshot));
        }

        ArgumentNullException.ThrowIfNull(Compensation);

        if (!Enum.IsDefined(Compensation.Model)
            || Compensation.GrossCustomerRevenue < 0m
            || Compensation.PilotCompensation < 0m)
        {
            throw new ArgumentException("Quoted compensation is invalid.", nameof(Compensation));
        }
    }

    private static ServiceTrack CompensationTrack(
        ContractCompensation compensation) =>
        compensation.Model switch
        {
            CompensationModel.PilotWage => ServiceTrack.CivilianEmployment,
            CompensationModel.CompanyRevenue => ServiceTrack.CompanyContract,
            CompensationModel.SalaryDuty => ServiceTrack.MilitaryService,
            CompensationModel.MissionFee when
                compensation.EmployerCoversFuel
                && compensation.EmployerCoversMaintenance
                && compensation.EmployerCoversAirportFees =>
                ServiceTrack.GovernmentContract,
            CompensationModel.MissionFee =>
                ServiceTrack.IndependentContract,
            CompensationModel.Reimbursement =>
                ServiceTrack.CivilianEmployment,
            _ => throw new ArgumentOutOfRangeException(nameof(compensation))
        };

    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateMultiplier(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name);
    }
}
