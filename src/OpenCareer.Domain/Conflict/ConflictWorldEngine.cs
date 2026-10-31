namespace OpenCareer.Domain.Conflict;

public static class ConflictWorldEngine
{
    private const double EngagementRadiusNm = 18;
    private const double SectorInfluenceRadiusNm = 35;

    public static ConflictWorldState Advance(
        ConflictWorldState state,
        DateTimeOffset through,
        ConflictFactionOperationalPosture friendlyPosture =
            ConflictFactionOperationalPosture.Defensive)
    {
        ConflictValidation.Validate(state);

        if (through < state.UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(through), "Conflict time cannot move backwards.");

        var elapsed = through - state.UpdatedAt;
        if (elapsed == TimeSpan.Zero)
        {
            return SupportRequestGenerator.Refresh(
                state,
                through,
                friendlyPosture);
        }

        if (elapsed > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(through), "Advance conflict simulation in bounded increments of 24 hours or less.");

        var hours = elapsed.TotalHours;
        var units = state.Units
            .Select(unit => AdvanceUnit(unit, state.Units, hours))
            .ToArray();

        var airUnits = state.AirUnits
            .Select(unit => AdvanceAirUnit(unit, hours))
            .ToArray();

        var sectors = state.Sectors
            .Select(sector => AdvanceSector(sector, units, hours))
            .ToArray();

        var threats = state.Threats
            .Select(threat => SynchronizeThreat(threat, units, airUnits))
            .ToArray();

        var advanced = state with
        {
            Tick = checked(state.Tick + 1),
            UpdatedAt = through,
            Units = units,
            AirUnits = airUnits,
            Sectors = sectors,
            Threats = threats,
            SupportRequests = state.SupportRequests
                .Select(request => request.IsPastOpenDeadline(through)
                    ? request.Expire(through)
                    : request)
                .ToArray()
        };

        return SupportRequestGenerator.Refresh(
            advanced,
            through,
            friendlyPosture);
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
            .Select(threat => SynchronizeThreat(threat, units, state.AirUnits))
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
        IReadOnlyList<GroundUnitState> groundUnits,
        IReadOnlyList<SimulatedAirUnitState> airUnits)
    {
        if (threat.SourceUnitId is not Guid sourceId)
            return threat;

        var groundSource = groundUnits.FirstOrDefault(
            unit => unit.UnitId == sourceId);

        if (groundSource is not null)
        {
            var severity = Math.Clamp(
                groundSource.Strength * groundSource.Readiness,
                0,
                1);

            return threat with
            {
                Center = groundSource.Position,
                Severity = severity,
                Active = groundSource.IsOperational && severity > 0.05
            };
        }

        var airSource = airUnits.FirstOrDefault(
            unit => unit.UnitId == sourceId);

        if (airSource is null)
            return threat with { Active = false, Severity = 0 };

        var airSeverity = Math.Clamp(
            airSource.Strength * airSource.Readiness,
            0,
            1);

        return threat with
        {
            Center = airSource.Position,
            Severity = airSeverity,
            Active = airSource.IsOperational && airSeverity > 0.05
        };
    }

