namespace OpenCareer.Domain.Conflict;

public enum ConflictSide
{
    Friendly,
    Hostile,
    Neutral
}

public enum GroundUnitRole
{
    Infantry,
    Armor,
    Logistics,
    AirDefense,
    Command
}

public enum AirThreatType
{
    AirDefense,
    Interceptor,
    Unknown
}

public enum SupportRequestType
{
    CloseAirSupport,
    Reconnaissance,
    Logistics,
    Patrol,
    Escort,
    Suppression
}

public enum SupportUrgency
{
    Routine,
    Priority,
    Immediate
}

public enum SupportRequestStatus
{
    Open,
    Reserved,
    Completed,
    Failed,
    Cancelled,
    Expired
}

public enum AirSupportMissionStage
{
    Offered,
    Accepted,
    Ingress,
    OnStation,
    ActionAuthorized,
    Egress,
    ObjectiveComplete,
    Failed,
    Expired
}

public enum PlayerActionKind
{
    PrecisionAttack,
    Suppression,
    Reconnaissance
}

public enum PlayerActionOutcome
{
    Applied,
    DuplicateIgnored,
    InvalidTarget,
    Rejected
}

public enum ThreatEngagementOutcome
{
    NoEngagement,
    Defeated,
    NearMiss,
    Hit,
    DuplicateIgnored
}

public enum SimulatedDamageLevel
{
    None,
    Light,
    Moderate,
    Severe,
    MissionKill
}

public sealed record GeoPoint(double LatitudeDegrees, double LongitudeDegrees)
{
    public void Validate()
    {
        if (!double.IsFinite(LatitudeDegrees) || LatitudeDegrees is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(LatitudeDegrees));

        if (!double.IsFinite(LongitudeDegrees) || LongitudeDegrees is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(LongitudeDegrees));
    }
}

public sealed record GroundUnitState(
    Guid UnitId,
    ConflictSide Side,
    GroundUnitRole Role,
    GeoPoint Position,
    double Strength,
    double Readiness,
    double Pressure,
    bool IsMobile)
{
    public bool IsOperational => Strength > 0.01 && Readiness > 0.01;

    public void Validate()
    {
        if (UnitId == Guid.Empty)
            throw new ArgumentException("Unit ID is required.", nameof(UnitId));

        ArgumentNullException.ThrowIfNull(Position);
        Position.Validate();
        ValidateUnitInterval(Strength, nameof(Strength));
        ValidateUnitInterval(Readiness, nameof(Readiness));
        ValidateUnitInterval(Pressure, nameof(Pressure));
    }

    private static void ValidateUnitInterval(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record ConflictSectorState(
    string SectorId,
    GeoPoint Center,
    double FriendlyControl,
    double IntelligenceConfidence)
{
    public double HostileControl => 1 - FriendlyControl;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SectorId);
        ArgumentNullException.ThrowIfNull(Center);
        Center.Validate();

        if (!double.IsFinite(FriendlyControl) || FriendlyControl is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(FriendlyControl));

        if (!double.IsFinite(IntelligenceConfidence) || IntelligenceConfidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(IntelligenceConfidence));
    }
}

public sealed record ThreatState(
    Guid ThreatId,
    Guid? SourceUnitId,
    ConflictSide Side,
    AirThreatType Type,
    GeoPoint Center,
    double RadiusNauticalMiles,
    double Severity,
    bool Active)
{
    public void Validate()
    {
        if (ThreatId == Guid.Empty)
            throw new ArgumentException("Threat ID is required.", nameof(ThreatId));

        ArgumentNullException.ThrowIfNull(Center);
        Center.Validate();

        if (!double.IsFinite(RadiusNauticalMiles) || RadiusNauticalMiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(RadiusNauticalMiles));

        if (!double.IsFinite(Severity) || Severity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Severity));
    }
}

