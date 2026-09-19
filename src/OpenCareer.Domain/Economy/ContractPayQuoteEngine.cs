using OpenCareer.Domain.Careers;

namespace OpenCareer.Domain.Economy;

public sealed record ContractPayPolicy(
    decimal TimeRatePerFlightHour,
    decimal DistanceRatePerNauticalMile,
    decimal PayloadRatePerPoundNauticalMile,
    double DemandSensitivity,
    double UrgencyPremium,
    double DifficultyPremium,
    double RelationshipPremium,
    decimal EmployeePilotShare,
    decimal MilitaryDutyShare,
    decimal OwnerOperatorCostRecoveryMarkup,
    double ExtendedDutyStartHours = 6,
    double ExtendedDutyFullPremiumHours = 12,
    double MaximumExtendedDutyPremium = 0.25)
{
    // Gameplay calibration, not a claim about real-world compensation.
    // The baseline is intentionally tuned around the current 50-80 flight-hour
    // first-aircraft progression target and remains data-driven for later playtesting.
    public static ContractPayPolicy Default { get; } = new(
        TimeRatePerFlightHour: 850m,
        DistanceRatePerNauticalMile: 0.65m,
        PayloadRatePerPoundNauticalMile: 0.0001m,
        DemandSensitivity: 0.25,
        UrgencyPremium: 0.35,
        DifficultyPremium: 0.20,
        RelationshipPremium: 0.08,
        EmployeePilotShare: 0.95m,
        MilitaryDutyShare: 0.80m,
        OwnerOperatorCostRecoveryMarkup: 0.10m,
        ExtendedDutyStartHours: 6,
        ExtendedDutyFullPremiumHours: 12,
        MaximumExtendedDutyPremium: 0.25);

    public void Validate()
    {
        if (TimeRatePerFlightHour < 0m
            || DistanceRatePerNauticalMile < 0m
            || PayloadRatePerPoundNauticalMile < 0m
            || EmployeePilotShare is < 0m or > 1m
            || MilitaryDutyShare is < 0m or > 1m
            || OwnerOperatorCostRecoveryMarkup is < 0m or > 2m
            || !double.IsFinite(ExtendedDutyStartHours)
            || ExtendedDutyStartHours < 0
            || !double.IsFinite(ExtendedDutyFullPremiumHours)
            || ExtendedDutyFullPremiumHours <= ExtendedDutyStartHours
            || !double.IsFinite(MaximumExtendedDutyPremium)
            || MaximumExtendedDutyPremium is < 0 or > 1
            || !double.IsFinite(DemandSensitivity)
            || DemandSensitivity is < 0 or > 2
            || !double.IsFinite(UrgencyPremium)
            || UrgencyPremium is < 0 or > 3
            || !double.IsFinite(DifficultyPremium)
            || DifficultyPremium is < 0 or > 3
            || !double.IsFinite(RelationshipPremium)
            || RelationshipPremium is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ContractPayPolicy));
        }
    }
}

public sealed record ContractPayQuoteRequest(
    ServiceTrack ServiceTrack,
    ContractKind Kind,
    double EstimatedFlightHours,
    double DistanceNauticalMiles,
    double PayloadPounds,
    double DemandAttractiveness,
    double Urgency,
    double Difficulty,
    double RelationshipStrength,
    decimal EstimatedPlayerOperatingCosts = 0m)
{
    public void Validate()
    {
        if (!Enum.IsDefined(ServiceTrack))
            throw new ArgumentOutOfRangeException(nameof(ServiceTrack));

        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind));

        if (!double.IsFinite(EstimatedFlightHours)
            || EstimatedFlightHours <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(EstimatedFlightHours));
        }

        if (!double.IsFinite(DistanceNauticalMiles)
            || DistanceNauticalMiles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DistanceNauticalMiles));
        }

        if (!double.IsFinite(PayloadPounds)
            || PayloadPounds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PayloadPounds));
        }

        if (!double.IsFinite(DemandAttractiveness)
            || DemandAttractiveness is < 0.25 or > 4.0)
        {
            throw new ArgumentOutOfRangeException(nameof(DemandAttractiveness));
        }

        ValidateUnit(Urgency, nameof(Urgency));
        ValidateUnit(Difficulty, nameof(Difficulty));
        ValidateUnit(RelationshipStrength, nameof(RelationshipStrength));

        Money.Validate(
            EstimatedPlayerOperatingCosts,
            nameof(EstimatedPlayerOperatingCosts));
    }

    private static void ValidateUnit(
        double value,
        string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record ContractPayQuote(
    decimal CoreServiceValue,
    double DemandMultiplier,
    double UrgencyMultiplier,
    double DifficultyMultiplier,
    double RelationshipMultiplier,
    double ExtendedDutyMultiplier,
    decimal OperatingCostRecovery,
    ContractCompensation Compensation)
{
    public decimal GrossQuotedValue =>
        Compensation.GrossCustomerRevenue;

    public decimal PilotCashCompensation =>
        Compensation.PilotCompensation;

    public void Validate()
    {
        Money.Validate(CoreServiceValue, nameof(CoreServiceValue));
        Money.Validate(OperatingCostRecovery, nameof(OperatingCostRecovery));

        ValidatePositiveMultiplier(DemandMultiplier, nameof(DemandMultiplier));
        ValidatePositiveMultiplier(UrgencyMultiplier, nameof(UrgencyMultiplier));
        ValidatePositiveMultiplier(DifficultyMultiplier, nameof(DifficultyMultiplier));
        ValidatePositiveMultiplier(RelationshipMultiplier, nameof(RelationshipMultiplier));
        ValidatePositiveMultiplier(ExtendedDutyMultiplier, nameof(ExtendedDutyMultiplier));

        ArgumentNullException.ThrowIfNull(Compensation);
    }

    private static void ValidatePositiveMultiplier(
        double value,
        string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name);
    }
}

