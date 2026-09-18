using OpenCareer.Domain.Simulation;

namespace OpenCareer.Domain.Events;

public static class AirfieldControlEngine
{
    public static AirfieldControlState Create(
        string airportIcao,
        string regionId,
        DateTimeOffset time,
        double runwayServiceability = 1,
        double groundServicesCapacity = 1)
    {
        var state = new AirfieldControlState(
            AirfieldControlState.CurrentSchemaVersion,
            airportIcao,
            regionId,
            AirfieldOperationalStatus.Open,
            ControllingSideId: null,
            ControlBalance: 0,
            RunwayServiceability: runwayServiceability,
            GroundServicesCapacity: groundServicesCapacity,
            SecurityPressure: 0,
            UpdatedAt: StartOfUtcDay(time));

        state.Validate();
        return state;
    }

    public static AirfieldControlState AdvanceOneDay(
        AirfieldControlState state,
        AirfieldDailyContext context,
        ulong careerSeed,
        DateTimeOffset through,
        IReadOnlyList<AirfieldMissionContribution>? contributions = null,
        AirfieldControlPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        var p = policy ?? AirfieldControlPolicy.Default;
        p.Validate();
        state.Validate();
        context.Validate();

        var target = StartOfUtcDay(through);
        if (target != state.UpdatedAt.AddDays(1))
            throw new ArgumentOutOfRangeException(nameof(through), "Airfield control advances exactly one UTC day per call.");

        var daily = (contributions ?? Array.Empty<AirfieldMissionContribution>())
            .Where(c =>
                string.Equals(c.CampaignId, context.CampaignId, StringComparison.Ordinal)
                && string.Equals(c.AirportIcao, state.AirportIcao, StringComparison.Ordinal)
                && c.OccurredAt >= state.UpdatedAt
                && c.OccurredAt < target)
            .ToArray();

        foreach (var contribution in daily)
            contribution.Validate();

        var random = DeterministicSeed.CreateStream(
            careerSeed,
            $"airfield-control:{state.AirportIcao}:{target.UtcDateTime.Ticks}");

        var playerControl = ComputePlayerControlDelta(daily, context, p);
        var playerRepair = ComputePlayerRepair(daily, p);

        var backgroundControl = context.CampaignPhase == ConflictCampaignPhase.ActiveConflict
            ? context.BackgroundControlPressure * (0.015 + 0.035 * context.CampaignSeverity)
            : 0;

        var noise = context.CampaignPhase == ConflictCampaignPhase.ActiveConflict
            ? random.NextNormal(0, 0.012 + 0.018 * context.CampaignSeverity)
            : 0;

        var controlBalance = Math.Clamp(
            state.ControlBalance + backgroundControl + playerControl + noise,
            -1,
            1);

        var damage = context.CampaignPhase == ConflictCampaignPhase.ActiveConflict
            ? Math.Clamp(
                context.InfrastructureDamagePressure
                    * context.CampaignSeverity
                    * random.NextDouble(0.005, 0.035),
                0,
                0.05)
            : 0;

        var passiveRepair = context.CampaignPhase switch
        {
            ConflictCampaignPhase.ActiveConflict =>
                0.003 * context.CivilAviationResilience * (1 - 0.60 * context.CampaignSeverity),
            ConflictCampaignPhase.Ceasefire => 0.015,
            ConflictCampaignPhase.Recovery => 0.035,
            ConflictCampaignPhase.Resolved => 0.025,
            _ => 0
        };

        var runway = Math.Clamp(
            state.RunwayServiceability - damage + passiveRepair + playerRepair,
            0,
            1);

        var servicesDamage = damage * random.NextDouble(0.8, 1.4);
        var servicesRepair = context.CampaignPhase switch
        {
            ConflictCampaignPhase.ActiveConflict =>
                passiveRepair * 0.75,
            ConflictCampaignPhase.Ceasefire => 0.012,
            ConflictCampaignPhase.Recovery => 0.030,
            ConflictCampaignPhase.Resolved => 0.020,
            _ => 0
        };

        var services = Math.Clamp(
            state.GroundServicesCapacity - servicesDamage + servicesRepair + playerRepair * 0.75,
            0,
            1);

        var security = context.CampaignPhase switch
        {
            ConflictCampaignPhase.ActiveConflict =>
                Math.Clamp(0.45 + 0.50 * context.CampaignSeverity, 0, 1),
            ConflictCampaignPhase.Ceasefire =>
                Math.Clamp(0.35 + 0.25 * context.CampaignSeverity, 0, 1),
            ConflictCampaignPhase.Recovery =>
                Math.Clamp(0.20 + 0.20 * context.CampaignSeverity, 0, 1),
            _ => Math.Max(0, state.SecurityPressure - 0.05)
        };

        var status = DetermineStatus(
            context,
            controlBalance,
            runway,
            services,
            security,
            p);

        var controllingSide = status == AirfieldOperationalStatus.Secured
            ? controlBalance >= 0 ? context.SideAId : context.SideBId
            : null;

        var result = state with
        {
            Status = status,
            ControllingSideId = controllingSide,
            ControlBalance = controlBalance,
            RunwayServiceability = runway,
            GroundServicesCapacity = services,
            SecurityPressure = security,
            UpdatedAt = target
        };

        result.Validate();
        return result;
    }

