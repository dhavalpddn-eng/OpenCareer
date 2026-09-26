namespace OpenCareer.Domain.Conflict;

/// <summary>
/// Resolves the deterministic faction posture carried into a completed-operation summary
/// or successor planning context. This is OpenCareer-only simulated conflict state and
/// never issues commands to the simulator.
/// </summary>
public static class ConflictFactionOutcomePosturePolicy
{
    public static ConflictFactionOperationalPosture Resolve(
        ConflictFactionOperationalPosture current,
        ConflictSide side,
        ConflictCampaignOutcome outcome)
    {
        if (!Enum.IsDefined(current))
            throw new ArgumentOutOfRangeException(nameof(current));
        if (side is not (ConflictSide.Friendly or ConflictSide.Hostile))
            throw new ArgumentOutOfRangeException(nameof(side));
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome));

        return outcome switch
        {
            ConflictCampaignOutcome.Ongoing => current,
            ConflictCampaignOutcome.Victory => side == ConflictSide.Friendly
                ? ConflictFactionOperationalPosture.LogisticsFocused
                : ConflictFactionOperationalPosture.Defensive,
            ConflictCampaignOutcome.Defeat => side == ConflictSide.Hostile
                ? ConflictFactionOperationalPosture.LogisticsFocused
                : ConflictFactionOperationalPosture.Defensive,
            ConflictCampaignOutcome.Stalemate => ConflictFactionOperationalPosture.Defensive,
            ConflictCampaignOutcome.Ceasefire => ConflictFactionOperationalPosture.LogisticsFocused,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
    }

    public static ConflictCampaignIdentity Apply(
        ConflictCampaignIdentity identity,
        ConflictCampaignOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();

        ConflictFactionOperationalPosture friendly = Resolve(
            identity.FriendlyFaction.Posture,
            ConflictSide.Friendly,
            outcome);
        ConflictFactionOperationalPosture hostile = Resolve(
            identity.HostileFaction.Posture,
            ConflictSide.Hostile,
            outcome);

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
