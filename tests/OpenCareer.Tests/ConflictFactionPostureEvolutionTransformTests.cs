using OpenCareer.Domain.Conflict;

namespace OpenCareer.Tests;

public sealed class ConflictFactionPostureEvolutionTransformTests
{
    [Fact]
    public void ApplyEvolvesBothBelligerentsWithoutChangingOperationIdentity()
    {
        ConflictCampaignIdentity identity = Identity(
            ConflictFactionOperationalPosture.AirFocused,
            ConflictFactionOperationalPosture.LogisticsFocused);

        ConflictCampaignIdentity evolved = ConflictFactionPostureEvolution.Apply(
            identity,
            ConflictCampaignPhase.Contested,
            ConflictCampaignPhase.FriendlyPressure);

        Assert.Equal(identity.OperationId, evolved.OperationId);
        Assert.Equal(identity.OperationName, evolved.OperationName);
        Assert.Equal(identity.FriendlyFaction.FactionId, evolved.FriendlyFaction.FactionId);
        Assert.Equal(identity.HostileFaction.FactionId, evolved.HostileFaction.FactionId);
        Assert.Equal(ConflictFactionOperationalPosture.Aggressive, evolved.FriendlyFaction.Posture);
        Assert.Equal(ConflictFactionOperationalPosture.Defensive, evolved.HostileFaction.Posture);
    }

    [Fact]
    public void ApplyPreservesIdentityWhenPhaseDoesNotRequirePostureChange()
    {
        ConflictCampaignIdentity identity = Identity(
            ConflictFactionOperationalPosture.AirFocused,
            ConflictFactionOperationalPosture.Defensive);

        ConflictCampaignIdentity evolved = ConflictFactionPostureEvolution.Apply(
            identity,
            ConflictCampaignPhase.FriendlyPressure,
            ConflictCampaignPhase.Contested);

        Assert.Same(identity, evolved);
    }

    [Fact]
    public void ApplyIsIdempotentForRepeatedPhaseEvaluation()
    {
        ConflictCampaignIdentity identity = Identity(
            ConflictFactionOperationalPosture.Defensive,
            ConflictFactionOperationalPosture.AirFocused);

        ConflictCampaignIdentity first = ConflictFactionPostureEvolution.Apply(
            identity,
            ConflictCampaignPhase.Contested,
            ConflictCampaignPhase.HostileSecured);

        ConflictCampaignIdentity second = ConflictFactionPostureEvolution.Apply(
            first,
            ConflictCampaignPhase.HostileSecured,
            ConflictCampaignPhase.HostileSecured);

        Assert.Equal(first, second);
        Assert.Equal(ConflictFactionOperationalPosture.Defensive, first.FriendlyFaction.Posture);
        Assert.Equal(ConflictFactionOperationalPosture.LogisticsFocused, first.HostileFaction.Posture);
    }

    private static ConflictCampaignIdentity Identity(
        ConflictFactionOperationalPosture friendly,
        ConflictFactionOperationalPosture hostile) =>
        new(
            "operation:test",
            "Operation Test",
            new ConflictFactionIdentity(
                "faction:friendly",
                "Friendly Coalition",
                "FCO",
                ConflictSide.Friendly)
            {
                Posture = friendly
            },
            new ConflictFactionIdentity(
                "faction:hostile",
                "Hostile Compact",
                "HCO",
                ConflictSide.Hostile)
            {
                Posture = hostile
            });
}
