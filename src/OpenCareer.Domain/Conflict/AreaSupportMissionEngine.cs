using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Domain.Conflict;

public enum AreaSupportMissionStage
{
    Accepted,
    Ingress,
    OnStation,
    ObjectiveComplete,
    Failed,
    Expired
}

public sealed record AreaSupportMissionProfile(
    double ObjectiveRadiusNauticalMiles,
    TimeSpan RequiredVerifiedPresence,
    TimeSpan MaximumCreditedSampleGap,
    double MinimumAglFeet,
    double MaximumAglFeet,
    double MinimumGroundSpeedKnots,
    double MaximumGroundSpeedKnots,
    bool RequireOnGround,
    bool RequireParkingBrake,
    bool RequireContinuousPresence)
{
    public static AreaSupportMissionProfile For(SupportRequestType type) =>
        type switch
        {
            SupportRequestType.Reconnaissance => new(
                ObjectiveRadiusNauticalMiles: 8,
                RequiredVerifiedPresence: TimeSpan.FromSeconds(90),
                MaximumCreditedSampleGap: TimeSpan.FromSeconds(10),
                MinimumAglFeet: 1_000,
                MaximumAglFeet: 25_000,
                MinimumGroundSpeedKnots: 80,
                MaximumGroundSpeedKnots: 500,
                RequireOnGround: false,
                RequireParkingBrake: false,
                RequireContinuousPresence: false),

            SupportRequestType.Logistics => new(
                ObjectiveRadiusNauticalMiles: 1.5,
                RequiredVerifiedPresence: TimeSpan.Zero,
                MaximumCreditedSampleGap: TimeSpan.FromSeconds(10),
                MinimumAglFeet: 0,
                MaximumAglFeet: 250,
                MinimumGroundSpeedKnots: 0,
                MaximumGroundSpeedKnots: 5,
                RequireOnGround: true,
                RequireParkingBrake: true,
                RequireContinuousPresence: true),

            SupportRequestType.Patrol => new(
                ObjectiveRadiusNauticalMiles: 12,
                RequiredVerifiedPresence: TimeSpan.FromMinutes(3),
                MaximumCreditedSampleGap: TimeSpan.FromSeconds(10),
                MinimumAglFeet: 1_500,
                MaximumAglFeet: 30_000,
                MinimumGroundSpeedKnots: 80,
                MaximumGroundSpeedKnots: 550,
                RequireOnGround: false,
                RequireParkingBrake: false,
                RequireContinuousPresence: true),

            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                "This support-request type does not use the area-mission profile.")
        };

    public void Validate()
    {
        if (!double.IsFinite(ObjectiveRadiusNauticalMiles)
            || ObjectiveRadiusNauticalMiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ObjectiveRadiusNauticalMiles));
        }

        if (RequiredVerifiedPresence < TimeSpan.Zero
            || RequiredVerifiedPresence > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(RequiredVerifiedPresence));
        }

        if (MaximumCreditedSampleGap <= TimeSpan.Zero
            || MaximumCreditedSampleGap > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCreditedSampleGap));
        }

        if (!double.IsFinite(MinimumAglFeet)
            || !double.IsFinite(MaximumAglFeet)
            || MinimumAglFeet < 0
            || MaximumAglFeet < MinimumAglFeet)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumAglFeet));
        }

        if (!double.IsFinite(MinimumGroundSpeedKnots)
            || !double.IsFinite(MaximumGroundSpeedKnots)
            || MinimumGroundSpeedKnots < 0
            || MaximumGroundSpeedKnots < MinimumGroundSpeedKnots)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumGroundSpeedKnots));
        }

        if (!RequireOnGround && RequireParkingBrake)
            throw new ArgumentException("Parking-brake requirement requires an on-ground mission profile.");
    }
}

public sealed record AreaSupportMission(
    Guid MissionId,
    string SupportRequestId,
    SupportRequestType Type,
    Guid RequestingUnitId,
    Guid? TargetUnitId,
    GeoPoint ObjectivePosition,
    double RequiredEffect,
    AreaSupportMissionStage Stage,
    DateTimeOffset AcceptedAt,
    DateTimeOffset LastUpdatedAt,
    TimeSpan VerifiedPresence)
{
    public static AreaSupportMission Accept(
        AirSupportRequest request,
        Guid missionId,
        DateTimeOffset acceptedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (missionId == Guid.Empty)
            throw new ArgumentException("Mission ID is required.", nameof(missionId));

        if (request.Status != SupportRequestStatus.Reserved
            || request.ReservedMissionId != missionId)
        {
            throw new InvalidOperationException(
                "Support request must be reserved by this mission before acceptance.");
        }

        if (request.Type is not (
            SupportRequestType.Reconnaissance
            or SupportRequestType.Logistics
            or SupportRequestType.Patrol))
        {
            throw new InvalidOperationException(
                "Support request does not use the area-support mission lifecycle.");
        }

        if (acceptedAt < request.CreatedAt)
            throw new ArgumentOutOfRangeException(nameof(acceptedAt));

        return new AreaSupportMission(
            missionId,
            request.RequestId,
            request.Type,
            request.RequestingUnitId,
            request.TargetUnitId,
            request.TargetPosition,
            request.RequiredEffect,
            AreaSupportMissionStage.Accepted,
            acceptedAt,
            acceptedAt,
            TimeSpan.Zero);
    }
}

