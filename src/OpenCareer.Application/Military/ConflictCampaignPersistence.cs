using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Application.Military;

public sealed record ConflictCampaignCheckpoint(
    int SchemaVersion,
    string CampaignId,
    ConflictWorldState World,
    ConflictCampaignState CampaignState,
    MilitaryCareerState MilitaryCareer,
    PlayerCombatState PlayerCombatState,
    AirSupportMission[] CombatSupportMissions,
    AreaSupportMission[] AreaSupportMissions,
    AirOperationMission[] AirOperationMissions,
    DateTimeOffset SavedAt)
{
    public const int CurrentSchemaVersion = 2;

    public static ConflictCampaignCheckpoint Create(
        string campaignId,
        ConflictWorldState world,
        MilitaryCareerState militaryCareer,
        PlayerCombatState playerCombatState,
        IEnumerable<AirSupportMission>? combatSupportMissions,
        IEnumerable<AreaSupportMission>? areaSupportMissions,
        IEnumerable<AirOperationMission>? airOperationMissions,
        DateTimeOffset savedAt,
        ConflictCampaignState? campaignState = null)
    {
        var checkpoint = new ConflictCampaignCheckpoint(
            CurrentSchemaVersion,
            campaignId,
            world,
            campaignState ?? ConflictCampaignDirector.Create(campaignId, world),
            militaryCareer,
            playerCombatState,
            combatSupportMissions?.ToArray() ?? Array.Empty<AirSupportMission>(),
            areaSupportMissions?.ToArray() ?? Array.Empty<AreaSupportMission>(),
            airOperationMissions?.ToArray() ?? Array.Empty<AirOperationMission>(),
            savedAt);

        checkpoint.Validate();
        return checkpoint;
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new NotSupportedException($"Conflict checkpoint schema {SchemaVersion} is not supported.");

        ArgumentException.ThrowIfNullOrWhiteSpace(CampaignId);
        ArgumentNullException.ThrowIfNull(World);
        ArgumentNullException.ThrowIfNull(CampaignState);
        ArgumentNullException.ThrowIfNull(MilitaryCareer);
        ArgumentNullException.ThrowIfNull(PlayerCombatState);
        ArgumentNullException.ThrowIfNull(CombatSupportMissions);
        ArgumentNullException.ThrowIfNull(AreaSupportMissions);
        ArgumentNullException.ThrowIfNull(AirOperationMissions);

        ConflictValidation.Validate(World);
        CampaignState.Validate();
        MilitaryCareer.Validate();

        if (!string.Equals(CampaignState.CampaignId, CampaignId, StringComparison.Ordinal))
            throw new ArgumentException("Campaign IDs do not match.");

        if (!string.Equals(CampaignState.TheaterId, World.TheaterId, StringComparison.Ordinal))
            throw new ArgumentException("Campaign theater does not match checkpoint world.");

        if (CampaignState.UpdatedAt != World.UpdatedAt)
            throw new ArgumentException("Campaign state must match checkpoint world time.");

        PlayerCombatState.Validate();

        if (SavedAt < World.UpdatedAt)
            throw new ArgumentException("Checkpoint save time cannot precede world time.");

        var reservations = new List<(Guid MissionId, string RequestId)>();

        foreach (var mission in CombatSupportMissions)
        {
            if (mission.MissionId == Guid.Empty)
                throw new ArgumentException("Combat-support mission ID is required.");

            if (mission.Stage is AirSupportMissionStage.Failed or AirSupportMissionStage.Expired)
                throw new ArgumentException("Terminal combat-support missions must not remain in an active checkpoint.");

            ValidateReservation(
                mission.MissionId,
                mission.SupportRequestId,
                request => request.Type is SupportRequestType.CloseAirSupport or SupportRequestType.Suppression);

            reservations.Add((mission.MissionId, mission.SupportRequestId));
        }

        foreach (var mission in AreaSupportMissions)
        {
            if (mission.MissionId == Guid.Empty)
                throw new ArgumentException("Area-support mission ID is required.");

            if (mission.Stage is AreaSupportMissionStage.Failed or AreaSupportMissionStage.Expired)
                throw new ArgumentException("Terminal area-support missions must not remain in an active checkpoint.");

            ValidateReservation(
                mission.MissionId,
                mission.SupportRequestId,
                request => request.Type is
                    SupportRequestType.Reconnaissance
                    or SupportRequestType.Logistics
                    or SupportRequestType.Patrol);

            reservations.Add((mission.MissionId, mission.SupportRequestId));
        }

        foreach (var mission in AirOperationMissions)
        {
            if (mission.MissionId == Guid.Empty)
                throw new ArgumentException("Air-operation mission ID is required.");

            if (mission.Stage is AirOperationMissionStage.Failed or AirOperationMissionStage.Expired)
                throw new ArgumentException("Terminal air-operation missions must not remain in an active checkpoint.");

            ValidateReservation(
                mission.MissionId,
                mission.SupportRequestId,
                request => request.Type is SupportRequestType.Escort or SupportRequestType.Intercept);

            reservations.Add((mission.MissionId, mission.SupportRequestId));
        }

        if (reservations.Select(item => item.MissionId).Distinct().Count() != reservations.Count)
            throw new ArgumentException("Active military missions must use unique mission IDs.");

        if (reservations.Select(item => item.RequestId).Distinct(StringComparer.Ordinal).Count() != reservations.Count)
            throw new ArgumentException("One support request cannot back multiple active missions.");

        var reservedRequests = World.SupportRequests
            .Where(request => request.Status == SupportRequestStatus.Reserved)
            .ToArray();

        if (reservedRequests.Length != reservations.Count)
            throw new ArgumentException("Every reserved support request must have exactly one recoverable active mission.");

        foreach (var request in reservedRequests)
        {
            if (request.ReservedMissionId is not Guid missionId
                || !reservations.Contains((missionId, request.RequestId)))
            {
                throw new ArgumentException("Reserved support request does not match an active mission checkpoint.");
            }
        }

        void ValidateReservation(
            Guid missionId,
            string requestId,
            Func<AirSupportRequest, bool> typePredicate)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

            var request = World.SupportRequests.SingleOrDefault(
                item => string.Equals(item.RequestId, requestId, StringComparison.Ordinal));

            if (request is null)
                throw new ArgumentException("Mission support request is missing from the world checkpoint.");

            if (request.Status != SupportRequestStatus.Reserved
                || request.ReservedMissionId != missionId)
            {
                throw new ArgumentException("Mission support request is not reserved by the matching mission.");
            }

            if (!typePredicate(request))
                throw new ArgumentException("Mission type does not match its support request.");
        }
    }
}

public sealed record ConflictCampaignStoreRecord(
    long Revision,
    ConflictCampaignCheckpoint Checkpoint)
{
    public void Validate()
    {
        if (Revision < 1)
            throw new ArgumentOutOfRangeException(nameof(Revision));

        ArgumentNullException.ThrowIfNull(Checkpoint);
        Checkpoint.Validate();
    }
}

public interface IConflictCampaignStore
{
    Task<ConflictCampaignStoreRecord?> LoadAsync(
        string campaignId,
        CancellationToken cancellationToken = default);

    Task<ConflictCampaignStoreRecord> SaveAsync(
        ConflictCampaignCheckpoint checkpoint,
        long? expectedRevision,
        CancellationToken cancellationToken = default);
}

public interface IConflictCampaignRecoverySource
{
    Task<ConflictCampaignStoreRecord?> LoadMostRecentlySavedAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ConflictCampaignConcurrencyException : InvalidOperationException
{
    public ConflictCampaignConcurrencyException(string message)
        : base(message)
    {
    }
}
