namespace OpenCareer.Domain.Conflict;

public sealed record ConflictCampaignCycleResult(
    ConflictWorldState World,
    double FriendlyReplacementReserve,
    double HostileReplacementReserve);

public static class ConflictCampaignCycleEngine
{
    private const double LogisticsSupportRadiusNauticalMiles = 45;
    private const double GroundReadinessRecoveryPerHour = 0.008;
    private const double GroundStrengthRecoveryPerHour = 0.002;
    private const double AirReadinessRecoveryPerHour = 0.004;
    private const double MomentumControlShiftPerHour = 0.003;
    private const double MaximumStateValue = 0.98;
    private const double MaximumControlShiftPerCycle = 0.025;
    private const double ReplacementStrengthPerHour = 0.0015;
    private const double ReplacementStrengthCeiling = 0.90;

    public static ConflictWorldState Apply(
        ConflictWorldState world,
        ConflictCampaignState campaign) =>
        ApplyCycle(world, campaign).World;

    public static ConflictCampaignCycleResult ApplyCycle(
        ConflictWorldState world,
        ConflictCampaignState campaign)
    {
        ConflictValidation.Validate(world);
        ArgumentNullException.ThrowIfNull(campaign);
        campaign.Validate();

        if (!string.Equals(
            world.TheaterId,
            campaign.TheaterId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Campaign state belongs to another conflict theater.");
        }

        if (world.UpdatedAt < campaign.UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(world),
                "Conflict world time cannot precede campaign time.");
        }

        TimeSpan elapsed = world.UpdatedAt - campaign.UpdatedAt;
        if (elapsed == TimeSpan.Zero || campaign.IsTerminal)
        {
            return new ConflictCampaignCycleResult(
                world,
                campaign.FriendlyReplacementReserve,
                campaign.HostileReplacementReserve);
        }

        if (elapsed > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(
                nameof(world),
                "Apply campaign evolution in bounded increments of 24 hours or less.");
        }

        double hours = elapsed.TotalHours;

        ConflictFactionOperationalPosture friendlyPosture =
            campaign.Identity?.FriendlyFaction.Posture
            ?? ConflictFactionOperationalPosture.Defensive;

        ConflictFactionOperationalPosture hostilePosture =
            campaign.Identity?.HostileFaction.Posture
            ?? ConflictFactionOperationalPosture.Defensive;

        GroundUnitState[] groundUnits = world.Units
            .Select(unit => RecoverGroundUnit(
                unit,
                world.Units,
                hours))
            .ToArray();

        var friendlyReplacement =
            ApplyGroundReplacements(
                groundUnits,
                ConflictSide.Friendly,
                campaign.FriendlyReplacementReserve,
                hours,
                friendlyPosture);

        groundUnits = friendlyReplacement.Units;
        double friendlyReserve =
            friendlyReplacement.RemainingReserve;

        var hostileReplacement =
            ApplyGroundReplacements(
                groundUnits,
                ConflictSide.Hostile,
                campaign.HostileReplacementReserve,
                hours,
                hostilePosture);

        groundUnits = hostileReplacement.Units;
        double hostileReserve =
            hostileReplacement.RemainingReserve;

        SimulatedAirUnitState[] airUnits = world.AirUnits
            .Select(unit => RecoverAirUnit(
                unit,
                world.Units,
                hours))
            .ToArray();

        ConflictSectorState[] sectors = world.Sectors
            .Select(sector => ConsolidateSector(
                sector,
                campaign.FriendlyMomentum,
                hours))
            .ToArray();

        var evolved = world with
        {
            Units = groundUnits,
            AirUnits = airUnits,
            Sectors = sectors
        };

        evolved = ConflictWorldEngine.RecalculatePressureAndThreats(evolved);
        evolved = SupportRequestGenerator.Refresh(
            evolved,
            evolved.UpdatedAt,
            friendlyPosture);

        ConflictValidation.Validate(evolved);

        return new ConflictCampaignCycleResult(
            evolved,
            friendlyReserve,
            hostileReserve);
    }

