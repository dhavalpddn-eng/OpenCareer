using OpenCareer.Domain.Conflict;

namespace OpenCareer.Application.Military;

public sealed record ConflictMapUnitProjection(
    Guid UnitId,
    ConflictSide Side,
    string Role,
    GeoPoint Position,
    double Strength,
    double Readiness,
    bool Airborne);

public sealed record ConflictFactionProjection(
    string FactionId,
    string DisplayName,
    string ShortCode,
    ConflictSide Side);

public sealed record ConflictThreatProjection(
    Guid ThreatId,
    AirThreatType Type,
    GeoPoint Center,
    double RadiusNauticalMiles,
    double Severity);

public sealed record ConflictSupportProjection(
    string RequestId,
    SupportRequestType Type,
    SupportUrgency Urgency,
    SupportRequestStatus Status,
    GeoPoint TargetPosition,
    DateTimeOffset ExpiresAt);

public sealed record ActiveMilitaryOperationProjection(
    Guid MissionId,
    string RequestId,
    SupportRequestType Type,
    string Stage);

public sealed record ConflictOperationsSnapshot(
    string CampaignId,
    string TheaterId,
    string OperationId,
    string OperationName,
    ConflictFactionProjection FriendlyFaction,
    ConflictFactionProjection HostileFaction,
    ConflictCampaignPhase Phase,
    ConflictCampaignOutcome Outcome,
    double FriendlyControlAverage,
    double FriendlyMomentum,
    double FriendlyReplacementReserve,
    double HostileReplacementReserve,
    double MilitaryTrust,
    PlayerCombatState PlayerCombatState,
    ConflictFrontSnapshot Front,
    ConflictStrategicObjective[] StrategicObjectives,
    ConflictMapUnitProjection[] Units,
    ConflictThreatProjection[] Threats,
    ConflictSupportProjection[] SupportRequests,
    ConflictCampaignHistoryEntry[] CompletedCampaigns,
    ActiveMilitaryOperationProjection? ActiveOperation,
    DateTimeOffset AsOf);

public static class ConflictOperationsSnapshotBuilder
{
    public static ConflictOperationsSnapshot Build(
        ConflictCampaignCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        checkpoint.Validate();

        ConflictFrontSnapshot front =
            ConflictFrontEstimator.Create(checkpoint.World);

        ConflictCampaignIdentity identity =
            checkpoint.CampaignState.Identity
            ?? ConflictCampaignIdentityGenerator.Create(
                checkpoint.CampaignId,
                checkpoint.World);

        ConflictMapUnitProjection[] ground =
            checkpoint.World.Units
                .Where(unit => unit.IsOperational)
                .Select(unit => new ConflictMapUnitProjection(
                    unit.UnitId,
                    unit.Side,
                    unit.Role.ToString(),
                    unit.Position,
                    unit.Strength,
                    unit.Readiness,
                    Airborne: false))
                .ToArray();

        ConflictMapUnitProjection[] air =
            checkpoint.World.AirUnits
                .Where(unit => unit.IsOperational)
                .Select(unit => new ConflictMapUnitProjection(
                    unit.UnitId,
                    unit.Side,
                    unit.Role.ToString(),
                    unit.Position,
                    unit.Strength,
                    unit.Readiness,
                    Airborne: true))
                .ToArray();

        ConflictThreatProjection[] threats =
            checkpoint.World.Threats
                .Where(threat => threat.Active)
                .OrderByDescending(threat => threat.Severity)
                .ThenBy(threat => threat.ThreatId)
                .Select(threat => new ConflictThreatProjection(
                    threat.ThreatId,
                    threat.Type,
                    threat.Center,
                    threat.RadiusNauticalMiles,
                    threat.Severity))
                .ToArray();

        ConflictSupportProjection[] requests =
            checkpoint.World.SupportRequests
                .Where(request => request.IsActive)
                .OrderByDescending(request => request.Urgency)
                .ThenBy(request => request.CreatedAt)
                .ThenBy(request => request.RequestId, StringComparer.Ordinal)
                .Select(request => new ConflictSupportProjection(
                    request.RequestId,
                    request.Type,
                    request.Urgency,
                    request.Status,
                    request.TargetPosition,
                    request.ExpiresAt))
                .ToArray();

        return new ConflictOperationsSnapshot(
            checkpoint.CampaignId,
            checkpoint.World.TheaterId,
            identity.OperationId,
            identity.OperationName,
            ProjectFaction(identity.FriendlyFaction),
            ProjectFaction(identity.HostileFaction),
            checkpoint.CampaignState.Phase,
            checkpoint.CampaignState.Outcome,
            checkpoint.CampaignState.FriendlyControlAverage,
            checkpoint.CampaignState.FriendlyMomentum,
            checkpoint.CampaignState.FriendlyReplacementReserve,
            checkpoint.CampaignState.HostileReplacementReserve,
            checkpoint.MilitaryCareer.Trust,
            checkpoint.PlayerCombatState,
            front,
            checkpoint.CampaignState.Objectives.ToArray(),
            ground.Concat(air).ToArray(),
            threats,
            requests,
            checkpoint.History.ToArray(),
            BuildActiveOperation(checkpoint),
            checkpoint.SavedAt);
    }

    private static ConflictFactionProjection ProjectFaction(
        ConflictFactionIdentity faction) =>
        new(
            faction.FactionId,
            faction.DisplayName,
            faction.ShortCode,
            faction.Side);

    private static ActiveMilitaryOperationProjection? BuildActiveOperation(
        ConflictCampaignCheckpoint checkpoint)
    {
        if (checkpoint.CombatSupportMissions.Length == 1)
        {
            AirSupportMission mission =
                checkpoint.CombatSupportMissions[0];

            return Projection(
                checkpoint,
                mission.MissionId,
                mission.SupportRequestId,
                mission.Stage.ToString());
        }

        if (checkpoint.AreaSupportMissions.Length == 1)
        {
            AreaSupportMission mission =
                checkpoint.AreaSupportMissions[0];

            return Projection(
                checkpoint,
                mission.MissionId,
                mission.SupportRequestId,
                mission.Stage.ToString());
        }

        if (checkpoint.AirOperationMissions.Length == 1)
        {
            AirOperationMission mission =
                checkpoint.AirOperationMissions[0];

            return Projection(
                checkpoint,
                mission.MissionId,
                mission.SupportRequestId,
                mission.Stage.ToString());
        }

        return null;
    }

    private static ActiveMilitaryOperationProjection Projection(
        ConflictCampaignCheckpoint checkpoint,
        Guid missionId,
        string requestId,
        string stage)
    {
        AirSupportRequest request =
            checkpoint.World.SupportRequests.Single(
                item => string.Equals(
                    item.RequestId,
                    requestId,
                    StringComparison.Ordinal));

        return new ActiveMilitaryOperationProjection(
            missionId,
            requestId,
            request.Type,
            stage);
    }
}