    private static SimulatedAirUnitState AdvanceAirUnit(
        SimulatedAirUnitState unit,
        double hours)
    {
        if (!unit.IsOperational
            || unit.GroundSpeedKnots <= 0
            || unit.Position == unit.Destination)
        {
            return unit;
        }

        var distance = unit.GroundSpeedKnots * hours;
        return unit with
        {
            Position = ConflictGeometry.MoveToward(
                unit.Position,
                unit.Destination,
                distance)
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
        DateTimeOffset now,
        ConflictFactionOperationalPosture posture =
            ConflictFactionOperationalPosture.Defensive)
    {
        ConflictValidation.Validate(state);

        ConflictFactionBehaviorProfile behavior =
            ConflictFactionBehaviorPolicy.For(posture);

        var tracked = state.SupportRequests
            .Select(request => request.IsPastOpenDeadline(now)
                ? request.Expire(now)
                : SynchronizeMovingTarget(request, state.AirUnits))
            .ToList();

        var activeKeys = tracked
            .Where(request => request.IsActive)
            .Select(request => (request.RequestingUnitId, request.Type))
            .ToHashSet();

        AddBattlefieldRequests(
            state,
            now,
            tracked,
            activeKeys,
            behavior);
        AddLogisticsRequests(
            state,
            now,
            tracked,
            activeKeys,
            behavior);
        AddReconnaissanceRequests(
            state,
            now,
            tracked,
            activeKeys,
            behavior);
        AddPatrolRequests(
            state,
            now,
            tracked,
            activeKeys,
            behavior);
        AddAirOperationRequests(
            state,
            now,
            tracked,
            activeKeys,
            behavior);

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
        ISet<(Guid RequestingUnitId, SupportRequestType Type)> activeKeys,
        ConflictFactionBehaviorProfile behavior)
    {
        foreach (var friendly in state.Units
            .Where(unit => unit.Side == ConflictSide.Friendly
                && unit.IsOperational
                && unit.Pressure >= behavior.BattlefieldPressureThreshold)
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
            if (behavior.PromoteBattlefieldUrgency)
            {
                urgency = ConflictFactionBehaviorPolicy.Promote(
                    urgency);
            }
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
        ISet<(Guid RequestingUnitId, SupportRequestType Type)> activeKeys,
        ConflictFactionBehaviorProfile behavior)
    {
        foreach (var friendly in state.Units
            .Where(unit => unit.Side == ConflictSide.Friendly
                && unit.IsOperational
                && unit.Readiness <= behavior.LowReadinessThreshold
                && unit.Pressure < 0.55)
            .OrderBy(unit => unit.Readiness)
            .ThenBy(unit => unit.UnitId))
        {
            SupportUrgency urgency =
                friendly.Readiness < 0.20
                    ? SupportUrgency.Priority
                    : SupportUrgency.Routine;

            if (behavior.PromoteLogisticsUrgency)
            {
                urgency = ConflictFactionBehaviorPolicy.Promote(
                    urgency);
            }

            AddRequestIfNeeded(
                tracked,
                activeKeys,
                state,
                now,
                friendly,
                friendly.UnitId,
                friendly.Position,
                SupportRequestType.Logistics,
                urgency,
                requiredEffect: Math.Clamp(
                    0.60 - friendly.Readiness,
                    0.10,
                    0.40));
        }
    }

    private static void AddReconnaissanceRequests(
        ConflictWorldState state,
        DateTimeOffset now,
        ICollection<AirSupportRequest> tracked,
        ISet<(Guid RequestingUnitId, SupportRequestType Type)> activeKeys,
        ConflictFactionBehaviorProfile behavior)
    {
        foreach (var sector in state.Sectors
            .Where(sector =>
                sector.IntelligenceConfidence
                    < behavior.LowIntelligenceThreshold)
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
        ISet<(Guid RequestingUnitId, SupportRequestType Type)> activeKeys,
        ConflictFactionBehaviorProfile behavior)
    {
        foreach (var sector in state.Sectors
            .Where(sector =>
                sector.FriendlyControl >= behavior.PatrolControlMinimum
                && sector.FriendlyControl <= behavior.PatrolControlMaximum
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

    private static void AddAirOperationRequests(
        ConflictWorldState state,
        DateTimeOffset now,
        ICollection<AirSupportRequest> tracked,
        ISet<(Guid RequestingUnitId, SupportRequestType Type)> activeKeys,
        ConflictFactionBehaviorProfile behavior)
    {
        var friendlyAir = state.AirUnits
            .Where(unit => unit.Side == ConflictSide.Friendly && unit.IsOperational)
            .ToArray();

        var hostileFighters = state.AirUnits
            .Where(unit => unit.Side == ConflictSide.Hostile
                && unit.Role == AirUnitRole.Fighter
                && unit.IsOperational)
            .ToArray();

        foreach (var hostile in hostileFighters
            .OrderBy(unit => unit.UnitId))
        {
            var nearestFriendlyAirDistance = friendlyAir.Length == 0
                ? double.MaxValue
                : friendlyAir.Min(unit =>
                    ConflictGeometry.DistanceNauticalMiles(
                        hostile.Position,
                        unit.Position));

            var requester = NearestFriendly(state, hostile.Position);
            if (requester is null)
                continue;

            var groundDistance = ConflictGeometry.DistanceNauticalMiles(
                requester.Position,
                hostile.Position);

            var closestFriendlyAsset =
                Math.Min(nearestFriendlyAirDistance, groundDistance);

            if (closestFriendlyAsset
                > behavior.InterceptRangeNauticalMiles)
            {
                continue;
            }

            SupportUrgency urgency =
                closestFriendlyAsset <= 45
                    ? SupportUrgency.Priority
                    : SupportUrgency.Routine;

            if (behavior.PromoteAirUrgency)
            {
                urgency = ConflictFactionBehaviorPolicy.Promote(
                    urgency);
            }

            AddRequestIfNeeded(
                tracked,
                activeKeys,
                state,
                now,
                requester,
                hostile.UnitId,
                hostile.Position,
                SupportRequestType.Intercept,
                urgency,
                requiredEffect: 0.20);
        }

        foreach (var package in friendlyAir
            .Where(unit => unit.Role is (
                AirUnitRole.Transport
                or AirUnitRole.Surveillance
                or AirUnitRole.Tanker))
            .OrderBy(unit => unit.UnitId))
        {
            var nearestThreat = hostileFighters
                .Select(hostile => ConflictGeometry.DistanceNauticalMiles(
                    package.Position,
                    hostile.Position))
                .DefaultIfEmpty(double.MaxValue)
                .Min();

            if (nearestThreat
                > behavior.EscortRangeNauticalMiles)
            {
                continue;
            }

            var requester = NearestFriendly(state, package.Position);
            if (requester is null)
                continue;

            SupportUrgency urgency =
                nearestThreat <= 40
                    ? SupportUrgency.Priority
                    : SupportUrgency.Routine;

            if (behavior.PromoteAirUrgency)
            {
                urgency = ConflictFactionBehaviorPolicy.Promote(
                    urgency);
            }

            AddRequestIfNeeded(
                tracked,
                activeKeys,
                state,
                now,
                requester,
                package.UnitId,
                package.Position,
                SupportRequestType.Escort,
                urgency,
                requiredEffect: 0.15);
        }
    }

    private static AirSupportRequest SynchronizeMovingTarget(
        AirSupportRequest request,
        IReadOnlyList<SimulatedAirUnitState> airUnits)
    {
        if (request.TargetUnitId is not Guid targetId
            || request.Type is not (
                SupportRequestType.Escort
                or SupportRequestType.Intercept))
        {
            return request;
        }

        var target = airUnits.FirstOrDefault(
            unit => unit.UnitId == targetId);

        return target is null
            ? request
            : request with { TargetPosition = target.Position };
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
