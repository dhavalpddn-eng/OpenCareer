namespace OpenCareer.Domain.Conflict;

/// <summary>
/// Applies deterministic campaign-phase posture changes to the persisted faction identity.
/// This policy changes only OpenCareer conflict state; it never issues simulator commands.
/// </summary>
public static class ConflictFactionPostureEvolution
{
    public static ConflictCampaignIdentity Apply(
        ConflictCampaignIdentity identity,
        ConflictCampaignPhase previousPhase,
        ConflictCampaignPhase nextPhase)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();

        ConflictFactionOperationalPosture friendly =
            ConflictFactionBehaviorPolicy.EvolveForPhase(
                identity.FriendlyFaction.Posture,
                ConflictSide.Friendly,
                previousPhase,
                nextPhase);

        ConflictFactionOperationalPosture hostile =
            ConflictFactionBehaviorPolicy.EvolveForPhase(
                identity.HostileFaction.Posture,
                ConflictSide.Hostile,
                previousPhase,
                nextPhase);

        if (friendly == identity.FriendlyFaction.Posture
            && hostile == identity.HostileFaction.Posture)
        {
            return identity;
        }

        var evolved = identity with
        {
            FriendlyFaction = identity.FriendlyFaction with { Posture = friendly },
            HostileFaction = identity.HostileFaction with { Posture = hostile }
        };

        evolved.Validate();
        return evolved;
    }
}
