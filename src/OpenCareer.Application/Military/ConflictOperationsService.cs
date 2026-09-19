using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Application.Military;

public sealed record AcceptedAirSupportMission(
    ConflictWorldState World,
    AirSupportMission Mission);

public sealed record AcceptedAreaSupportMission(
    ConflictWorldState World,
    AreaSupportMission Mission);

public sealed record ConflictActionExecution(
    ConflictWorldState World,
    AirSupportMission Mission,
    PlayerActionResult Action);

public sealed record CompletedAreaSupportMission(
    ConflictWorldState World,
    AreaSupportMission Mission,
    AreaMissionOutcomeResult Outcome);

public sealed class ConflictOperationsService
{
    public ConflictWorldState AdvanceWorld(
        ConflictWorldState state,
        DateTimeOffset through) =>
        ConflictWorldEngine.Advance(state, through);

    public AcceptedAirSupportMission AcceptCombatSupportRequest(
        ConflictWorldState state,
        string requestId,
        Guid missionId,
        DateTimeOffset acceptedAt)
    {
        ConflictValidation.Validate(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var reservedWorld = SupportRequestLifecycle.Reserve(
            state,
            requestId,
            missionId,
            acceptedAt);

        var request = FindRequest(reservedWorld, requestId);
        var mission = AirSupportMission.Accept(
            request,
            missionId,
            acceptedAt);

        return new AcceptedAirSupportMission(
            reservedWorld,
            mission);
    }

    public AcceptedAreaSupportMission AcceptAreaSupportRequest(
        ConflictWorldState state,
        string requestId,
        Guid missionId,
        DateTimeOffset acceptedAt)
    {
        ConflictValidation.Validate(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var reservedWorld = SupportRequestLifecycle.Reserve(
            state,
            requestId,
            missionId,
            acceptedAt);

        var request = FindRequest(reservedWorld, requestId);
        var mission = AreaSupportMission.Accept(
            request,
            missionId,
            acceptedAt);

        return new AcceptedAreaSupportMission(
            reservedWorld,
            mission);
    }

    public AirSupportMissionTelemetryResult UpdateMission(
        AirSupportMission mission,
        AirSupportMissionProfile profile,
        AircraftTelemetrySnapshot telemetry,
        DateTimeOffset now) =>
        AirSupportMissionEngine.Update(mission, profile, telemetry, now);

    public AreaSupportMissionTelemetryResult UpdateAreaMission(
        AreaSupportMission mission,
        AreaSupportMissionProfile profile,
        AircraftTelemetrySnapshot telemetry,
        DateTimeOffset now) =>
        AreaSupportMissionEngine.Update(mission, profile, telemetry, now);

    public ConflictActionExecution ExecuteAuthorizedAction(
        ConflictWorldState state,
        AirSupportMission mission,
        PlayerActionKind kind,
        string actionId,
        double geometryQuality,
        double targetConfidence,
        DateTimeOffset executedAt)
    {
        if (mission.Stage != AirSupportMissionStage.ActionAuthorized)
            throw new InvalidOperationException("Mission action is not authorized.");

        var supportRequest = EnsureRequestReservedByMission(
            state,
            mission.SupportRequestId,
            mission.MissionId);

        ValidateCombatActionKind(supportRequest.Type, kind);

        var resolution = ConflictActionResolver.Apply(
            state,
            new PlayerActionRequest(
                actionId,
                mission.MissionId,
                mission.TargetUnitId,
                kind,
                geometryQuality,
                targetConfidence));

        if (resolution.Result.Outcome != PlayerActionOutcome.Applied)
        {
            return new ConflictActionExecution(
                resolution.State,
                mission,
                resolution.Result);
        }

        var advancedMission = AirSupportMissionEngine.MarkActionApplied(
            mission,
            executedAt);

        return new ConflictActionExecution(
            resolution.State,
            advancedMission,
            resolution.Result);
    }

    public ConflictWorldState CompleteCombatSupportMission(
        ConflictWorldState state,
        AirSupportMission mission,
        DateTimeOffset completedAt)
    {
        if (mission.Stage != AirSupportMissionStage.ObjectiveComplete)
            throw new InvalidOperationException("Combat-support mission objective is not complete.");

        return SupportRequestLifecycle.Close(
            state,
            mission.SupportRequestId,
            mission.MissionId,
            SupportRequestStatus.Completed,
            completedAt);
    }

    public CompletedAreaSupportMission CompleteAreaSupportMission(
        ConflictWorldState state,
        AreaSupportMission mission,
        DateTimeOffset completedAt)
    {
        if (mission.Stage != AreaSupportMissionStage.ObjectiveComplete)
            throw new InvalidOperationException("Area-support mission objective is not complete.");

        EnsureRequestReservedByMission(
            state,
            mission.SupportRequestId,
            mission.MissionId);

        var outcome = AreaMissionOutcomeResolver.Apply(state, mission);

        var closed = SupportRequestLifecycle.Close(
            outcome.State,
            mission.SupportRequestId,
            mission.MissionId,
            SupportRequestStatus.Completed,
            completedAt);

        return new CompletedAreaSupportMission(
            closed,
            mission,
            outcome with { State = closed });
    }

    public ConflictWorldState FailReservedSupportMission(
        ConflictWorldState state,
        string requestId,
        Guid missionId,
        DateTimeOffset failedAt) =>
        SupportRequestLifecycle.Close(
            state,
            requestId,
            missionId,
            SupportRequestStatus.Failed,
            failedAt);

    public ThreatResolution ResolveThreat(
        ConflictWorldState state,
        ThreatEngagementRequest request,
        PlayerCombatState playerState) =>
        ThreatEngagementResolver.Resolve(state, request, playerState);

    private static AirSupportRequest FindRequest(
        ConflictWorldState state,
        string requestId) =>
        state.SupportRequests
            .SingleOrDefault(candidate =>
                string.Equals(candidate.RequestId, requestId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Support request was not found.");

    private static AirSupportRequest EnsureRequestReservedByMission(
        ConflictWorldState state,
        string requestId,
        Guid missionId)
    {
        var request = FindRequest(state, requestId);

        if (request.Status != SupportRequestStatus.Reserved
            || request.ReservedMissionId != missionId)
        {
            throw new InvalidOperationException(
                "Support request is not reserved by the active mission.");
        }

        return request;
    }

    private static void ValidateCombatActionKind(
        SupportRequestType requestType,
        PlayerActionKind actionKind)
    {
        var valid = requestType switch
        {
            SupportRequestType.CloseAirSupport =>
                actionKind == PlayerActionKind.PrecisionAttack,
            SupportRequestType.Suppression =>
                actionKind == PlayerActionKind.Suppression,
            _ => false
        };

        if (!valid)
        {
            throw new InvalidOperationException(
                $"Action {actionKind} is not valid for support request type {requestType}.");
        }
    }
}
