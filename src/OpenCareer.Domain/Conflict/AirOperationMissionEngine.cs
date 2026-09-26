using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Domain.Conflict;

public enum AirOperationMissionStage
{
    Accepted,
    Rendezvous,
    Tracking,
    ActionAuthorized,
    Egress,
    ObjectiveComplete,
    Failed,
    Expired
}

public enum InterceptActionKind
{
    Identify,
    CompelDisengagement,
    Neutralize
}

public sealed record AirOperationMissionProfile(
    double ProximityRadiusNauticalMiles,
    double ActionRadiusNauticalMiles,
    double ExitRadiusNauticalMiles,
    TimeSpan RequiredVerifiedProximity,
    TimeSpan MaximumCreditedSampleGap)
{
    public static AirOperationMissionProfile For(SupportRequestType type) =>
        type switch
        {
            SupportRequestType.Escort => new(
                ProximityRadiusNauticalMiles: 8,
                ActionRadiusNauticalMiles: 8,
                ExitRadiusNauticalMiles: 12,
                RequiredVerifiedProximity: TimeSpan.FromMinutes(3),
                MaximumCreditedSampleGap: TimeSpan.FromSeconds(10)),

            SupportRequestType.Intercept => new(
                ProximityRadiusNauticalMiles: 8,
                ActionRadiusNauticalMiles: 3,
                ExitRadiusNauticalMiles: 12,
                RequiredVerifiedProximity: TimeSpan.FromSeconds(20),
                MaximumCreditedSampleGap: TimeSpan.FromSeconds(10)),

            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                "This support-request type does not use the air-operation profile.")
        };

    public void Validate()
    {
        if (!double.IsFinite(ProximityRadiusNauticalMiles)
            || ProximityRadiusNauticalMiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ProximityRadiusNauticalMiles));
        }

        if (!double.IsFinite(ActionRadiusNauticalMiles)
            || ActionRadiusNauticalMiles <= 0
            || ActionRadiusNauticalMiles > ProximityRadiusNauticalMiles)
        {
            throw new ArgumentOutOfRangeException(nameof(ActionRadiusNauticalMiles));
        }

        if (!double.IsFinite(ExitRadiusNauticalMiles)
            || ExitRadiusNauticalMiles <= ProximityRadiusNauticalMiles)
        {
            throw new ArgumentOutOfRangeException(nameof(ExitRadiusNauticalMiles));
        }

        if (RequiredVerifiedProximity <= TimeSpan.Zero
            || RequiredVerifiedProximity > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(RequiredVerifiedProximity));
        }

        if (MaximumCreditedSampleGap <= TimeSpan.Zero
            || MaximumCreditedSampleGap > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCreditedSampleGap));
        }
    }
}

public sealed record AirOperationMission(
    Guid MissionId,
    string SupportRequestId,
    SupportRequestType Type,
    Guid TargetAirUnitId,
    AirOperationMissionStage Stage,
    DateTimeOffset AcceptedAt,
    DateTimeOffset LastUpdatedAt,
    TimeSpan VerifiedProximity,
    bool ActionApplied)
{
    public static AirOperationMission Accept(
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
            SupportRequestType.Escort
            or SupportRequestType.Intercept))
        {
            throw new InvalidOperationException(
                "Support request does not use the air-operation mission lifecycle.");
        }

        if (request.TargetUnitId is not Guid targetAirUnitId)
            throw new InvalidOperationException("Air operation requires a target air unit.");

        return new AirOperationMission(
            missionId,
            request.RequestId,
            request.Type,
            targetAirUnitId,
            AirOperationMissionStage.Accepted,
            acceptedAt,
            acceptedAt,
            TimeSpan.Zero,
            ActionApplied: false);
    }
}

public sealed record AirOperationMissionTelemetryResult(
    AirOperationMission Mission,
    double DistanceToTargetNauticalMiles,
    string? BlockingReason);

