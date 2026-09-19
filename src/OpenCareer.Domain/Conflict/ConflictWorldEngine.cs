namespace OpenCareer.Domain.Conflict;

public static class ConflictWorldEngine
{
    private const double EngagementRadiusNm = 18;
    private const double SectorInfluenceRadiusNm = 35;

    public static ConflictWorldState Advance(
        ConflictWorldState state,
        DateTimeOffset through)
    {
        ConflictValidation.Validate(state);

        if (through < state.UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(through), "Conflict time cannot move backwards.");

        var elapsed = through - state.UpdatedAt;
        if (elapsed == TimeSpan.Zero)
            return SupportRequestGenerator.Refresh(state, through);

        if (elapsed > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(through), "Advance conflict simulation in bounded increments of 24 hours or less.");

        var hours = elapsed.TotalHours;
        var units = state.Units
            .Select(unit => AdvanceUnit(unit, state.Units, hours))
            .ToArray();

        var sectors = state.Sectors
            .Select(sector => AdvanceSector(sector, units, hours))
            .ToArray();

        var threats = state.Threats
            .Select(threat => SynchronizeThreat(threat, units))
            .ToArray();

        var advanced = state with
        {
            Tick = checked(state.Tick + 1),
            UpdatedAt = through,
            Units = units,
            Sectors = sectors,
            Threats = threats,
            SupportRequests = state.SupportRequests
                .Where(request => !request.IsExpired(through))
                .ToArray()
        };

        return SupportRequestGenerator.Refresh(advanced, through);
    }

    internal static ConflictWorldState RecalculatePressureAndThreats(ConflictWorldState state)
    {
        ConflictValidation.Validate(state);

        var units = state.Units
            .Select(unit => unit with
            {
                Pressure = CalculatePressure(unit, state.Units)
            })
            .ToArray();

        var threats = state.Threats
            .Select(threat => SynchronizeThreat(threat, units))
            .ToArray();

        return state with { Units = units, Threats = threats };
    }

    private static GroundUnitState AdvanceUnit(
        GroundUnitState unit,
        IReadOnlyList<GroundUnitState> allUnits,
        double hours)
    {
        if (!unit.IsOperational || unit.Side == ConflictSide.Neutral)
            return unit with { Pressure = 0 };

        var pressure = CalculatePressure(unit, allUnits);
        var attrition = pressure * 0.0125 * hours;
        var readinessLoss = pressure * 0.018 * hours;

        return unit with
        {
            Strength = Math.Clamp(unit.Strength - attrition, 0, 1),
            Readiness = Math.Clamp(unit.Readiness - readinessLoss, 0, 1),
            Pressure = pressure
        };
    }

    private static double CalculatePressure(
        GroundUnitState unit,
        IReadOnlyList<GroundUnitState> allUnits)
    {
        if (!unit.IsOperational || unit.Side == ConflictSide.Neutral)
            return 0;

        var enemyPower = allUnits
            .Where(other => other.Side != unit.Side
                && other.Side != ConflictSide.Neutral
                && other.IsOperational
                && ConflictGeometry.DistanceNauticalMiles(unit.Position, other.Position) <= EngagementRadiusNm)
            .Sum(other => other.Strength * other.Readiness * RolePressureWeight(other.Role));

        var friendlySupport = allUnits
            .Where(other => other.Side == unit.Side
                && other.UnitId != unit.UnitId
                && other.IsOperational
                && ConflictGeometry.DistanceNauticalMiles(unit.Position, other.Position) <= EngagementRadiusNm)
            .Sum(other => other.Strength * other.Readiness * 0.35);

        return Math.Clamp(enemyPower - friendlySupport, 0, 1);
    }

    private static ConflictSectorState AdvanceSector(
        ConflictSectorState sector,
        IReadOnlyList<GroundUnitState> units,
        double hours)
    {
        var friendlyPower = units
            .Where(unit => unit.Side == ConflictSide.Friendly
                && unit.IsOperational
                && ConflictGeometry.DistanceNauticalMiles(sector.Center, unit.Position) <= SectorInfluenceRadiusNm)
            .Sum(unit => unit.Strength * unit.Readiness);

        var hostilePower = units
            .Where(unit => unit.Side == ConflictSide.Hostile
                && unit.IsOperational
                && ConflictGeometry.DistanceNauticalMiles(sector.Center, unit.Position) <= SectorInfluenceRadiusNm)
            .Sum(unit => unit.Strength * unit.Readiness);

        var total = friendlyPower + hostilePower;
        if (total <= 0.0001)
            return sector;

        var balance = (friendlyPower - hostilePower) / total;
        var shift = Math.Clamp(balance * 0.02 * hours, -0.05, 0.05);

        return sector with
        {
            FriendlyControl = Math.Clamp(sector.FriendlyControl + shift, 0, 1)
        };
    }

