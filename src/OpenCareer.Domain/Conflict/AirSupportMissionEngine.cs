using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Domain.Conflict;

public sealed record AirSupportMissionProfile(
    double IngressRadiusNauticalMiles,
    double OnStationRadiusNauticalMiles,
    double ActionRadiusNauticalMiles,
    double ExitRadiusNauticalMiles,
    double MinimumActionAglFeet,
    double MaximumActionAglFeet,
    double MinimumActionGroundSpeedKnots,
    double MaximumActionGroundSpeedKnots)
{
    public static AirSupportMissionProfile FixedWingDefault { get; } =
        new(
            IngressRadiusNauticalMiles: 35,
            OnStationRadiusNauticalMiles: 10,
            ActionRadiusNauticalMiles: 5,
            ExitRadiusNauticalMiles: 12,
            MinimumActionAglFeet: 300,
            MaximumActionAglFeet: 18_000,
            MinimumActionGroundSpeedKnots: 80,
            MaximumActionGroundSpeedKnots: 700);

    public void Validate()
    {
        if (!double.IsFinite(IngressRadiusNauticalMiles) || IngressRadiusNauticalMiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(IngressRadiusNauticalMiles));

        if (!double.IsFinite(OnStationRadiusNauticalMiles)
            || OnStationRadiusNauticalMiles <= 0
            || OnStationRadiusNauticalMiles > IngressRadiusNauticalMiles)
            throw new ArgumentOutOfRangeException(nameof(OnStationRadiusNauticalMiles));

        if (!double.IsFinite(ActionRadiusNauticalMiles)
            || ActionRadiusNauticalMiles <= 0
            || ActionRadiusNauticalMiles > OnStationRadiusNauticalMiles)
            throw new ArgumentOutOfRangeException(nameof(ActionRadiusNauticalMiles));

        if (!double.IsFinite(ExitRadiusNauticalMiles)
            || ExitRadiusNauticalMiles <= OnStationRadiusNauticalMiles)
            throw new ArgumentOutOfRangeException(nameof(ExitRadiusNauticalMiles));

        if (!double.IsFinite(MinimumActionAglFeet)
            || !double.IsFinite(MaximumActionAglFeet)
            || MinimumActionAglFeet < 0
            || MaximumActionAglFeet <= MinimumActionAglFeet)
            throw new ArgumentOutOfRangeException(nameof(MaximumActionAglFeet));

        if (!double.IsFinite(MinimumActionGroundSpeedKnots)
            || !double.IsFinite(MaximumActionGroundSpeedKnots)
            || MinimumActionGroundSpeedKnots < 0
            || MaximumActionGroundSpeedKnots <= MinimumActionGroundSpeedKnots)
            throw new ArgumentOutOfRangeException(nameof(MaximumActionGroundSpeedKnots));
    }
}

public sealed record AirSupportMission(
    Guid MissionId,
    string SupportRequestId,
    Guid TargetUnitId,
    GeoPoint TargetPosition,
    AirSupportMissionStage Stage,
    DateTimeOffset AcceptedAt,
    DateTimeOffset LastUpdatedAt,
    bool ActionApplied)
{
    public static AirSupportMission Accept(
        AirSupportRequest request,
        Guid missionId,
        DateTimeOffset acceptedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (missionId == Guid.Empty)
            throw new ArgumentException("Mission ID is required.", nameof(missionId));

        if (request.IsExpired(acceptedAt))
            throw new InvalidOperationException("Support request has expired.");

        if (request.TargetUnitId is not Guid targetUnitId)
            throw new InvalidOperationException("Support request does not identify a target.");

        return new AirSupportMission(
            missionId,
            request.RequestId,
            targetUnitId,
            request.TargetPosition,
            AirSupportMissionStage.Accepted,
            acceptedAt,
            acceptedAt,
            ActionApplied: false);
    }
}

public sealed record AirSupportMissionTelemetryResult(
    AirSupportMission Mission,
    double DistanceToTargetNauticalMiles,
    bool ActionWindowSatisfied,
    double GeometryQuality,
    string? BlockingReason);

