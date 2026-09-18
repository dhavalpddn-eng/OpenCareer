using OpenCareer.Domain.Flights;

namespace OpenCareer.Domain.Careers;

public enum MissionFamily
{
    FerryAndReposition,
    CargoLogistics,
    PassengerService,
    MedicalResponse,
    SurveyAndRecon,
    SpecialtyAirwork,
    UtilityAndEmergency,
    GovernmentService,
    MilitaryService,
    Other
}

public sealed record JobRepeatExposure(
    int SameFamilyRecent,
    int SameRouteRecent,
    int SameMarketRecent)
{
    public static JobRepeatExposure None { get; } = new(0, 0, 0);

    public void Validate()
    {
        if (SameFamilyRecent < 0 || SameRouteRecent < 0 || SameMarketRecent < 0)
            throw new ArgumentOutOfRangeException(nameof(JobRepeatExposure));
    }
}

public static class JobRepeatExposureCalculator
{
    public const int DefaultRecentSettlementWindow = 8;

    public static JobRepeatExposure FromCompletedContracts(
        IEnumerable<JobContract> contracts,
        ContractKind candidateKind,
        string originIcao,
        string destinationIcao,
        string? marketId,
        int windowSize = DefaultRecentSettlementWindow)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentException.ThrowIfNullOrWhiteSpace(originIcao);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationIcao);
        if (!Enum.IsDefined(candidateKind) || windowSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowSize));

        var recent = contracts
            .Where(x => x.Status == ContractStatus.Completed && x.CompletedAt is not null)
            .OrderByDescending(x => x.CompletedAt)
            .ThenByDescending(x => x.ContractId)
            .Take(windowSize)
            .ToArray();

        var family = JobEconomyBalancePolicy.FamilyFor(candidateKind);
        var sameFamily = recent.Count(x =>
            JobEconomyBalancePolicy.FamilyFor(x.Kind) == family);
        var sameRoute = recent.Count(x =>
            string.Equals(x.OriginIcao, originIcao, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.DestinationIcao, destinationIcao, StringComparison.OrdinalIgnoreCase));
        var sameMarket = string.IsNullOrWhiteSpace(marketId)
            ? 0
            : recent.Count(x =>
                string.Equals(x.MarketId, marketId, StringComparison.Ordinal));

        return new JobRepeatExposure(sameFamily, sameRoute, sameMarket);
    }
}

public sealed record JobEconomyInput(
    ContractKind Kind,
    ServiceTrack ServiceTrack,
    double ExpectedCareerCreditHours,
    decimal EstimatedDirectOperatingCost,
    double DemandIndex,
    double Urgency,
    double Complexity,
    JobRepeatExposure RepeatExposure)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(RepeatExposure);
        RepeatExposure.Validate();

        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(ServiceTrack)
            || !double.IsFinite(ExpectedCareerCreditHours)
            || ExpectedCareerCreditHours <= 0
            || ExpectedCareerCreditHours > CareerSessionPolicy.MaximumOfferedDuration.TotalHours
            || EstimatedDirectOperatingCost < 0
            || !double.IsFinite(DemandIndex) || DemandIndex is < 0 or > 2
            || !double.IsFinite(Urgency) || Urgency is < 0 or > 1
            || !double.IsFinite(Complexity) || Complexity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(JobEconomyInput));
        }
    }
}

public sealed record JobEconomyQuote(
    MissionFamily Family,
    decimal TargetPlayerNet,
    decimal RecommendedGrossRevenue,
    decimal HourlyPlayerNet,
    double FreshHourlyFactor,
    double RepetitionFactor,
    double ReputationReward);

public sealed record JobEconomySettlementQuote(
    decimal PlayerNet,
    decimal RecommendedGrossRevenue,
    decimal DirectOperatingCostComponent,
    double TimeAccelerationFactor);