public sealed record AreaSupportMissionTelemetryResult(
    AreaSupportMission Mission,
    double DistanceToObjectiveNauticalMiles,
    bool EvidenceSatisfied,
    string? BlockingReason);

public static class AreaSupportMissionEngine
{
    public static AreaSupportMissionTelemetryResult Update(
        AreaSupportMission mission,
        AreaSupportMissionProfile profile,
        AircraftTelemetrySnapshot telemetry,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(telemetry);
        profile.Validate();

        if (now < mission.LastUpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(now), "Mission time cannot move backwards.");

        if (mission.Stage is AreaSupportMissionStage.ObjectiveComplete
            or AreaSupportMissionStage.Failed
            or AreaSupportMissionStage.Expired)
        {
            return new AreaSupportMissionTelemetryResult(
                mission,
                Distance(telemetry, mission.ObjectivePosition),
                false,
                "Mission is no longer active.");
        }

        var distance = Distance(telemetry, mission.ObjectivePosition);
        var evidence = EvaluateEvidence(profile, telemetry, distance);

        if (mission.Type == SupportRequestType.Logistics)
        {
            var completed = evidence.Satisfied
                ? mission with
                {
                    Stage = AreaSupportMissionStage.ObjectiveComplete,
                    LastUpdatedAt = now
                }
                : mission with
                {
                    Stage = AreaSupportMissionStage.Ingress,
                    LastUpdatedAt = now
                };

            return new AreaSupportMissionTelemetryResult(
                completed,
                distance,
                evidence.Satisfied,
                evidence.Reason);
        }

        if (!evidence.Satisfied)
        {
            var retained = profile.RequireContinuousPresence
                ? TimeSpan.Zero
                : mission.VerifiedPresence;

            return new AreaSupportMissionTelemetryResult(
                mission with
                {
                    Stage = AreaSupportMissionStage.Ingress,
                    LastUpdatedAt = now,
                    VerifiedPresence = retained
                },
                distance,
                false,
                evidence.Reason);
        }

        var credited = mission.Stage == AreaSupportMissionStage.OnStation
            ? Min(now - mission.LastUpdatedAt, profile.MaximumCreditedSampleGap)
            : TimeSpan.Zero;

        var verified = mission.VerifiedPresence + credited;
        var complete = verified >= profile.RequiredVerifiedPresence;

        var updated = mission with
        {
            Stage = complete
                ? AreaSupportMissionStage.ObjectiveComplete
                : AreaSupportMissionStage.OnStation,
            LastUpdatedAt = now,
            VerifiedPresence = verified
        };

        return new AreaSupportMissionTelemetryResult(
            updated,
            distance,
            true,
            complete ? null : "Maintain verified presence in the objective area.");
    }