public static class AirSupportMissionEngine
{
    public static AirSupportMissionTelemetryResult Update(
        AirSupportMission mission,
        AirSupportMissionProfile profile,
        AircraftTelemetrySnapshot telemetry,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(telemetry);
        profile.Validate();

        if (now < mission.LastUpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(now), "Mission time cannot move backwards.");

        if (mission.Stage is AirSupportMissionStage.Failed
            or AirSupportMissionStage.Expired
            or AirSupportMissionStage.ObjectiveComplete)
        {
            return new AirSupportMissionTelemetryResult(
                mission,
                Distance(telemetry, mission.TargetPosition),
                false,
                0,
                "Mission is no longer active.");
        }

        var distance = Distance(telemetry, mission.TargetPosition);

        if (mission.ActionApplied)
        {
            var stage = distance >= profile.ExitRadiusNauticalMiles
                ? AirSupportMissionStage.ObjectiveComplete
                : AirSupportMissionStage.Egress;

            var updated = mission with
            {
                Stage = stage,
                LastUpdatedAt = now
            };

            return new AirSupportMissionTelemetryResult(
                updated,
                distance,
                false,
                0,
                stage == AirSupportMissionStage.ObjectiveComplete
                    ? null
                    : "Exit the objective area.");
        }

        var actionWindow = EvaluateActionWindow(profile, telemetry, distance);
        var stageBeforeAction = distance <= profile.ActionRadiusNauticalMiles && actionWindow.Satisfied
            ? AirSupportMissionStage.ActionAuthorized
            : distance <= profile.OnStationRadiusNauticalMiles
                ? AirSupportMissionStage.OnStation
                : AirSupportMissionStage.Ingress;

        var updatedMission = mission with
        {
            Stage = stageBeforeAction,
            LastUpdatedAt = now
        };

        return new AirSupportMissionTelemetryResult(
            updatedMission,
            distance,
            actionWindow.Satisfied && distance <= profile.ActionRadiusNauticalMiles,
            actionWindow.Satisfied
                ? CalculateGeometryQuality(profile, telemetry, distance)
                : 0,
            actionWindow.Satisfied
                ? distance <= profile.ActionRadiusNauticalMiles
                    ? null
                    : "Proceed into the authorized objective radius."
                : actionWindow.Reason);
    }

    public static AirSupportMission MarkActionApplied(
        AirSupportMission mission,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(mission);

        if (mission.Stage != AirSupportMissionStage.ActionAuthorized)
            throw new InvalidOperationException("Mission action is not authorized.");

        if (now < mission.LastUpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(now));

        return mission with
        {
            Stage = AirSupportMissionStage.Egress,
            LastUpdatedAt = now,
            ActionApplied = true
        };
    }

    private static (bool Satisfied, string? Reason) EvaluateActionWindow(
        AirSupportMissionProfile profile,
        AircraftTelemetrySnapshot telemetry,
        double distance)
    {
        if (telemetry.Paused)
            return (false, "Simulator is paused.");

        if (telemetry.SlewActive)
            return (false, "Slew mode cannot advance conflict objectives.");

        if (telemetry.OnGround)
            return (false, "Aircraft is on the ground.");

        if (distance > profile.ActionRadiusNauticalMiles)
            return (true, null);

        if (telemetry.AltitudeAglFeet < profile.MinimumActionAglFeet
            || telemetry.AltitudeAglFeet > profile.MaximumActionAglFeet)
            return (false, "Altitude is outside the mission-authored action window.");

        if (telemetry.GroundSpeedKnots < profile.MinimumActionGroundSpeedKnots
            || telemetry.GroundSpeedKnots > profile.MaximumActionGroundSpeedKnots)
            return (false, "Ground speed is outside the mission-authored action window.");

        return (true, null);
    }

    private static double CalculateGeometryQuality(
        AirSupportMissionProfile profile,
        AircraftTelemetrySnapshot telemetry,
        double distance)
    {
        var distanceScore = 1 - Math.Clamp(
            distance / profile.ActionRadiusNauticalMiles,
            0,
            1);

        var altitudeMid = (profile.MinimumActionAglFeet + profile.MaximumActionAglFeet) / 2;
        var altitudeHalfRange = (profile.MaximumActionAglFeet - profile.MinimumActionAglFeet) / 2;
        var altitudeScore = 1 - Math.Clamp(
            Math.Abs(telemetry.AltitudeAglFeet - altitudeMid) / altitudeHalfRange,
            0,
            1);

        var speedMid = (profile.MinimumActionGroundSpeedKnots + profile.MaximumActionGroundSpeedKnots) / 2;
        var speedHalfRange = (profile.MaximumActionGroundSpeedKnots - profile.MinimumActionGroundSpeedKnots) / 2;
        var speedScore = 1 - Math.Clamp(
            Math.Abs(telemetry.GroundSpeedKnots - speedMid) / speedHalfRange,
            0,
            1);

        return Math.Clamp(
            distanceScore * 0.50 + altitudeScore * 0.25 + speedScore * 0.25,
            0,
            1);
    }

    private static double Distance(
        AircraftTelemetrySnapshot telemetry,
        GeoPoint target) =>
        ConflictGeometry.DistanceNauticalMiles(
            new GeoPoint(telemetry.LatitudeDegrees, telemetry.LongitudeDegrees),
            target);
}