public sealed record JobEconomyBalancePolicy(
    decimal TargetNetPerCareerCreditHour,
    double MinimumFreshHourlyFactor,
    double MaximumFreshHourlyFactor,
    double MaximumRepetitionPenalty)
{
    public static JobEconomyBalancePolicy Default { get; } = new(
        CareerProgressionPolicy.Default.TargetNetSavingsPerFlightHour,
        MinimumFreshHourlyFactor: 0.85,
        MaximumFreshHourlyFactor: 1.20,
        MaximumRepetitionPenalty: 0.30);

    public JobEconomyQuote Quote(JobEconomyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        Validate();

        var family = FamilyFor(input.Kind);
        var intrinsicFactor = IntrinsicFactor(input.Kind);

        // Normal repeatable work should remain close to the same career-hour value.
        // Scarcity, urgency and complexity create useful variation without allowing one
        // family to permanently dominate the economy.
        var demandAdjustment = (input.DemandIndex - 1.0) * 0.10;
        var contextFactor = 1.0
            + demandAdjustment
            + input.Urgency * 0.06
            + input.Complexity * 0.06;

        var freshFactor = Math.Clamp(
            intrinsicFactor * contextFactor,
            MinimumFreshHourlyFactor,
            MaximumFreshHourlyFactor);

        var repetitionPenalty = Math.Min(
            MaximumRepetitionPenalty,
            0.04 * Math.Min(input.RepeatExposure.SameFamilyRecent, 5)
            + 0.02 * Math.Min(input.RepeatExposure.SameRouteRecent, 5)
            + 0.01 * Math.Min(input.RepeatExposure.SameMarketRecent, 5));

        var repetitionFactor = 1.0 - repetitionPenalty;
        var hours = (decimal)input.ExpectedCareerCreditHours;
        var targetPlayerNet = decimal.Round(
            TargetNetPerCareerCreditHour * hours * (decimal)(freshFactor * repetitionFactor),
            2,
            MidpointRounding.AwayFromZero);

        var gross = decimal.Round(
            targetPlayerNet + input.EstimatedDirectOperatingCost,
            2,
            MidpointRounding.AwayFromZero);

        var hourlyNet = decimal.Round(
            targetPlayerNet / hours,
            2,
            MidpointRounding.AwayFromZero);

        // Reputation scales with real career-credit time rather than one flat reward per job,
        // so short-hop farming does not accelerate standing.
        var reputationReward = Math.Min(
            2.0,
            input.ExpectedCareerCreditHours
            * (0.75 + 0.25 * input.Complexity)
            * repetitionFactor);

        return new JobEconomyQuote(
            family,
            targetPlayerNet,
            gross,
            hourlyNet,
            freshFactor,
            repetitionFactor,
            reputationReward);
    }

    public JobEconomySettlementQuote NormalizeForFlightTime(
        JobEconomyQuote quote,
        FlightTimeLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(ledger);

        if (quote.TargetPlayerNet < 0
            || quote.RecommendedGrossRevenue < quote.TargetPlayerNet
            || ledger.MovementFlightTime < TimeSpan.Zero
            || ledger.CareerCreditTime < TimeSpan.Zero)
        {
            throw new ArgumentException("Invalid economy quote or flight-time ledger.");
        }

        var movementSeconds = ledger.MovementFlightTime.TotalSeconds;
        var careerSeconds = ledger.CareerCreditTime.TotalSeconds;
        var timeFactor = movementSeconds <= 0
            ? 0d
            : Math.Clamp(careerSeconds / movementSeconds, 0d, 1d);

        var directOperatingCostComponent =
            quote.RecommendedGrossRevenue - quote.TargetPlayerNet;
        var playerNet = decimal.Round(
            quote.TargetPlayerNet * (decimal)timeFactor,
            2,
            MidpointRounding.AwayFromZero);

        return new JobEconomySettlementQuote(
            playerNet,
            directOperatingCostComponent + playerNet,
            directOperatingCostComponent,
            timeFactor);
    }

    public void Validate()
    {
        if (TargetNetPerCareerCreditHour <= 0
            || !double.IsFinite(MinimumFreshHourlyFactor)
            || !double.IsFinite(MaximumFreshHourlyFactor)
            || !double.IsFinite(MaximumRepetitionPenalty)
            || MinimumFreshHourlyFactor <= 0
            || MaximumFreshHourlyFactor < MinimumFreshHourlyFactor
            || MaximumRepetitionPenalty is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(JobEconomyBalancePolicy));
        }
    }

    public static MissionFamily FamilyFor(ContractKind kind) => kind switch
    {
        ContractKind.Ferry or ContractKind.Reposition => MissionFamily.FerryAndReposition,

        ContractKind.Cargo or ContractKind.ExpressCargo or ContractKind.AogPartsDelivery
            or ContractKind.DisasterRelief => MissionFamily.CargoLogistics,

        ContractKind.Passenger or ContractKind.Charter or ContractKind.Evacuation =>
            MissionFamily.PassengerService,

        ContractKind.Medical or ContractKind.Medevac =>
            MissionFamily.MedicalResponse,

        ContractKind.Survey or ContractKind.Photography or ContractKind.MilitarySurveillance =>
            MissionFamily.SurveyAndRecon,

        ContractKind.GliderTow or ContractKind.Skydiving or ContractKind.BannerTow =>
            MissionFamily.SpecialtyAirwork,

        ContractKind.Agricultural or ContractKind.Firefighting or ContractKind.SearchAndRescue =>
            MissionFamily.UtilityAndEmergency,

        ContractKind.GovernmentCourier => MissionFamily.GovernmentService,

        ContractKind.MilitaryTraining or ContractKind.MilitaryReadiness or ContractKind.MilitaryIntercept
            or ContractKind.MilitaryEscort or ContractKind.MilitaryPatrol or ContractKind.MilitaryTransport
            or ContractKind.MilitaryFerry or ContractKind.MilitaryTankerSupport =>
            MissionFamily.MilitaryService,

        _ => MissionFamily.Other
    };

    private static double IntrinsicFactor(ContractKind kind) => kind switch
    {
        ContractKind.Ferry or ContractKind.Reposition => 0.95,

        ContractKind.Cargo => 1.00,
        ContractKind.ExpressCargo => 1.04,
        ContractKind.AogPartsDelivery => 1.06,

        ContractKind.Passenger => 1.00,
        ContractKind.Charter => 1.04,

        ContractKind.Medical or ContractKind.Medevac => 1.08,

        ContractKind.Survey or ContractKind.Photography => 0.98,

        ContractKind.GliderTow or ContractKind.Skydiving or ContractKind.BannerTow => 1.00,

        ContractKind.Agricultural => 1.04,
        ContractKind.Firefighting or ContractKind.SearchAndRescue or ContractKind.DisasterRelief
            or ContractKind.Evacuation => 1.08,

        ContractKind.GovernmentCourier => 1.03,

        ContractKind.MilitaryTraining or ContractKind.MilitaryReadiness or ContractKind.MilitaryIntercept
            or ContractKind.MilitaryEscort or ContractKind.MilitaryPatrol or ContractKind.MilitaryTransport
            or ContractKind.MilitaryFerry or ContractKind.MilitaryTankerSupport
            or ContractKind.MilitarySurveillance => 1.03,

        _ => 1.00
    };
}
