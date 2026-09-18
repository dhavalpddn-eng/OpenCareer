namespace OpenCareer.Domain.Military;

public sealed record MilitaryMissionEvidence(
    DateTimeOffset Time,
    bool HasStableTelemetry,
    bool AtOrigin,
    bool Airborne,
    bool InObjectiveArea,
    bool ObjectiveActionVerified,
    bool AtRecoveryAirfield,
    bool ParkedAndSecured,
    bool AbortRequested = false,
    bool FailureDetected = false,
    double DeltaSeconds = 0)
{
    public void Validate(DateTimeOffset previousTime)
    {
        if (Time < previousTime)
            throw new ArgumentException("Military mission evidence cannot move backward in time.");

        if (!double.IsFinite(DeltaSeconds) || DeltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(DeltaSeconds));

        if (AtOrigin && Airborne)
            throw new ArgumentException("Evidence cannot be both at-origin and airborne.");

        if (ParkedAndSecured && Airborne)
            throw new ArgumentException("A parked aircraft cannot also be airborne.");
    }
}

public sealed record MilitaryMissionProgress(
    Guid OperationId,
    MilitaryOperationPhase Phase,
    double OnStationSeconds,
    bool ObjectiveActionVerified,
    DateTimeOffset UpdatedAt)
{
    public static MilitaryMissionProgress Briefed(
        MilitaryOperationPlan plan,
        DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();

        return new MilitaryMissionProgress(
            plan.OperationId,
            MilitaryOperationPhase.Briefed,
            0,
            false,
            time);
    }

    public bool IsTerminal =>
        Phase is MilitaryOperationPhase.Complete
            or MilitaryOperationPhase.Aborted
            or MilitaryOperationPhase.Failed;

    public MilitaryMissionProgress Accept(
        MilitaryOperationPlan plan,
        MilitaryOperationEligibility eligibility,
        DateTimeOffset time)
    {
        Validate(plan);

        if (Phase != MilitaryOperationPhase.Briefed)
            throw new InvalidOperationException($"Cannot accept a military operation in phase {Phase}.");

        if (!eligibility.IsEligible)
            throw new InvalidOperationException("Military operation eligibility must be verified before acceptance.");

        if (time < UpdatedAt)
            throw new ArgumentException("Acceptance cannot precede the last mission update.");

        return this with
        {
            Phase = MilitaryOperationPhase.Accepted,
            UpdatedAt = time
        };
    }

    public void Validate(MilitaryOperationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();

        if (OperationId != plan.OperationId)
            throw new ArgumentException("Mission progress does not belong to the supplied operation.");

        if (!Enum.IsDefined(Phase))
            throw new ArgumentOutOfRangeException(nameof(Phase));

        if (!double.IsFinite(OnStationSeconds) || OnStationSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(OnStationSeconds));
    }
}

public static class MilitaryMissionEngine
{
    public static MilitaryMissionProgress Advance(
        MilitaryOperationPlan plan,
        MilitaryMissionProgress progress,
        MilitaryMissionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(evidence);

        plan.Validate();
        progress.Validate(plan);
        evidence.Validate(progress.UpdatedAt);

        if (progress.IsTerminal)
            return progress;

        if (evidence.FailureDetected)
        {
            return progress with
            {
                Phase = MilitaryOperationPhase.Failed,
                UpdatedAt = evidence.Time
            };
        }

        if (evidence.AbortRequested)
        {
            return progress with
            {
                Phase = MilitaryOperationPhase.Aborted,
                UpdatedAt = evidence.Time
            };
        }

        var next = progress with { UpdatedAt = evidence.Time };

        switch (progress.Phase)
        {
            case MilitaryOperationPhase.Briefed:
                return next;

            case MilitaryOperationPhase.Accepted:
                if (evidence.HasStableTelemetry && evidence.AtOrigin && !evidence.Airborne)
                    next = next with { Phase = MilitaryOperationPhase.Preflight };
                return next;

            case MilitaryOperationPhase.Preflight:
                if (evidence.Airborne)
                    next = next with { Phase = MilitaryOperationPhase.EnRoute };
                return next;

            case MilitaryOperationPhase.EnRoute:
                if (evidence.InObjectiveArea && evidence.Airborne)
                {
                    var dwell = Math.Max(0, evidence.DeltaSeconds);
                    next = next with
                    {
                        Phase = MilitaryOperationPhase.OnStation,
                        OnStationSeconds = dwell,
                        ObjectiveActionVerified = evidence.ObjectiveActionVerified
                    };
                }
                return TryAdvanceOnStation(plan, next);

            case MilitaryOperationPhase.OnStation:
                if (evidence.InObjectiveArea && evidence.Airborne)
                {
                    next = next with
                    {
                        OnStationSeconds = progress.OnStationSeconds + evidence.DeltaSeconds,
                        ObjectiveActionVerified =
                            progress.ObjectiveActionVerified || evidence.ObjectiveActionVerified
                    };
                }

                return TryAdvanceOnStation(plan, next);

            case MilitaryOperationPhase.Objective:
                return next with { Phase = MilitaryOperationPhase.Egress };

            case MilitaryOperationPhase.Egress:
                if (evidence.AtRecoveryAirfield && !evidence.Airborne)
                    next = next with { Phase = MilitaryOperationPhase.Recovery };
                return next;

            case MilitaryOperationPhase.Recovery:
                if (evidence.ParkedAndSecured && !evidence.Airborne)
                    next = next with { Phase = MilitaryOperationPhase.Complete };
                return next;

            default:
                return next;
        }
    }

    private static MilitaryMissionProgress TryAdvanceOnStation(
        MilitaryOperationPlan plan,
        MilitaryMissionProgress progress)
    {
        var dwellSatisfied = progress.OnStationSeconds >= plan.MinimumOnStationSeconds;
        var actionSatisfied = !plan.RequiresObjectiveAction || progress.ObjectiveActionVerified;

        return dwellSatisfied && actionSatisfied
            ? progress with { Phase = MilitaryOperationPhase.Objective }
            : progress;
    }
}
