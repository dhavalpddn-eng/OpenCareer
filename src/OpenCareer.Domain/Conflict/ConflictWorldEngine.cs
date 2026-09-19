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
                .Select(request => request.IsPastOpenDeadline(through)
                    ? request.Expire(through)
                    : request)
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
    private const double LowReadinessThreshold = 0.35;
    private const double LowIntelligenceThreshold = 0.30;

    public static ConflictWorldState Refresh(
        ConflictWorldState state,
        DateTimeOffset now)
    {
        ConflictValidation.Validate(state);

        var tracked = state.SupportRequests
            .Select(request => request.IsPastOpenDeadline(now)
                ? request.Expire(now)
                : request)
            .ToList();

        var activeKeys = tracked
            .Where(request => request.IsActive)
            .Select(request => (request.RequestingUnitId, request.Type))
            .ToHashSet();

        AddBattlefieldRequests(state, now, tracked, activeKeys);
        AddLogisticsRequests(state, now, tracked, activeKeys);
        AddReconnaissanceRequests(state, now, tracked, activeKeys);
        AddPatrolRequests(state, now, tracked, activeKeys);

        return state with
        {
            SupportRequests = tracked
                .OrderBy(request => request.IsTerminal)
                .ThenByDescending(request => request.Urgency)
                .ThenBy(request => request.CreatedAt)
                .ThenBy(request => request.RequestId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void AddBattlefieldRequests(
        ConflictWorldState state,
        DateTimeOffset now,
        ICollection<AirSupportRequest> tracked,
        ISet<(Guid RequestingUnitId, SupportRequestType Type)> activeKeys)
    {
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
                    tracked,
                    activeKeys,
                    state,
                    now,
                    friendly,
                    maneuverTarget.UnitId,
                    maneuverTarget.Position,
                    SupportRequestType.CloseAirSupport,
                    urgency,
                    requiredEffect: Math.Clamp(0.18 + friendly.Pressure * 0.32, 0, 0.55));
            }

            var airDefenseTarget = hostiles
                .Where(unit => unit.Role == GroundUnitRole.AirDefense)
                .OrderByDescending(unit => unit.Strength * unit.Readiness)
                .ThenBy(unit => unit.UnitId)
                .FirstOrDefault();

            if (airDefenseTarget is not null)
            {
                AddRequestIfNeeded(
                    tracked,
                    activeKeys,
                    state,
                    now,
                    friendly,
                    airDefenseTarget.UnitId,
                    airDefenseTarget.Position,
                    SupportRequestType.Suppression,
                    urgency,
                    requiredEffect: Math.Clamp(0.15 + friendly.Pressure * 0.25, 0, 0.45));
            }
        }
    }

    private static void AddLogisticsRequests(
        ConflictWorldState state,
        DateTimeOffset now,
        ICollection<AirSupportRequest> tracked,
        ISet<(Guid RequestingUnitId, SupportRequestType Type)> activeKeys)
    {
        foreach (var friendly in state.Units
            .Where(unit => unit.Side == ConflictSide.Friendly
                && unit.IsOperational
                && unit.Readiness <= LowReadinessThreshold
                && unit.Pressure < 0.55)
            .OrderBy(unit => unit.Readiness)
            .ThenBy(unit => unit.UnitId))
        {
            AddRequestIfNeeded(
                tracked,
                activeKeys,
                state,
                now,
                friendly,
                friendly.UnitId,
                friendly.Position,
                SupportRequestType.Logistics,
                friendly.Readiness < 0.20 ? SupportUrgency.Priority : SupportUrgency.Routine,
                requiredEffect: Math.Clamp(0.60 - friendly.Readiness, 0.10, 0.40));
        }
    }

    private static void AddReconnaissanceRequests(
        ConflictWorldState state,
        DateTimeOffset now,
        ICollection<AirSupportRequest> tracked,
        ISet<(Guid RequestingUnitId, SupportRequestType Type)> activeKeys)
    {
        foreach (var sector in state.Sectors
            .Where(sector => sector.IntelligenceConfidence < LowIntelligenceThreshold)
            .OrderBy(sector => sector.IntelligenceConfidence)
            .ThenBy(sector => sector.SectorId, StringComparer.Ordinal))
        {
            var requester = NearestFriendly(state, sector.Center);
            if (requester is null)
                continue;

            AddRequestIfNeeded(
                tracked,
                activeKeys,
                state,
                now,
                requester,
                targetUnitId: null,
                sector.Center,
                SupportRequestType.Reconnaissance,
                sector.IntelligenceConfidence < 0.15 ? SupportUrgency.Priority : SupportUrgency.Routine,
                requiredEffect: Math.Clamp(0.55 - sector.IntelligenceConfidence, 0.15, 0.40));
        }
    }

    private static void AddPatrolRequests(
        ConflictWorldState state,
        DateTimeOffset now,
        ICollection<AirSupportRequest> tracked,
        ISet<(Guid RequestingUnitId, SupportRequestType Type)> activeKeys)
    {
        foreach (var sector in state.Sectors
            .Where(sector => sector.FriendlyControl is >= 0.40 and <= 0.60
                && sector.IntelligenceConfidence >= 0.50)
            .OrderBy(sector => Math.Abs(sector.FriendlyControl - 0.50))
            .ThenBy(sector => sector.SectorId, StringComparer.Ordinal))
        {
            var requester = NearestFriendly(state, sector.Center);
            if (requester is null)
                continue;

            AddRequestIfNeeded(
                tracked,
                activeKeys,
                state,
                now,
                requester,
                targetUnitId: null,
                sector.Center,
                SupportRequestType.Patrol,
                SupportUrgency.Routine,
                requiredEffect: 0.10);
        }
    }

    private static GroundUnitState? NearestFriendly(
        ConflictWorldState state,
        GeoPoint position) =>
        state.Units
            .Where(unit => unit.Side == ConflictSide.Friendly && unit.IsOperational)
            .OrderBy(unit => ConflictGeometry.DistanceNauticalMiles(position, unit.Position))
            .ThenBy(unit => unit.UnitId)
            .FirstOrDefault();

    private static void AddRequestIfNeeded(
        ICollection<AirSupportRequest> tracked,
        ISet<(Guid RequestingUnitId, SupportRequestType Type)> activeKeys,
        ConflictWorldState state,
        DateTimeOffset now,
        GroundUnitState requester,
        Guid? targetUnitId,
        GeoPoint targetPosition,
        SupportRequestType type,
        SupportUrgency urgency,
        double requiredEffect)
    {
        if (!activeKeys.Add((requester.UnitId, type)))
            return;

        var lifetime = urgency switch
        {
            SupportUrgency.Immediate => TimeSpan.FromMinutes(20),
            SupportUrgency.Priority => TimeSpan.FromMinutes(35),
            _ => TimeSpan.FromMinutes(50)
        };

        var requestId =
            $"{state.TheaterId}:{requester.UnitId:N}:{type}:{state.Tick}";

        tracked.Add(new AirSupportRequest(
            requestId,
            type,
            urgency,
            requester.UnitId,
            targetUnitId,
            targetPosition,
            requiredEffect,
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
