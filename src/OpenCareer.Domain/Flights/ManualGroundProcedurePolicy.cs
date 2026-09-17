namespace OpenCareer.Domain.Flights;

public sealed record ManualGroundProcedurePolicy(decimal MaximumRewardPerFlight)
{
    public static ManualGroundProcedurePolicy Default { get; } = new(35m);

    public decimal CalculateReward(IEnumerable<GroundProcedureEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

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

        return Math.Min(reward, MaximumRewardPerFlight);
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