public static class ContractPayQuoteEngine
{
    public static ContractPayQuote Quote(
        ContractPayQuoteRequest request,
        ContractPayPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        ContractPayPolicy effectivePolicy =
            policy ?? ContractPayPolicy.Default;

        request.Validate();
        effectivePolicy.Validate();

        decimal timeComponent =
            effectivePolicy.TimeRatePerFlightHour
            * (decimal)request.EstimatedFlightHours;

        decimal distanceComponent =
            effectivePolicy.DistanceRatePerNauticalMile
            * (decimal)request.DistanceNauticalMiles;

        decimal payloadDistanceComponent =
            effectivePolicy.PayloadRatePerPoundNauticalMile
            * (decimal)request.PayloadPounds
            * (decimal)request.DistanceNauticalMiles;

        decimal coreServiceValue =
            Money.Normalize(
                timeComponent
                + distanceComponent
                + payloadDistanceComponent);

        double demandMultiplier = Math.Clamp(
            1.0
            + effectivePolicy.DemandSensitivity
                * (Math.Sqrt(request.DemandAttractiveness) - 1.0),
            0.70,
            1.50);

        double urgencyMultiplier =
            1.0
            + effectivePolicy.UrgencyPremium
                * request.Urgency;

        double difficultyMultiplier =
            1.0
            + effectivePolicy.DifficultyPremium
                * request.Difficulty;

        double relationshipMultiplier =
            1.0
            + effectivePolicy.RelationshipPremium
                * request.RelationshipStrength;

        double extendedDutyProgress =
            Math.Clamp(
                (request.EstimatedFlightHours
                    - effectivePolicy.ExtendedDutyStartHours)
                / (effectivePolicy.ExtendedDutyFullPremiumHours
                    - effectivePolicy.ExtendedDutyStartHours),
                0.0,
                1.0);

        double extendedDutyMultiplier =
            1.0
            + effectivePolicy.MaximumExtendedDutyPremium
                * extendedDutyProgress;

        decimal adjustedServiceValue =
            Money.Normalize(
                coreServiceValue
                * (decimal)demandMultiplier
                * (decimal)urgencyMultiplier
                * (decimal)difficultyMultiplier
                * (decimal)relationshipMultiplier
                * (decimal)extendedDutyMultiplier);

        bool playerOwnsOperatingCosts =
            request.ServiceTrack is
                ServiceTrack.IndependentContract
                or ServiceTrack.CompanyContract;

        decimal operatingCostRecovery =
            playerOwnsOperatingCosts
                ? Money.Normalize(
                    request.EstimatedPlayerOperatingCosts
                    * (1m
                        + effectivePolicy
                            .OwnerOperatorCostRecoveryMarkup))
                : 0m;

        ContractCompensation compensation =
            CreateCompensation(
                request.ServiceTrack,
                adjustedServiceValue,
                operatingCostRecovery,
                effectivePolicy);

        var quote = new ContractPayQuote(
            coreServiceValue,
            demandMultiplier,
            urgencyMultiplier,
            difficultyMultiplier,
            relationshipMultiplier,
            extendedDutyMultiplier,
            operatingCostRecovery,
            compensation);

        quote.Validate();
        return quote;
    }

    private static ContractCompensation CreateCompensation(
        ServiceTrack serviceTrack,
        decimal adjustedServiceValue,
        decimal operatingCostRecovery,
        ContractPayPolicy policy)
    {
        return serviceTrack switch
        {
            ServiceTrack.CivilianEmployment =>
                new ContractCompensation(
                    CompensationModel.PilotWage,
                    GrossCustomerRevenue:
                        adjustedServiceValue,
                    PilotCompensation:
                        Money.Normalize(
                            adjustedServiceValue
                            * policy.EmployeePilotShare),
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),

            ServiceTrack.IndependentContract =>
                DirectMissionFee(
                    adjustedServiceValue,
                    operatingCostRecovery),

            ServiceTrack.CompanyContract =>
                new ContractCompensation(
                    CompensationModel.CompanyRevenue,
                    GrossCustomerRevenue:
                        Money.Normalize(
                            adjustedServiceValue
                            + operatingCostRecovery),
                    PilotCompensation: 0m,
                    EmployerCoversFuel: false,
                    EmployerCoversMaintenance: false,
                    EmployerCoversAirportFees: false),

            ServiceTrack.GovernmentContract =>
                new ContractCompensation(
                    CompensationModel.MissionFee,
                    GrossCustomerRevenue:
                        adjustedServiceValue,
                    PilotCompensation:
                        adjustedServiceValue,
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),

            ServiceTrack.MilitaryService =>
                new ContractCompensation(
                    CompensationModel.SalaryDuty,
                    GrossCustomerRevenue: 0m,
                    PilotCompensation:
                        Money.Normalize(
                            adjustedServiceValue
                            * policy.MilitaryDutyShare),
                    EmployerCoversFuel: true,
                    EmployerCoversMaintenance: true,
                    EmployerCoversAirportFees: true),

            _ => throw new ArgumentOutOfRangeException(
                nameof(serviceTrack))
        };
    }

    private static ContractCompensation DirectMissionFee(
        decimal adjustedServiceValue,
        decimal operatingCostRecovery)
    {
        decimal directFee =
            Money.Normalize(
                adjustedServiceValue
                + operatingCostRecovery);

        return new ContractCompensation(
            CompensationModel.MissionFee,
            GrossCustomerRevenue: directFee,
            PilotCompensation: directFee,
            EmployerCoversFuel: false,
            EmployerCoversMaintenance: false,
            EmployerCoversAirportFees: false);
    }
}
