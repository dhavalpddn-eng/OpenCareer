using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Application.Military;

public sealed record MilitaryMissionAcceptanceResult(
    ConflictCampaignStoreRecord Record,
    AcceptedMilitaryOperation Operation);

public sealed class MilitaryCampaignMissionService
{
    private readonly MilitaryDispatchService _dispatch;
    private readonly ConflictOperationsService _operations;
    private readonly ConflictCampaignCoordinator _campaigns;
    private readonly PersistedOperationConsequenceCoordinator _consequences;
    private readonly IOperationConsequenceHistorySource _consequenceHistory;

    public MilitaryCampaignMissionService(
        MilitaryDispatchService dispatch,
        ConflictOperationsService operations,
        ConflictCampaignCoordinator campaigns,
        PersistedOperationConsequenceCoordinator consequences,
        IOperationConsequenceHistorySource consequenceHistory)
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _campaigns = campaigns ?? throw new ArgumentNullException(nameof(campaigns));
        _consequences = consequences ?? throw new ArgumentNullException(nameof(consequences));
        _consequenceHistory = consequenceHistory ?? throw new ArgumentNullException(nameof(consequenceHistory));
    }

    public async Task<MilitaryMissionAcceptanceResult> AcceptAsync(
        ConflictCampaignStoreRecord current,
        string requestId,
        Guid missionId,
        DateTimeOffset acceptedAt,
        AircraftCapabilityProfile aircraft,
        bool aircraftAssignedForOperation,
        CancellationToken cancellationToken = default)
    {
        current.Validate();

        if (current.Checkpoint.CampaignState.IsTerminal)
        {
            throw new InvalidOperationException(
                "This military campaign has ended and cannot accept new operations.");
        }

        EnsureNoActiveMission(current.Checkpoint);

        if (acceptedAt < current.Checkpoint.SavedAt)
            throw new ArgumentOutOfRangeException(nameof(acceptedAt));

        AcceptedMilitaryOperation accepted = _dispatch.Accept(
            current.Checkpoint.World,
            requestId,
            missionId,
            acceptedAt,
            current.Checkpoint.MilitaryCareer,
            current.Checkpoint.PlayerCombatState,
            aircraft,
            aircraftAssignedForOperation);

        ConflictCampaignCheckpoint checkpoint = AddMission(
            current.Checkpoint,
            accepted,
            acceptedAt);

        ConflictCampaignStoreRecord saved = await _campaigns
            .SaveMutationAsync(current, checkpoint, cancellationToken)
            .ConfigureAwait(false);

        return new MilitaryMissionAcceptanceResult(saved, accepted);
    }

    public Task<ConflictCampaignStoreRecord> SaveCombatProgressAsync(
        ConflictCampaignStoreRecord current,
        ConflictWorldState updatedWorld,
        AirSupportMission mission,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        EnsureActiveMission(
            current.Checkpoint.CombatSupportMissions,
            mission.MissionId,
            item => item.MissionId);

        return SaveProgressAsync(
            current,
            updatedWorld,
            mission.MissionId,
            savedAt,
            checkpoint => checkpoint with
            {
                CombatSupportMissions = new[] { mission }
            },
            cancellationToken);
    }

    public Task<ConflictCampaignStoreRecord> SaveAreaProgressAsync(
        ConflictCampaignStoreRecord current,
        ConflictWorldState updatedWorld,
        AreaSupportMission mission,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        EnsureActiveMission(
            current.Checkpoint.AreaSupportMissions,
            mission.MissionId,
            item => item.MissionId);

        return SaveProgressAsync(
            current,
            updatedWorld,
            mission.MissionId,
            savedAt,
            checkpoint => checkpoint with
            {
                AreaSupportMissions = new[] { mission }
            },
            cancellationToken);
    }

    public Task<ConflictCampaignStoreRecord> SaveAirOperationProgressAsync(
        ConflictCampaignStoreRecord current,
        ConflictWorldState updatedWorld,
        AirOperationMission mission,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        EnsureActiveMission(
            current.Checkpoint.AirOperationMissions,
            mission.MissionId,
            item => item.MissionId);

        return SaveProgressAsync(
            current,
            updatedWorld,
            mission.MissionId,
            savedAt,
            checkpoint => checkpoint with
            {
                AirOperationMissions = new[] { mission }
            },
            cancellationToken);
    }

    public async Task<ConflictCampaignStoreRecord> CompleteAsync(
        ConflictCampaignStoreRecord current,
        Guid missionId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        current.Validate();

        if (completedAt < current.Checkpoint.SavedAt)
            throw new ArgumentOutOfRangeException(nameof(completedAt));

        AirSupportRequest request = FindReservedRequest(
            current.Checkpoint.World,
            missionId);

        DateTimeOffset acceptedAt =
            FindMissionAcceptedAt(
                current.Checkpoint,
                missionId);

        ConflictWorldState world;
        ConflictCampaignCheckpoint checkpoint = current.Checkpoint;

        AirSupportMission? combat = checkpoint.CombatSupportMissions
            .SingleOrDefault(item => item.MissionId == missionId);

        if (combat is not null)
        {
            world = _operations.CompleteCombatSupportMission(
                checkpoint.World,
                combat,
                completedAt);
            checkpoint = checkpoint with
            {
                CombatSupportMissions = Array.Empty<AirSupportMission>()
            };
        }
        else
        {
            AreaSupportMission? area = checkpoint.AreaSupportMissions
                .SingleOrDefault(item => item.MissionId == missionId);

            if (area is not null)
            {
                CompletedAreaSupportMission result =
                    _operations.CompleteAreaSupportMission(
                        checkpoint.World,
                        area,
                        completedAt);
                world = result.World;
                checkpoint = checkpoint with
                {
                    AreaSupportMissions = Array.Empty<AreaSupportMission>()
                };
            }
            else
            {
                AirOperationMission air = checkpoint.AirOperationMissions
                    .SingleOrDefault(item => item.MissionId == missionId)
                    ?? throw new InvalidOperationException(
                        "Active military mission was not found.");

                world = _operations.CompleteAirOperationMission(
                    checkpoint.World,
                    air,
                    completedAt);
                checkpoint = checkpoint with
                {
                    AirOperationMissions = Array.Empty<AirOperationMission>()
                };
            }
        }

        string sectorId =
            FindConsequenceSectorId(
                world,
                request);

        OperationConsequenceStoreRecord consequence =
            await ResolveConsequencesAsync(
                    checkpoint,
                    request,
                    missionId,
                    MissionExecutionResult.Completed,
                    objectivesCompleted: 1,
                    acceptedAt,
                    completedAt,
                    sectorId,
                    cancellationToken)
                .ConfigureAwait(false);

        world = ApplyTerritoryControlDelta(
            world,
            sectorId,
            consequence.Result.TerritoryPressure.FriendlyControlDelta);

        checkpoint = checkpoint with
        {
            World = world,
            CampaignState =
                ConflictCampaignDirector.Advance(
                    checkpoint.CampaignState,
                    world),
            MilitaryCareer = consequence.Result.MilitaryCareer,
            SavedAt = completedAt
        };

        return await _campaigns
            .SaveMutationAsync(current, checkpoint, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(ConflictCampaignStoreRecord Record, ThreatEngagementResult Result)> ResolveThreatAsync(
        ConflictCampaignStoreRecord current,
        ThreatEngagementRequest request,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        current.Validate();

        if (savedAt < current.Checkpoint.SavedAt)
            throw new ArgumentOutOfRangeException(nameof(savedAt));

        ThreatResolution resolution = _operations.ResolveThreat(
            current.Checkpoint.World,
            request,
            current.Checkpoint.PlayerCombatState);

        if (resolution.Result.Outcome == ThreatEngagementOutcome.DuplicateIgnored)
            return (current, resolution.Result);

        var checkpoint = current.Checkpoint with
        {
            World = resolution.State,
            PlayerCombatState = resolution.Result.PlayerState,
            SavedAt = savedAt
        };

        ConflictCampaignStoreRecord saved = await _campaigns
            .SaveMutationAsync(current, checkpoint, cancellationToken)
            .ConfigureAwait(false);

        return (saved, resolution.Result);
    }

    public async Task<ConflictCampaignStoreRecord> FailAsync(
        ConflictCampaignStoreRecord current,
        Guid missionId,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default)
    {
        current.Validate();

        if (failedAt < current.Checkpoint.SavedAt)
            throw new ArgumentOutOfRangeException(nameof(failedAt));

        AirSupportRequest request = FindReservedRequest(
            current.Checkpoint.World,
            missionId);

        DateTimeOffset acceptedAt =
            FindMissionAcceptedAt(
                current.Checkpoint,
                missionId);

        ConflictWorldState world =
            _operations.FailReservedSupportMission(
                current.Checkpoint.World,
                request.RequestId,
                missionId,
                failedAt);

        string sectorId =
            FindConsequenceSectorId(
                world,
                request);

        OperationConsequenceStoreRecord consequence =
            await ResolveConsequencesAsync(
                    current.Checkpoint,
                    request,
                    missionId,
                    MissionExecutionResult.Failed,
                    objectivesCompleted: 0,
                    acceptedAt,
                    failedAt,
                    sectorId,
                    cancellationToken)
                .ConfigureAwait(false);

        world = ApplyTerritoryControlDelta(
            world,
            sectorId,
            consequence.Result.TerritoryPressure.FriendlyControlDelta);

        var checkpoint = current.Checkpoint with
        {
            World = world,
            CampaignState =
                Math.Abs(
                    consequence.Result.TerritoryPressure.FriendlyControlDelta)
                    > 0.0000001
                    ? ConflictCampaignDirector.Advance(
                        current.Checkpoint.CampaignState,
                        world)
                    : current.Checkpoint.CampaignState,
            MilitaryCareer = consequence.Result.MilitaryCareer,
            CombatSupportMissions = current.Checkpoint.CombatSupportMissions
                .Where(item => item.MissionId != missionId)
                .ToArray(),
            AreaSupportMissions = current.Checkpoint.AreaSupportMissions
                .Where(item => item.MissionId != missionId)
                .ToArray(),
            AirOperationMissions = current.Checkpoint.AirOperationMissions
                .Where(item => item.MissionId != missionId)
                .ToArray(),
            SavedAt = failedAt
        };

        return await _campaigns
            .SaveMutationAsync(current, checkpoint, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<OperationConsequenceStoreRecord> ResolveConsequencesAsync(
        ConflictCampaignCheckpoint checkpoint,
        AirSupportRequest request,
        Guid missionId,
        MissionExecutionResult missionResult,
        int objectivesCompleted,
        DateTimeOffset acceptedAt,
        DateTimeOffset resolvedAt,
        string sectorId,
        CancellationToken cancellationToken)
    {
        OperationResolutionKey key =
            OperationResolutionKey.Create(
                request.RequestId,
                missionId);

        OperationConsequenceStoreRecord? existing =
            await _consequences
                .LoadAsync(
                    key,
                    cancellationToken)
                .ConfigureAwait(false);

        if (existing is not null)
            return existing;

        OperationConsequenceState state =
            await BuildConsequenceStateAsync(
                    checkpoint,
                    sectorId,
                    cancellationToken)
                .ConfigureAwait(false);

        var input = new OperationResolutionInput(
            missionId,
            request.RequestId,
            missionResult,
            objectivesCompleted,
            ObjectivesRequired: 1,
            AircraftSurvived: true,
            CrewSurvived: true,
            MissionDuration: resolvedAt - acceptedAt,
            ResolvedAt: resolvedAt);

        return await _consequences
            .ApplyAsync(
                input,
                state,
                resolvedAt,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<OperationConsequenceState> BuildConsequenceStateAsync(
        ConflictCampaignCheckpoint checkpoint,
        string sectorId,
        CancellationToken cancellationToken)
    {
        ConflictCampaignIdentity identity =
            checkpoint.CampaignState.Identity
            ?? ConflictCampaignIdentityGenerator.Create(
                checkpoint.CampaignId,
                checkpoint.World);

        OperationConsequenceStoreRecord? latestCampaign =
            await _consequenceHistory
                .LoadLatestForCampaignAsync(
                    checkpoint.CampaignId,
                    cancellationToken)
                .ConfigureAwait(false);

        OperationConsequenceStoreRecord? latestSector =
            await _consequenceHistory
                .LoadLatestForSectorAsync(
                    checkpoint.CampaignId,
                    sectorId,
                    cancellationToken)
                .ConfigureAwait(false);

        return new OperationConsequenceState(
            new FactionInfluenceState(
                identity.FriendlyFaction.FactionId,
                identity.HostileFaction.FactionId,
                latestCampaign?.Result.FactionInfluence.FriendlyInfluence
                    ?? 0.50,
                latestCampaign?.Result.FactionInfluence.HostileInfluence
                    ?? 0.50),
            new CampaignProgressState(
                checkpoint.CampaignId,
                latestCampaign?.Result.CampaignProgress.FriendlyProgress
                    ?? 0.50),
            new TerritoryPressureState(
                sectorId,
                latestSector?.Result.TerritoryPressure.State
                    .AccumulatedFriendlyPressure
                    ?? 0),
            checkpoint.MilitaryCareer,
            new ConflictResourceState(
                checkpoint.CampaignId,
                latestCampaign?.Result.Resources.FriendlySupply
                    ?? 0.50,
                latestCampaign?.Result.Resources.FriendlyOperationalReadiness
                    ?? 0.50,
                latestCampaign?.Result.Resources.HostileSupply
                    ?? 0.50));
    }

    private static string FindConsequenceSectorId(
        ConflictWorldState world,
        AirSupportRequest request) =>
        world.Sectors
            .OrderBy(sector =>
                ConflictGeometry.DistanceNauticalMiles(
                    request.TargetPosition,
                    sector.Center))
            .ThenBy(sector => sector.SectorId, StringComparer.Ordinal)
            .Select(sector => sector.SectorId)
            .FirstOrDefault()
        ?? $"theater:{world.TheaterId}";

    private static ConflictWorldState ApplyTerritoryControlDelta(
        ConflictWorldState world,
        string sectorId,
        double friendlyControlDelta)
    {
        if (Math.Abs(friendlyControlDelta) <= 0.0000001)
            return world;

        ConflictSectorState[] sectors = world.Sectors
            .Select(sector =>
                string.Equals(
                    sector.SectorId,
                    sectorId,
                    StringComparison.Ordinal)
                    ? sector with
                    {
                        FriendlyControl = Math.Clamp(
                            sector.FriendlyControl + friendlyControlDelta,
                            0,
                            1)
                    }
                    : sector)
            .ToArray();

        return world with { Sectors = sectors };
    }

    private static DateTimeOffset FindMissionAcceptedAt(
        ConflictCampaignCheckpoint checkpoint,
        Guid missionId)
    {
        AirSupportMission? combat =
            checkpoint.CombatSupportMissions
                .SingleOrDefault(item => item.MissionId == missionId);

        if (combat is not null)
            return combat.AcceptedAt;

        AreaSupportMission? area =
            checkpoint.AreaSupportMissions
                .SingleOrDefault(item => item.MissionId == missionId);

        if (area is not null)
            return area.AcceptedAt;

        AirOperationMission? air =
            checkpoint.AirOperationMissions
                .SingleOrDefault(item => item.MissionId == missionId);

        return air?.AcceptedAt
            ?? throw new InvalidOperationException(
                "Active military mission was not found.");
    }

    private async Task<ConflictCampaignStoreRecord> SaveProgressAsync(
        ConflictCampaignStoreRecord current,
        ConflictWorldState updatedWorld,
        Guid missionId,
        DateTimeOffset savedAt,
        Func<ConflictCampaignCheckpoint, ConflictCampaignCheckpoint> missionUpdate,
        CancellationToken cancellationToken)
    {
        current.Validate();
        ConflictValidation.Validate(updatedWorld);

        if (savedAt < current.Checkpoint.SavedAt)
            throw new ArgumentOutOfRangeException(nameof(savedAt));

        if (updatedWorld.UpdatedAt < current.Checkpoint.World.UpdatedAt)
            throw new InvalidOperationException("Conflict world time cannot move backwards.");

        if (!string.Equals(
            updatedWorld.TheaterId,
            current.Checkpoint.World.TheaterId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Mission progress cannot move to another conflict theater.");
        }

        FindReservedRequest(updatedWorld, missionId);

        ConflictCampaignState strategic =
            updatedWorld.UpdatedAt == current.Checkpoint.CampaignState.UpdatedAt
                ? current.Checkpoint.CampaignState
                : ConflictCampaignDirector.Advance(
                    current.Checkpoint.CampaignState,
                    updatedWorld,
                    evaluateCampaignOutcome: false);

        ConflictCampaignCheckpoint checkpoint = missionUpdate(
            current.Checkpoint with
            {
                World = updatedWorld,
                CampaignState = strategic,
                SavedAt = savedAt
            });

        checkpoint.Validate();

        return await _campaigns
            .SaveMutationAsync(current, checkpoint, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureActiveMission<T>(
        IReadOnlyList<T> missions,
        Guid missionId,
        Func<T, Guid> getMissionId)
    {
        ArgumentNullException.ThrowIfNull(missions);
        ArgumentNullException.ThrowIfNull(getMissionId);

        if (missionId == Guid.Empty
            || !missions.Any(item => getMissionId(item) == missionId))
        {
            throw new InvalidOperationException(
                "Active military mission was not found.");
        }
    }

    private static ConflictCampaignCheckpoint AddMission(
        ConflictCampaignCheckpoint checkpoint,
        AcceptedMilitaryOperation accepted,
        DateTimeOffset savedAt) =>
        accepted.Type switch
        {
            SupportRequestType.CloseAirSupport
                or SupportRequestType.Suppression =>
                checkpoint with
                {
                    World = accepted.World,
                    CombatSupportMissions =
                        new[] { accepted.CombatSupportMission! },
                    SavedAt = savedAt
                },

            SupportRequestType.Reconnaissance
                or SupportRequestType.Logistics
                or SupportRequestType.Patrol =>
                checkpoint with
                {
                    World = accepted.World,
                    AreaSupportMissions =
                        new[] { accepted.AreaSupportMission! },
                    SavedAt = savedAt
                },

            SupportRequestType.Escort
                or SupportRequestType.Intercept =>
                checkpoint with
                {
                    World = accepted.World,
                    AirOperationMissions =
                        new[] { accepted.AirOperationMission! },
                    SavedAt = savedAt
                },

            _ => throw new NotSupportedException(
                $"Military mission type {accepted.Type} is not supported.")
        };

    private static AirSupportRequest FindReservedRequest(
        ConflictWorldState world,
        Guid missionId) =>
        world.SupportRequests.SingleOrDefault(
            request =>
                request.Status == SupportRequestStatus.Reserved
                && request.ReservedMissionId == missionId)
        ?? throw new InvalidOperationException(
            "Reserved support request for the active mission was not found.");

    private static void EnsureNoActiveMission(
        ConflictCampaignCheckpoint checkpoint)
    {
        int active =
            checkpoint.CombatSupportMissions.Length
            + checkpoint.AreaSupportMissions.Length
            + checkpoint.AirOperationMissions.Length;

        if (active != 0)
            throw new InvalidOperationException(
                "The player already has an active military operation.");
    }
}
