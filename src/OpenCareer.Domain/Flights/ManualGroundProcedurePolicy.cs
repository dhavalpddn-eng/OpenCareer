namespace OpenCareer.Domain.Flights;

public sealed record ManualGroundProcedurePolicy(
    decimal MaximumRewardPerFlight,
    decimal MaximumRewardPerCareerCreditHour = 35m)
{
    public static ManualGroundProcedurePolicy Default { get; } = new(35m, 35m);

    // Legacy quote helper: per-flight cap only. Actual settlement must use the
    // career-credit-hour overload so short missions cannot farm fixed bonuses.
    public decimal CalculateReward(IEnumerable<GroundProcedureEvent> events) =>
        CalculateRewardInternal(events, MaximumRewardPerFlight);

    public decimal CalculateReward(
        IEnumerable<GroundProcedureEvent> events,
        decimal careerCreditHours)
    {
        if (careerCreditHours <= 0)
            throw new ArgumentOutOfRangeException(nameof(careerCreditHours));

        var timeScaledCap = Math.Min(
            MaximumRewardPerFlight,
            MaximumRewardPerCareerCreditHour * careerCreditHours);

        return CalculateRewardInternal(events, timeScaledCap);
    }

    private decimal CalculateRewardInternal(
        IEnumerable<GroundProcedureEvent> events,
        decimal cap)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (MaximumRewardPerFlight < 0 || MaximumRewardPerCareerCreditHour < 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumRewardPerFlight));

        decimal reward = 0;
        var rewarded = new HashSet<GroundProcedureKind>();
        foreach (var item in events)
        {
            item.Validate();
            if (item.Outcome != ProcedureOutcome.Completed || item.Source != ProcedureObservationSource.UserConfirmed ||
                item.Confidence < 0.8 || !rewarded.Add(item.Procedure))
                continue;

            reward += RewardFor(item.Procedure);
        }

        return Math.Min(reward, cap);
    }

    private static decimal RewardFor(GroundProcedureKind procedure) => procedure switch
    {
        GroundProcedureKind.PreflightInspection => 8m,
        GroundProcedureKind.Fueling => 5m,
        GroundProcedureKind.Deicing => 10m,
        GroundProcedureKind.CargoLoading => 8m,
        GroundProcedureKind.PassengerBoarding => 5m,
        GroundProcedureKind.Pushback => 4m,
        GroundProcedureKind.CargoUnloading => 6m,
        GroundProcedureKind.PassengerDeplaning => 4m,
        _ => 0m
    };
}
