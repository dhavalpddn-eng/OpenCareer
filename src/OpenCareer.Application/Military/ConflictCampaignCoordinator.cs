using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Application.Military;

public sealed class ConflictCampaignCoordinator
{
    private readonly IConflictCampaignStore _store;

    public ConflictCampaignCoordinator(IConflictCampaignStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ConflictCampaignStoreRecord> CreateAsync(
        string campaignId,
        ConflictTheaterTemplate template,
        ulong theaterSeed,
        DateTimeOffset createdAt,
        MilitaryCareerState militaryCareer,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(militaryCareer);

        militaryCareer.Validate();

        ConflictWorldState world = ConflictTheaterGenerator.Generate(
            template,
            theaterSeed,
            createdAt);

        ConflictCampaignState campaignState =
            ConflictCampaignDirector.Create(campaignId, world);

        var checkpoint = ConflictCampaignCheckpoint.Create(
            campaignId,
            world,
            militaryCareer,
            PlayerCombatState.Undamaged,
            combatSupportMissions: null,
            areaSupportMissions: null,
            airOperationMissions: null,
            savedAt: createdAt,
            campaignState);

        return await _store
            .SaveAsync(
                checkpoint,
                expectedRevision: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ConflictCampaignStoreRecord> CreateSuccessorAsync(
        ConflictCampaignStoreRecord completed,
        string campaignId,
        ConflictTheaterTemplate template,
        ulong theaterSeed,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);
        ArgumentNullException.ThrowIfNull(template);
        completed.Validate();

        if (!completed.Checkpoint.CampaignState.IsTerminal)
        {
            throw new InvalidOperationException(
                "A successor military campaign can only begin after the current campaign has ended.");
        }

        int activeMissionCount =
            completed.Checkpoint.CombatSupportMissions.Length
            + completed.Checkpoint.AreaSupportMissions.Length
            + completed.Checkpoint.AirOperationMissions.Length;

        if (activeMissionCount != 0)
        {
            throw new InvalidOperationException(
                "A successor military campaign cannot begin while an operation is still active.");
        }

        if (string.Equals(
            completed.Checkpoint.CampaignId,
            campaignId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A successor military campaign requires a new campaign ID.");
        }

        if (completed.Checkpoint.History.Any(entry =>
            string.Equals(
                entry.CampaignId,
                campaignId,
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "A successor military campaign cannot reuse a historical campaign ID.");
        }

        if (createdAt < completed.Checkpoint.SavedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(createdAt),
                "A successor military campaign cannot begin before the completed campaign checkpoint.");
        }

        ConflictCampaignHistoryEntry completedEntry =
            ConflictCampaignHistoryEntry.FromCheckpoint(
                completed.Checkpoint);

        ConflictCampaignHistoryEntry[] history =
            completed.Checkpoint.History
                .Append(completedEntry)
                .ToArray();

        ConflictWorldState world =
            ConflictTheaterGenerator.Generate(
                template,
                theaterSeed,
                createdAt);

        ConflictCampaignState campaignState =
            ConflictCampaignDirector.Create(
                campaignId,
                world);

        ConflictCampaignCheckpoint checkpoint =
            ConflictCampaignCheckpoint.Create(
                campaignId,
                world,
                completed.Checkpoint.MilitaryCareer,
                completed.Checkpoint.PlayerCombatState,
                combatSupportMissions: null,
                areaSupportMissions: null,
                airOperationMissions: null,
                savedAt: createdAt,
                campaignState,
                history);

        return await _store
            .SaveAsync(
                checkpoint,
                expectedRevision: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ConflictCampaignStoreRecord> CreateSuccessorAsync(
        ConflictCampaignStoreRecord completed,
        ConflictCampaignSuccessorOffer offer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        offer.Validate();

        return CreateSuccessorAsync(
            completed,
            offer.CampaignId,
            offer.Theater,
            offer.TheaterSeed,
            offer.AvailableAt,
            cancellationToken);
    }

    public Task<ConflictCampaignStoreRecord?> LoadAsync(
        string campaignId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);
        return _store.LoadAsync(campaignId, cancellationToken);
    }

    public async Task<ConflictCampaignStoreRecord> AdvanceAsync(
        ConflictCampaignStoreRecord current,
        DateTimeOffset through,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        current.Validate();

        if (savedAt < through)
            throw new ArgumentOutOfRangeException(
                nameof(savedAt),
                "Checkpoint save time cannot precede the requested campaign time.");

        ConflictWorldState world = ConflictWorldEngine.Advance(
            current.Checkpoint.World,
            through);

        ConflictCampaignCycleResult cycle =
            ConflictCampaignCycleEngine.ApplyCycle(
                world,
                current.Checkpoint.CampaignState);

        world = cycle.World;

        ConflictCampaignState campaignBeforeAdvance =
            current.Checkpoint.CampaignState with
            {
                FriendlyReplacementReserve =
                    cycle.FriendlyReplacementReserve,
                HostileReplacementReserve =
                    cycle.HostileReplacementReserve
            };

        ConflictCampaignState campaignState =
            ConflictCampaignDirector.Advance(
                campaignBeforeAdvance,
                world);

        var checkpoint = current.Checkpoint with
        {
            World = world,
            CampaignState = campaignState,
            SavedAt = savedAt
        };

        checkpoint.Validate();

        return await _store
            .SaveAsync(
                checkpoint,
                current.Revision,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ConflictCampaignStoreRecord> SaveMutationAsync(
        ConflictCampaignStoreRecord current,
        ConflictCampaignCheckpoint updatedCheckpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(updatedCheckpoint);
        current.Validate();
        updatedCheckpoint.Validate();

        if (!string.Equals(
            current.Checkpoint.CampaignId,
            updatedCheckpoint.CampaignId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Cannot replace a conflict campaign record with another campaign.");
        }

        if (updatedCheckpoint.SavedAt < current.Checkpoint.SavedAt)
        {
            throw new InvalidOperationException(
                "Conflict campaign checkpoints cannot move save time backwards.");
        }

        return await _store
            .SaveAsync(
                updatedCheckpoint,
                current.Revision,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