    private static double ComputePlayerControlDelta(
        IReadOnlyList<AirfieldMissionContribution> contributions,
        AirfieldDailyContext context,
        AirfieldControlPolicy policy)
    {
        var raw = 0.0;

        foreach (var contribution in contributions)
        {
            if (contribution.Kind is AirfieldContributionKind.Medical
                or AirfieldContributionKind.Evacuation
                or AirfieldContributionKind.Humanitarian
                or AirfieldContributionKind.Engineering
                or AirfieldContributionKind.RecoverySupply
                or AirfieldContributionKind.Reconnaissance)
            {
                continue;
            }

            var sign = string.Equals(contribution.SupportedSideId, context.SideAId, StringComparison.Ordinal)
                ? 1
                : string.Equals(contribution.SupportedSideId, context.SideBId, StringComparison.Ordinal)
                    ? -1
                    : 0;

            var kindScale = contribution.Kind switch
            {
                AirfieldContributionKind.TroopLift => 1.00,
                AirfieldContributionKind.CargoLogistics => 0.65,
                _ => 0.35
            };

            raw += sign * kindScale * Math.Log10(1 + contribution.EffortPoints) / 250.0;
        }

        return Math.Clamp(
            raw,
            -policy.MaxPlayerControlShiftPerDay,
            policy.MaxPlayerControlShiftPerDay);
    }

    private static double ComputePlayerRepair(
        IReadOnlyList<AirfieldMissionContribution> contributions,
        AirfieldControlPolicy policy)
    {
        var points = contributions
            .Where(c => c.Kind is AirfieldContributionKind.Engineering
                or AirfieldContributionKind.RecoverySupply
                or AirfieldContributionKind.Humanitarian)
            .Sum(c => c.EffortPoints);

        if (points <= 0)
            return 0;

        return Math.Min(
            policy.MaxPlayerRepairPerDay,
            Math.Log10(1 + points) / 180.0);
    }

    private static AirfieldOperationalStatus DetermineStatus(
        AirfieldDailyContext context,
        double controlBalance,
        double runway,
        double services,
        double security,
        AirfieldControlPolicy policy)
    {
        if (runway < 0.10)
            return AirfieldOperationalStatus.Closed;

        if (context.CampaignPhase == ConflictCampaignPhase.ActiveConflict)
        {
            if (Math.Abs(controlBalance) < policy.ContestedControlThreshold)
                return AirfieldOperationalStatus.Contested;

            if (Math.Abs(controlBalance) >= policy.SecureControlThreshold
                && runway >= policy.MinimumRunwayForOperations)
            {
                return AirfieldOperationalStatus.Secured;
            }

            if (runway < policy.MinimumRunwayForOperations)
                return AirfieldOperationalStatus.Damaged;

            return AirfieldOperationalStatus.Restricted;
        }

        if (runway < policy.MinimumRunwayForOperations || services < 0.20)
            return AirfieldOperationalStatus.Damaged;

        if (context.CampaignPhase is ConflictCampaignPhase.Ceasefire
            or ConflictCampaignPhase.Recovery)
        {
            if (runway < 0.75 || services < 0.65)
                return AirfieldOperationalStatus.Reopening;

            return security >= 0.45
                ? AirfieldOperationalStatus.HeightenedSecurity
                : AirfieldOperationalStatus.Open;
        }

        return security >= 0.45
            ? AirfieldOperationalStatus.HeightenedSecurity
            : AirfieldOperationalStatus.Open;
    }

    private static DateTimeOffset StartOfUtcDay(DateTimeOffset value)
    {
        var day = value.UtcDateTime.Date;
        return new DateTimeOffset(day, TimeSpan.Zero);
    }
}