public static class AirOperationMissionEngine
{
    public static AirOperationMissionTelemetryResult Update(
        ConflictWorldState state,
        AirOperationMission mission,
        AirOperationMissionProfile profile,
        AircraftTelemetrySnapshot telemetry,
        DateTimeOffset now)
    {
        ConflictValidation.Validate(state);
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(telemetry);
        profile.Validate();

        if (now < mission.LastUpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(now), "Mission time cannot move backwards.");

        var target = state.AirUnits.FirstOrDefault(
            unit => unit.UnitId == mission.TargetAirUnitId);

        if (target is null || !target.IsOperational)
        {
            var failed = mission with
            {
                Stage = AirOperationMissionStage.Failed,
                LastUpdatedAt = now
            };

            return new AirOperationMissionTelemetryResult(
                failed,
                double.MaxValue,
                "Target air unit is no longer available.");
        }

        var distance = ConflictGeometry.DistanceNauticalMiles(
            new GeoPoint(telemetry.LatitudeDegrees, telemetry.LongitudeDegrees),
            target.Position);

        if (mission.Stage is AirOperationMissionStage.ObjectiveComplete
            or AirOperationMissionStage.Failed
            or AirOperationMissionStage.Expired)
        {
            return new AirOperationMissionTelemetryResult(
                mission,
                distance,
                "Mission is no longer active.");
        }

        if (mission.ActionApplied)
        {
            var stage = distance >= profile.ExitRadiusNauticalMiles
                ? AirOperationMissionStage.ObjectiveComplete
                : AirOperationMissionStage.Egress;

            return new AirOperationMissionTelemetryResult(
                mission with
                {
                    Stage = stage,
                    LastUpdatedAt = now
                },
                distance,
                stage == AirOperationMissionStage.ObjectiveComplete
                    ? null
                    : "Separate from the intercept area.");
        }

        var evidenceReason = ValidateFlightEvidence(telemetry);
        if (evidenceReason is not null)
        {
            return new AirOperationMissionTelemetryResult(
                mission with
                {
                    Stage = AirOperationMissionStage.Rendezvous,
                    LastUpdatedAt = now,
                    VerifiedProximity = TimeSpan.Zero
                },
                distance,
                evidenceReason);
        }

        if (distance > profile.ProximityRadiusNauticalMiles)
        {
            return new AirOperationMissionTelemetryResult(
                mission with
                {
                    Stage = AirOperationMissionStage.Rendezvous,
                    LastUpdatedAt = now,
                    VerifiedProximity = TimeSpan.Zero
                },
                distance,
                "Rendezvous with the assigned air unit.");
        }

        var credited = mission.Stage == AirOperationMissionStage.Tracking
            ? Min(now - mission.LastUpdatedAt, profile.MaximumCreditedSampleGap)
            : TimeSpan.Zero;

        var verified = mission.VerifiedProximity + credited;

        if (mission.Type == SupportRequestType.Escort)
        {
            var complete = verified >= profile.RequiredVerifiedProximity;

            return new AirOperationMissionTelemetryResult(
                mission with
                {
                    Stage = complete
                        ? AirOperationMissionStage.ObjectiveComplete
                        : AirOperationMissionStage.Tracking,
                    LastUpdatedAt = now,
                    VerifiedProximity = verified
                },
                distance,
                complete ? null : "Maintain escort proximity.");
        }

        var actionAuthorized =
            verified >= profile.RequiredVerifiedProximity
            && distance <= profile.ActionRadiusNauticalMiles;

        return new AirOperationMissionTelemetryResult(
            mission with
            {
                Stage = actionAuthorized
                    ? AirOperationMissionStage.ActionAuthorized
                    : AirOperationMissionStage.Tracking,
                LastUpdatedAt = now,
                VerifiedProximity = verified
            },
            distance,
            actionAuthorized
                ? null
                : verified < profile.RequiredVerifiedProximity
                    ? "Maintain verified intercept tracking."
                    : "Close to the authorized intercept radius.");
    }

    public static AirOperationMission MarkActionApplied(
        AirOperationMission mission,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(mission);

        if (mission.Type != SupportRequestType.Intercept
            || mission.Stage != AirOperationMissionStage.ActionAuthorized)
        {
            throw new InvalidOperationException("Intercept action is not authorized.");
        }

        if (now < mission.LastUpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(now));

        return mission with
        {
            Stage = AirOperationMissionStage.Egress,
            LastUpdatedAt = now,
            ActionApplied = true
        };
    }

    private static string? ValidateFlightEvidence(
        AircraftTelemetrySnapshot telemetry)
    {
        if (telemetry.Paused)
            return "Simulator is paused.";

        if (telemetry.SlewActive)
            return "Slew mode cannot advance air-operation objectives.";

        if (telemetry.OnGround)
            return "Air operation requires the player aircraft to be airborne.";

        return null;
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;
}

public sealed record InterceptActionResult(
    ConflictWorldState State,
    bool Applied,
    InterceptActionKind Kind,
    double StrengthRemoved,
    double ReadinessRemoved);

public static class InterceptActionResolver
{
    public static InterceptActionResult Apply(
        ConflictWorldState state,
        AirOperationMission mission,
        string actionId,
        InterceptActionKind kind,
        double geometryQuality)
    {
        ConflictValidation.Validate(state);
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        if (mission.Type != SupportRequestType.Intercept
            || mission.Stage != AirOperationMissionStage.ActionAuthorized)
        {
            throw new InvalidOperationException("Intercept action is not authorized.");
        }

        if (!double.IsFinite(geometryQuality) || geometryQuality is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(geometryQuality));

        if (state.ProcessedEventIds.Contains(actionId, StringComparer.Ordinal))
        {
            return new InterceptActionResult(
                state,
                Applied: false,
                kind,
                StrengthRemoved: 0,
                ReadinessRemoved: 0);
        }

        var index = Array.FindIndex(
            state.AirUnits,
            unit => unit.UnitId == mission.TargetAirUnitId);

        if (index < 0
            || state.AirUnits[index].Side != ConflictSide.Hostile
            || !state.AirUnits[index].IsOperational)
        {
            throw new InvalidOperationException(
                "Intercept target is unavailable or no longer hostile.");
        }

        var target = state.AirUnits[index];
        var strengthRemoved = 0d;
        var readinessRemoved = 0d;

        switch (kind)
        {
            case InterceptActionKind.Identify:
                readinessRemoved = 0;
                break;

            case InterceptActionKind.CompelDisengagement:
                readinessRemoved = Math.Min(
                    target.Readiness,
                    0.30 * geometryQuality);
                break;

            case InterceptActionKind.Neutralize:
                strengthRemoved = Math.Min(
                    target.Strength,
                    0.28 * geometryQuality);
                readinessRemoved = Math.Min(
                    target.Readiness,
                    0.22 * geometryQuality);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var airUnits = state.AirUnits.ToArray();
        airUnits[index] = target with
        {
            Strength = Math.Clamp(target.Strength - strengthRemoved, 0, 1),
            Readiness = Math.Clamp(target.Readiness - readinessRemoved, 0, 1)
        };

        var updated = state with
        {
            AirUnits = airUnits,
            ProcessedEventIds = state.ProcessedEventIds
                .Append(actionId)
                .ToArray()
        };

        updated = ConflictWorldEngine.RecalculatePressureAndThreats(updated);
        updated = SupportRequestGenerator.Refresh(updated, state.UpdatedAt);

        return new InterceptActionResult(
            updated,
            Applied: true,
            kind,
            strengthRemoved,
            readinessRemoved);
    }
}