    private static (bool Satisfied, string? Reason) EvaluateEvidence(
        AreaSupportMissionProfile profile,
        AircraftTelemetrySnapshot telemetry,
        double distance)
    {
        if (telemetry.Paused)
            return (false, "Simulator is paused.");

        if (telemetry.SlewActive)
            return (false, "Slew mode cannot advance military objectives.");

        if (distance > profile.ObjectiveRadiusNauticalMiles)
            return (false, "Proceed into the mission objective area.");

        if (profile.RequireOnGround != telemetry.OnGround)
        {
            return profile.RequireOnGround
                ? (false, "Land inside the objective area.")
                : (false, "Mission requires the aircraft to remain airborne.");
        }

        if (profile.RequireParkingBrake && !telemetry.ParkingBrakeSet)
            return (false, "Set the parking brake to confirm the logistics stop.");

        if (telemetry.AltitudeAglFeet < profile.MinimumAglFeet
            || telemetry.AltitudeAglFeet > profile.MaximumAglFeet)
        {
            return (false, "Altitude is outside the mission-authored objective window.");
        }

        if (telemetry.GroundSpeedKnots < profile.MinimumGroundSpeedKnots
            || telemetry.GroundSpeedKnots > profile.MaximumGroundSpeedKnots)
        {
            return (false, "Ground speed is outside the mission-authored objective window.");
        }

        return (true, null);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;

    private static double Distance(
        AircraftTelemetrySnapshot telemetry,
        GeoPoint target) =>
        ConflictGeometry.DistanceNauticalMiles(
            new GeoPoint(telemetry.LatitudeDegrees, telemetry.LongitudeDegrees),
            target);
}

public sealed record AreaMissionOutcomeResult(
    ConflictWorldState State,
    bool Applied,
    double IntelligenceGain,
    double ReadinessGain,
    double FriendlyControlGain);

public static class AreaMissionOutcomeResolver
{
    public static AreaMissionOutcomeResult Apply(
        ConflictWorldState state,
        AreaSupportMission mission)
    {
        ConflictValidation.Validate(state);
        ArgumentNullException.ThrowIfNull(mission);

        if (mission.Stage != AreaSupportMissionStage.ObjectiveComplete)
            throw new InvalidOperationException("Area mission objective is not complete.");

        var eventId = $"military-area:{mission.MissionId:N}:effect";
        if (state.ProcessedEventIds.Contains(eventId, StringComparer.Ordinal))
        {
            return new AreaMissionOutcomeResult(
                state,
                Applied: false,
                IntelligenceGain: 0,
                ReadinessGain: 0,
                FriendlyControlGain: 0);
        }

        var units = state.Units.ToArray();
        var sectors = state.Sectors.ToArray();
        var intelligenceGain = 0d;
        var readinessGain = 0d;
        var controlGain = 0d;

        switch (mission.Type)
        {
            case SupportRequestType.Reconnaissance:
            {
                var index = FindNearestSectorIndex(sectors, mission.ObjectivePosition);
                if (index < 0)
                    throw new InvalidOperationException("Reconnaissance objective has no conflict sector.");

                intelligenceGain = Math.Min(
                    mission.RequiredEffect,
                    1 - sectors[index].IntelligenceConfidence);

                sectors[index] = sectors[index] with
                {
                    IntelligenceConfidence =
                        Math.Clamp(
                            sectors[index].IntelligenceConfidence + intelligenceGain,
                            0,
                            1)
                };
                break;
            }

            case SupportRequestType.Logistics:
            {
                if (mission.TargetUnitId is not Guid targetUnitId)
                    throw new InvalidOperationException("Logistics mission requires a target unit.");

                var index = Array.FindIndex(units, unit => unit.UnitId == targetUnitId);
                if (index < 0 || units[index].Side != ConflictSide.Friendly)
                    throw new InvalidOperationException("Logistics target is unavailable or not friendly.");

                readinessGain = Math.Min(
                    mission.RequiredEffect,
                    1 - units[index].Readiness);

                units[index] = units[index] with
                {
                    Readiness = Math.Clamp(
                        units[index].Readiness + readinessGain,
                        0,
                        1)
                };
                break;
            }

            case SupportRequestType.Patrol:
            {
                var index = FindNearestSectorIndex(sectors, mission.ObjectivePosition);
                if (index < 0)
                    throw new InvalidOperationException("Patrol objective has no conflict sector.");

                controlGain = Math.Min(
                    Math.Clamp(mission.RequiredEffect * 0.20, 0, 0.03),
                    1 - sectors[index].FriendlyControl);

                intelligenceGain = Math.Min(
                    0.05,
                    1 - sectors[index].IntelligenceConfidence);

                sectors[index] = sectors[index] with
                {
                    FriendlyControl = Math.Clamp(
                        sectors[index].FriendlyControl + controlGain,
                        0,
                        1),
                    IntelligenceConfidence = Math.Clamp(
                        sectors[index].IntelligenceConfidence + intelligenceGain,
                        0,
                        1)
                };
                break;
            }

            default:
                throw new InvalidOperationException(
                    "Mission type does not use the area-support outcome resolver.");
        }

        var updated = state with
        {
            Units = units,
            Sectors = sectors,
            ProcessedEventIds = state.ProcessedEventIds
                .Append(eventId)
                .ToArray()
        };

        updated = ConflictWorldEngine.RecalculatePressureAndThreats(updated);
        updated = SupportRequestGenerator.Refresh(updated, state.UpdatedAt);

        return new AreaMissionOutcomeResult(
            updated,
            Applied: true,
            intelligenceGain,
            readinessGain,
            controlGain);
    }

    private static int FindNearestSectorIndex(
        IReadOnlyList<ConflictSectorState> sectors,
        GeoPoint position)
    {
        if (sectors.Count == 0)
            return -1;

        var bestIndex = 0;
        var bestDistance = double.MaxValue;

        for (var index = 0; index < sectors.Count; index++)
        {
            var distance = ConflictGeometry.DistanceNauticalMiles(
                sectors[index].Center,
                position);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }
}