    private static (GroundUnitState[] Units, double RemainingReserve)
        ApplyGroundReplacements(
            GroundUnitState[] units,
            ConflictSide side,
            double replacementReserve,
            double hours,
            ConflictFactionOperationalPosture posture)
    {
        if (replacementReserve <= 0)
            return (units, 0);

        bool hasLogistics = units.Any(unit =>
            unit.Side == side
            && unit.Role == GroundUnitRole.Logistics
            && unit.IsOperational
            && unit.Readiness >= 0.25);

        if (!hasLogistics)
            return (units, replacementReserve);

        double budget = Math.Min(
            replacementReserve,
            ReplacementStrengthPerHour * hours);

        if (budget <= 0)
            return (units, replacementReserve);

        GroundUnitState[] updated = units.ToArray();

        foreach (GroundUnitState candidate in updated
            .Where(unit =>
                unit.Side == side
                && unit.IsOperational
                && unit.Strength < ReplacementStrengthCeiling)
            .OrderBy(unit =>
                ConflictFactionBehaviorPolicy.ReplacementPriority(
                    posture,
                    unit.Role))
            .ThenBy(unit => unit.Strength)
            .ThenBy(unit => unit.UnitId)
            .ToArray())
        {
            if (budget <= 0)
                break;

            int index = Array.FindIndex(
                updated,
                item => item.UnitId == candidate.UnitId);

            if (index < 0)
                continue;

            double needed =
                ReplacementStrengthCeiling - candidate.Strength;

            double applied = Math.Min(needed, budget);

            updated[index] = candidate with
            {
                Strength = candidate.Strength + applied
            };

            budget -= applied;
            replacementReserve -= applied;
        }

        return (
            updated,
            Math.Clamp(replacementReserve, 0, 1));
    }

    private static GroundUnitState RecoverGroundUnit(
        GroundUnitState unit,
        IReadOnlyList<GroundUnitState> allUnits,
        double hours)
    {
        if (unit.Side == ConflictSide.Neutral
            || !unit.IsOperational)
        {
            return unit;
        }

        double supportPower = allUnits
            .Where(other =>
                other.UnitId != unit.UnitId
                && other.Side == unit.Side
                && other.Role == GroundUnitRole.Logistics
                && other.IsOperational
                && ConflictGeometry.DistanceNauticalMiles(
                    unit.Position,
                    other.Position)
                    <= LogisticsSupportRadiusNauticalMiles)
            .Sum(other => other.Strength * other.Readiness);

        supportPower = Math.Clamp(supportPower, 0, 1);

        if (supportPower <= 0)
            return unit;

        double pressureFactor =
            1 - Math.Clamp(unit.Pressure, 0, 1) * 0.85;

        double readinessGain =
            supportPower
            * GroundReadinessRecoveryPerHour
            * hours
            * pressureFactor;

        double strengthGain =
            supportPower
            * GroundStrengthRecoveryPerHour
            * hours
            * pressureFactor;

        return unit with
        {
            Strength = Math.Min(
                MaximumStateValue,
                unit.Strength + strengthGain),
            Readiness = Math.Min(
                MaximumStateValue,
                unit.Readiness + readinessGain)
        };
    }

    private static SimulatedAirUnitState RecoverAirUnit(
        SimulatedAirUnitState unit,
        IReadOnlyList<GroundUnitState> groundUnits,
        double hours)
    {
        if (unit.Side == ConflictSide.Neutral
            || !unit.IsOperational)
        {
            return unit;
        }

        double theaterLogistics = groundUnits
            .Where(ground =>
                ground.Side == unit.Side
                && ground.Role == GroundUnitRole.Logistics
                && ground.IsOperational)
            .Sum(ground => ground.Strength * ground.Readiness);

        theaterLogistics = Math.Clamp(theaterLogistics, 0, 1);

        if (theaterLogistics <= 0)
            return unit;

        double readinessGain =
            theaterLogistics
            * AirReadinessRecoveryPerHour
            * hours;

        return unit with
        {
            Readiness = Math.Min(
                MaximumStateValue,
                unit.Readiness + readinessGain)
        };
    }

    private static ConflictSectorState ConsolidateSector(
        ConflictSectorState sector,
        double friendlyMomentum,
        double hours)
    {
        if (Math.Abs(friendlyMomentum) <= 0.000001)
            return sector;

        double shift = Math.Clamp(
            friendlyMomentum
            * MomentumControlShiftPerHour
            * hours,
            -MaximumControlShiftPerCycle,
            MaximumControlShiftPerCycle);

        return sector with
        {
            FriendlyControl = Math.Clamp(
                sector.FriendlyControl + shift,
                0,
                1)
        };
    }
}