    private static ThreatState SynchronizeThreat(
        ThreatState threat,
        IReadOnlyList<GroundUnitState> units)
    {
        if (threat.SourceUnitId is not Guid sourceId)
            return threat;

        var source = units.FirstOrDefault(unit => unit.UnitId == sourceId);
        if (source is null)
            return threat with { Active = false, Severity = 0 };

        var severity = Math.Clamp(source.Strength * source.Readiness, 0, 1);
        return threat with
        {
            Center = source.Position,
            Severity = severity,
            Active = source.IsOperational && severity > 0.05
        };
    }

    private static double RolePressureWeight(GroundUnitRole role) => role switch
    {
        GroundUnitRole.Armor => 0.80,
        GroundUnitRole.Infantry => 0.55,
        GroundUnitRole.AirDefense => 0.25,
        GroundUnitRole.Command => 0.20,
        GroundUnitRole.Logistics => 0.10,
        _ => 0.20
    };
}

public static class SupportRequestGenerator
{
    private const double NearbyHostileRadiusNm = 20;

    public static ConflictWorldState Refresh(
        ConflictWorldState state,
        DateTimeOffset now)
    {
        ConflictValidation.Validate(state);

        var active = state.SupportRequests
            .Where(request => !request.IsExpired(now))
            .ToList();

        var activeKeys = active
            .Select(request => (request.RequestingUnitId, request.Type))
            .ToHashSet();

        foreach (var friendly in state.Units
            .Where(unit => unit.Side == ConflictSide.Friendly
                && unit.IsOperational
                && unit.Pressure >= 0.55)
            .OrderByDescending(unit => unit.Pressure)
            .ThenBy(unit => unit.UnitId))
        {
            var hostiles = state.Units
                .Where(unit => unit.Side == ConflictSide.Hostile
                    && unit.IsOperational
                    && ConflictGeometry.DistanceNauticalMiles(friendly.Position, unit.Position) <= NearbyHostileRadiusNm)
                .OrderBy(unit => ConflictGeometry.DistanceNauticalMiles(friendly.Position, unit.Position))
                .ThenBy(unit => unit.UnitId)
                .ToArray();

            if (hostiles.Length == 0)
                continue;

            var urgency = GetUrgency(friendly.Pressure);
            var maneuverTarget = hostiles
                .Where(unit => unit.Role != GroundUnitRole.AirDefense)
                .OrderByDescending(unit => unit.Role == GroundUnitRole.Armor)
                .ThenByDescending(unit => unit.Strength * unit.Readiness)
                .ThenBy(unit => unit.UnitId)
                .FirstOrDefault();

            if (maneuverTarget is not null)
            {
                AddRequestIfNeeded(
                    active,
                    activeKeys,
                    state,
                    now,
                    friendly,
                    maneuverTarget,
                    SupportRequestType.CloseAirSupport,
                    urgency);
            }

            var airDefenseTarget = hostiles
                .Where(unit => unit.Role == GroundUnitRole.AirDefense)
                .OrderByDescending(unit => unit.Strength * unit.Readiness)
                .ThenBy(unit => unit.UnitId)
                .FirstOrDefault();

            if (airDefenseTarget is not null)
            {
                AddRequestIfNeeded(
                    active,
                    activeKeys,
                    state,
                    now,
                    friendly,
                    airDefenseTarget,
                    SupportRequestType.Suppression,
                    urgency);
            }
        }

        return state with
        {
            SupportRequests = active
                .OrderByDescending(request => request.Urgency)
                .ThenBy(request => request.CreatedAt)
                .ThenBy(request => request.RequestId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void AddRequestIfNeeded(
        ICollection<AirSupportRequest> active,
        ISet<(Guid RequestingUnitId, SupportRequestType Type)> activeKeys,
        ConflictWorldState state,
        DateTimeOffset now,
        GroundUnitState friendly,
        GroundUnitState target,
        SupportRequestType type,
        SupportUrgency urgency)
    {
        if (!activeKeys.Add((friendly.UnitId, type)))
            return;

        var lifetime = urgency switch
        {
            SupportUrgency.Immediate => TimeSpan.FromMinutes(20),
            SupportUrgency.Priority => TimeSpan.FromMinutes(35),
            _ => TimeSpan.FromMinutes(50)
        };

        var requestId =
            $"{state.TheaterId}:{friendly.UnitId:N}:{type}:{state.Tick}";

        active.Add(new AirSupportRequest(
            requestId,
            type,
            urgency,
            friendly.UnitId,
            target.UnitId,
            target.Position,
            RequiredEffect: Math.Clamp(0.18 + friendly.Pressure * 0.32, 0, 0.55),
            CreatedAt: now,
            ExpiresAt: now + lifetime));
    }

    private static SupportUrgency GetUrgency(double pressure) =>
        pressure >= 0.82
            ? SupportUrgency.Immediate
            : pressure >= 0.68
                ? SupportUrgency.Priority
                : SupportUrgency.Routine;
}