public sealed record AirSupportRequest(
    string RequestId,
    SupportRequestType Type,
    SupportUrgency Urgency,
    Guid RequestingUnitId,
    Guid? TargetUnitId,
    GeoPoint TargetPosition,
    double RequiredEffect,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    SupportRequestStatus Status = SupportRequestStatus.Open,
    Guid? ReservedMissionId = null,
    DateTimeOffset? ClosedAt = null)
{
    public bool IsPastOpenDeadline(DateTimeOffset now) =>
        Status == SupportRequestStatus.Open && now >= ExpiresAt;

    public bool IsActive =>
        Status is SupportRequestStatus.Open or SupportRequestStatus.Reserved;

    public bool IsTerminal =>
        Status is SupportRequestStatus.Completed
            or SupportRequestStatus.Failed
            or SupportRequestStatus.Cancelled
            or SupportRequestStatus.Expired;

    public AirSupportRequest Reserve(Guid missionId, DateTimeOffset acceptedAt)
    {
        if (missionId == Guid.Empty)
            throw new ArgumentException("Mission ID is required.", nameof(missionId));

        if (Status != SupportRequestStatus.Open)
            throw new InvalidOperationException("Only an open support request can be reserved.");

        if (acceptedAt < CreatedAt)
            throw new ArgumentOutOfRangeException(nameof(acceptedAt));

        if (acceptedAt >= ExpiresAt)
            throw new InvalidOperationException("Support request has expired.");

        return this with
        {
            Status = SupportRequestStatus.Reserved,
            ReservedMissionId = missionId
        };
    }

    public AirSupportRequest Expire(DateTimeOffset now)
    {
        if (Status != SupportRequestStatus.Open)
            return this;

        if (now < ExpiresAt)
            return this;

        return this with
        {
            Status = SupportRequestStatus.Expired,
            ClosedAt = now
        };
    }

    public AirSupportRequest Close(
        Guid missionId,
        SupportRequestStatus terminalStatus,
        DateTimeOffset closedAt)
    {
        if (terminalStatus is not (
            SupportRequestStatus.Completed
            or SupportRequestStatus.Failed
            or SupportRequestStatus.Cancelled))
        {
            throw new ArgumentOutOfRangeException(nameof(terminalStatus));
        }

        if (Status != SupportRequestStatus.Reserved
            || ReservedMissionId != missionId)
        {
            throw new InvalidOperationException(
                "Support request is not reserved by this mission.");
        }

        if (closedAt < CreatedAt)
            throw new ArgumentOutOfRangeException(nameof(closedAt));

        return this with
        {
            Status = terminalStatus,
            ClosedAt = closedAt
        };
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RequestId);

        if (RequestingUnitId == Guid.Empty)
            throw new ArgumentException("Requesting unit ID is required.", nameof(RequestingUnitId));

        ArgumentNullException.ThrowIfNull(TargetPosition);
        TargetPosition.Validate();

        if (!double.IsFinite(RequiredEffect) || RequiredEffect is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(RequiredEffect));

        if (ExpiresAt <= CreatedAt)
            throw new ArgumentException("Support request must expire after it is created.");

        switch (Status)
        {
            case SupportRequestStatus.Open:
                if (ReservedMissionId is not null || ClosedAt is not null)
                    throw new ArgumentException("Open support request cannot have reservation/closure data.");
                break;

            case SupportRequestStatus.Reserved:
                if (ReservedMissionId is null || ReservedMissionId == Guid.Empty)
                    throw new ArgumentException("Reserved support request requires a mission ID.");
                if (ClosedAt is not null)
                    throw new ArgumentException("Reserved support request cannot already be closed.");
                break;

            case SupportRequestStatus.Completed:
            case SupportRequestStatus.Failed:
            case SupportRequestStatus.Cancelled:
                if (ReservedMissionId is null || ReservedMissionId == Guid.Empty)
                    throw new ArgumentException("Mission-closed support request requires a mission ID.");
                if (ClosedAt is null || ClosedAt < CreatedAt)
                    throw new ArgumentException("Closed support request requires a valid close time.");
                break;

            case SupportRequestStatus.Expired:
                if (ReservedMissionId is not null)
                    throw new ArgumentException("Expired unaccepted request cannot have a mission reservation.");
                if (ClosedAt is null || ClosedAt < ExpiresAt)
                    throw new ArgumentException("Expired request requires a close time at/after expiry.");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(Status));
        }
    }
}

