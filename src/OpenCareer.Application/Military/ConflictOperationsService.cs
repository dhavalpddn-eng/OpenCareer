using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Application.Military;

public sealed record ConflictActionExecution(
    ConflictWorldState World,
    AirSupportMission Mission,
    PlayerActionResult Action);

public sealed class ConflictOperationsService
{
    public ConflictWorldState AdvanceWorld(
        ConflictWorldState state,
        DateTimeOffset through) =>
        ConflictWorldEngine.Advance(state, through);

    public AirSupportMission AcceptSupportRequest(
        ConflictWorldState state,
        string requestId,
        Guid missionId,
        DateTimeOffset acceptedAt)
    {
        ConflictValidation.Validate(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var request = state.SupportRequests
            .SingleOrDefault(candidate =>
                string.Equals(candidate.RequestId, requestId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Support request was not found.");

        return AirSupportMission.Accept(request, missionId, acceptedAt);
    }

    public AirSupportMissionTelemetryResult UpdateMission(
        AirSupportMission mission,
        AirSupportMissionProfile profile,
        AircraftTelemetrySnapshot telemetry,
        DateTimeOffset now) =>
        AirSupportMissionEngine.Update(mission, profile, telemetry, now);

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

    public ThreatResolution ResolveThreat(
        ConflictWorldState state,
        ThreatEngagementRequest request,
        PlayerCombatState playerState) =>
        ThreatEngagementResolver.Resolve(state, request, playerState);
}
