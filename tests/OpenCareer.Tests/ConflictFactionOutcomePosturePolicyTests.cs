using OpenCareer.Domain.Conflict;

namespace OpenCareer.Tests;

public sealed class ConflictFactionOutcomePosturePolicyTests
{
    [Theory]
    [InlineData(ConflictCampaignOutcome.Victory, ConflictSide.Friendly, ConflictFactionOperationalPosture.LogisticsFocused)]
    [InlineData(ConflictCampaignOutcome.Victory, ConflictSide.Hostile, ConflictFactionOperationalPosture.Defensive)]
    [InlineData(ConflictCampaignOutcome.Defeat, ConflictSide.Friendly, ConflictFactionOperationalPosture.Defensive)]
    [InlineData(ConflictCampaignOutcome.Defeat, ConflictSide.Hostile, ConflictFactionOperationalPosture.LogisticsFocused)]
    [InlineData(ConflictCampaignOutcome.Stalemate, ConflictSide.Friendly, ConflictFactionOperationalPosture.Defensive)]
    [InlineData(ConflictCampaignOutcome.Ceasefire, ConflictSide.Hostile, ConflictFactionOperationalPosture.LogisticsFocused)]
    public void ResolveMapsTerminalOutcomeDeterministically(
        ConflictCampaignOutcome outcome,
        ConflictSide side,
        ConflictFactionOperationalPosture expected)
    {
        ConflictFactionOperationalPosture resolved =
            ConflictFactionOutcomePosturePolicy.Resolve(
                ConflictFactionOperationalPosture.AirFocused,
                side,
                outcome);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void OngoingOutcomePreservesCurrentPosture()
    {
        ConflictFactionOperationalPosture resolved =
            ConflictFactionOutcomePosturePolicy.Resolve(
                ConflictFactionOperationalPosture.Aggressive,
                ConflictSide.Friendly,
                ConflictCampaignOutcome.Ongoing);

        Assert.Equal(ConflictFactionOperationalPosture.Aggressive, resolved);
    }

    [Fact]
    public void ResolveRejectsNeutralSide()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConflictFactionOutcomePosturePolicy.Resolve(
                ConflictFactionOperationalPosture.Defensive,
                ConflictSide.Neutral,
                ConflictCampaignOutcome.Stalemate));
    }
}
