namespace OpenCareer.Domain.Conflict;

public static class ConflictCampaignCycleEngine
{
    private const double LogisticsSupportRadiusNauticalMiles = 45;
    private const double GroundReadinessRecoveryPerHour = 0.008;
    private const double GroundStrengthRecoveryPerHour = 0.002;
    private const double AirReadinessRecoveryPerHour = 0.004;
    private const double MomentumControlShiftPerHour = 0.003;
    private const double MaximumStateValue = 0.98;
    private const double MaximumControlShiftPerCycle = 0.025;

    public static ConflictWorldState Apply(
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
        if (elapsed == TimeSpan.Zero)
            return world;

        if (elapsed > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(
                nameof(world),
                "Apply campaign evolution in bounded increments of 24 hours or less.");
        }

        double hours = elapsed.TotalHours;

        GroundUnitState[] groundUnits = world.Units
            .Select(unit => RecoverGroundUnit(
                unit,
                world.Units,
                hours))
            .ToArray();

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
            evolved.UpdatedAt);

        ConflictValidation.Validate(evolved);
        return evolved;
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
