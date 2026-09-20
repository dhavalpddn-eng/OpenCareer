using OpenCareer.Domain.Conflict;

namespace OpenCareer.Tests;

public sealed class ConflictFactionPostureEvolutionTests
{
    [Theory]
    [InlineData(ConflictSide.Friendly, ConflictCampaignPhase.FriendlyPressure, ConflictFactionOperationalPosture.Aggressive)]
    [InlineData(ConflictSide.Hostile, ConflictCampaignPhase.HostilePressure, ConflictFactionOperationalPosture.Aggressive)]
    [InlineData(ConflictSide.Friendly, ConflictCampaignPhase.HostilePressure, ConflictFactionOperationalPosture.Defensive)]
    [InlineData(ConflictSide.Hostile, ConflictCampaignPhase.FriendlyPressure, ConflictFactionOperationalPosture.Defensive)]
    [InlineData(ConflictSide.Friendly, ConflictCampaignPhase.FriendlySecured, ConflictFactionOperationalPosture.LogisticsFocused)]
    [InlineData(ConflictSide.Hostile, ConflictCampaignPhase.HostileSecured, ConflictFactionOperationalPosture.LogisticsFocused)]
    public void PhaseTransitionSelectsDeterministicPosture(
        ConflictSide side,
        ConflictCampaignPhase nextPhase,
        ConflictFactionOperationalPosture expected)
    {
        ConflictFactionOperationalPosture result = ConflictFactionBehaviorPolicy.EvolveForPhase(
            ConflictFactionOperationalPosture.AirFocused,
            side,
            ConflictCampaignPhase.Contested,
            nextPhase);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void UnchangedOrContestedPhasePreservesExistingPosture()
    {
        Assert.Equal(
            ConflictFactionOperationalPosture.AirFocused,
            ConflictFactionBehaviorPolicy.EvolveForPhase(
                ConflictFactionOperationalPosture.AirFocused,
                ConflictSide.Friendly,
                ConflictCampaignPhase.FriendlyPressure,
                ConflictCampaignPhase.FriendlyPressure));

        Assert.Equal(
            ConflictFactionOperationalPosture.AirFocused,
            ConflictFactionBehaviorPolicy.EvolveForPhase(
                ConflictFactionOperationalPosture.AirFocused,
                ConflictSide.Friendly,
                ConflictCampaignPhase.FriendlyPressure,
                ConflictCampaignPhase.Contested));
    }

    [Fact]
    public void NeutralFactionCannotReceiveOperationalPostureEvolution()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConflictFactionBehaviorPolicy.EvolveForPhase(
                ConflictFactionOperationalPosture.Defensive,
                ConflictSide.Neutral,
                ConflictCampaignPhase.Contested,
                ConflictCampaignPhase.FriendlyPressure));
    }
}