public sealed record ConflictWorldState(
    int SchemaVersion,
    string TheaterId,
    ulong TheaterSeed,
    long Tick,
    DateTimeOffset UpdatedAt,
    GroundUnitState[] Units,
    ConflictSectorState[] Sectors,
    ThreatState[] Threats,
    AirSupportRequest[] SupportRequests,
    string[] ProcessedEventIds)
{
    public static ConflictWorldState Create(
        string theaterId,
        ulong theaterSeed,
        DateTimeOffset updatedAt,
        IEnumerable<GroundUnitState> units,
        IEnumerable<ConflictSectorState> sectors,
        IEnumerable<ThreatState>? threats = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(theaterId);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(sectors);

        var state = new ConflictWorldState(
            SchemaVersion: 1,
            theaterId,
            theaterSeed,
            Tick: 0,
            updatedAt,
            units.ToArray(),
            sectors.ToArray(),
            threats?.ToArray() ?? Array.Empty<ThreatState>(),
            Array.Empty<AirSupportRequest>(),
            Array.Empty<string>());

        ConflictValidation.Validate(state);
        return state;
    }
}

public sealed record PlayerActionRequest(
    string ActionId,
    Guid MissionId,
    Guid TargetUnitId,
    PlayerActionKind Kind,
    double GeometryQuality,
    double TargetConfidence)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ActionId);

        if (MissionId == Guid.Empty)
            throw new ArgumentException("Mission ID is required.", nameof(MissionId));

        if (TargetUnitId == Guid.Empty)
            throw new ArgumentException("Target unit ID is required.", nameof(TargetUnitId));

        if (!double.IsFinite(GeometryQuality) || GeometryQuality is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(GeometryQuality));

        if (!double.IsFinite(TargetConfidence) || TargetConfidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(TargetConfidence));
    }
}

public sealed record PlayerActionResult(
    string ActionId,
    PlayerActionOutcome Outcome,
    double StrengthRemoved,
    double ReadinessRemoved,
    double IntelligenceGain,
    string? Reason);

public sealed record PlayerCombatState(
    double AirframeDamage,
    double PropulsionDamage,
    double SystemsDamage)
{
    public static PlayerCombatState Undamaged { get; } = new(0, 0, 0);

    public double MaximumDamage => Math.Max(AirframeDamage, Math.Max(PropulsionDamage, SystemsDamage));

    public bool MissionCapable => MaximumDamage < 0.75;

    public void Validate()
    {
        ValidateDamage(AirframeDamage, nameof(AirframeDamage));
        ValidateDamage(PropulsionDamage, nameof(PropulsionDamage));
        ValidateDamage(SystemsDamage, nameof(SystemsDamage));
    }

    private static void ValidateDamage(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record ThreatEngagementRequest(
    string EngagementId,
    Guid MissionId,
    Guid ThreatId,
    TimeSpan ExposureDuration,
    double DefensiveResponseQuality)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(EngagementId);

        if (MissionId == Guid.Empty)
            throw new ArgumentException("Mission ID is required.", nameof(MissionId));

        if (ThreatId == Guid.Empty)
            throw new ArgumentException("Threat ID is required.", nameof(ThreatId));

        if (ExposureDuration < TimeSpan.Zero || ExposureDuration > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(ExposureDuration));

        if (!double.IsFinite(DefensiveResponseQuality) || DefensiveResponseQuality is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(DefensiveResponseQuality));
    }
}

public sealed record ThreatEngagementResult(
    string EngagementId,
    ThreatEngagementOutcome Outcome,
    SimulatedDamageLevel DamageLevel,
    PlayerCombatState PlayerState,
    string? Reason);

public static class ConflictValidation
{
    public static void Validate(ConflictWorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.SchemaVersion != 1)
            throw new ArgumentException("Unsupported conflict-state schema version.");

        ArgumentException.ThrowIfNullOrWhiteSpace(state.TheaterId);
        ArgumentNullException.ThrowIfNull(state.Units);
        ArgumentNullException.ThrowIfNull(state.Sectors);
        ArgumentNullException.ThrowIfNull(state.Threats);
        ArgumentNullException.ThrowIfNull(state.SupportRequests);
        ArgumentNullException.ThrowIfNull(state.ProcessedEventIds);

        foreach (var unit in state.Units)
            unit.Validate();

        foreach (var sector in state.Sectors)
            sector.Validate();

        foreach (var threat in state.Threats)
            threat.Validate();

        foreach (var request in state.SupportRequests)
            request.Validate();

        if (state.Units.Select(unit => unit.UnitId).Distinct().Count() != state.Units.Length)
            throw new ArgumentException("Duplicate ground-unit IDs.");

        if (state.Sectors.Select(sector => sector.SectorId).Distinct(StringComparer.Ordinal).Count() != state.Sectors.Length)
            throw new ArgumentException("Duplicate conflict-sector IDs.");

        if (state.Threats.Select(threat => threat.ThreatId).Distinct().Count() != state.Threats.Length)
            throw new ArgumentException("Duplicate threat IDs.");

        if (state.SupportRequests.Select(request => request.RequestId).Distinct(StringComparer.Ordinal).Count() != state.SupportRequests.Length)
            throw new ArgumentException("Duplicate support-request IDs.");

        var reservedMissionIds = state.SupportRequests
            .Where(request => request.ReservedMissionId.HasValue && request.IsActive)
            .Select(request => request.ReservedMissionId!.Value)
            .ToArray();

        if (reservedMissionIds.Distinct().Count() != reservedMissionIds.Length)
            throw new ArgumentException("One mission cannot reserve multiple active support requests.");

        if (state.ProcessedEventIds.Distinct(StringComparer.Ordinal).Count() != state.ProcessedEventIds.Length)
            throw new ArgumentException("Duplicate processed conflict-event IDs.");
    }
}

public static class ConflictGeometry
{
    private const double EarthRadiusNauticalMiles = 3440.065;

    public static double DistanceNauticalMiles(GeoPoint from, GeoPoint to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        from.Validate();
        to.Validate();

        var lat1 = DegreesToRadians(from.LatitudeDegrees);
        var lat2 = DegreesToRadians(to.LatitudeDegrees);
        var deltaLat = lat2 - lat1;
        var deltaLon = DegreesToRadians(to.LongitudeDegrees - from.LongitudeDegrees);

        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
            + Math.Cos(lat1) * Math.Cos(lat2)
            * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        var angle = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0, 1 - a)));
        return EarthRadiusNauticalMiles * angle;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}

public static class SupportRequestLifecycle
{
    public static ConflictWorldState Reserve(
        ConflictWorldState state,
        string requestId,
        Guid missionId,
        DateTimeOffset acceptedAt)
    {
        ConflictValidation.Validate(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var index = Array.FindIndex(
            state.SupportRequests,
            request => string.Equals(request.RequestId, requestId, StringComparison.Ordinal));

        if (index < 0)
            throw new InvalidOperationException("Support request was not found.");

        if (state.SupportRequests.Any(
            request => request.IsActive
                && request.ReservedMissionId == missionId
                && !string.Equals(request.RequestId, requestId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Mission already reserves another support request.");
        }

        var requests = state.SupportRequests.ToArray();
        requests[index] = requests[index].Reserve(missionId, acceptedAt);

        var updated = state with { SupportRequests = requests };
        ConflictValidation.Validate(updated);
        return updated;
    }

    public static ConflictWorldState Close(
        ConflictWorldState state,
        string requestId,
        Guid missionId,
        SupportRequestStatus terminalStatus,
        DateTimeOffset closedAt)
    {
        ConflictValidation.Validate(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var index = Array.FindIndex(
            state.SupportRequests,
            request => string.Equals(request.RequestId, requestId, StringComparison.Ordinal));

        if (index < 0)
            throw new InvalidOperationException("Support request was not found.");

        var requests = state.SupportRequests.ToArray();
        requests[index] = requests[index].Close(missionId, terminalStatus, closedAt);

        var updated = state with { SupportRequests = requests };
        ConflictValidation.Validate(updated);
        return updated;
    }
}
