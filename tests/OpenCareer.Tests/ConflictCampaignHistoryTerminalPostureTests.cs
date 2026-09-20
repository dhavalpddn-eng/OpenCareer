using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class ConflictCampaignHistoryTerminalPostureTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ConflictCampaignOutcome.Victory, ConflictFactionOperationalPosture.LogisticsFocused, ConflictFactionOperationalPosture.Defensive)]
    [InlineData(ConflictCampaignOutcome.Defeat, ConflictFactionOperationalPosture.Defensive, ConflictFactionOperationalPosture.LogisticsFocused)]
    [InlineData(ConflictCampaignOutcome.Stalemate, ConflictFactionOperationalPosture.Defensive, ConflictFactionOperationalPosture.Defensive)]
    [InlineData(ConflictCampaignOutcome.Ceasefire, ConflictFactionOperationalPosture.LogisticsFocused, ConflictFactionOperationalPosture.LogisticsFocused)]
    public void ArchiveFreezesDeterministicTerminalPostures(
        ConflictCampaignOutcome outcome,
        ConflictFactionOperationalPosture expectedFriendly,
        ConflictFactionOperationalPosture expectedHostile)
    {
        ConflictCampaignCheckpoint checkpoint = TerminalCheckpoint(outcome);

        ConflictCampaignHistoryEntry entry =
            ConflictCampaignHistoryEntry.FromCheckpoint(checkpoint);

        Assert.Equal(expectedFriendly, entry.FinalFriendlyPosture);
        Assert.Equal(expectedHostile, entry.FinalHostilePosture);
        Assert.Equal(checkpoint.CampaignState.Identity, entry.Identity);
    }

    [Fact]
    public void RepeatedArchiveProjectionDoesNotDriftTerminalPostures()
    {
        ConflictCampaignCheckpoint checkpoint =
            TerminalCheckpoint(ConflictCampaignOutcome.Victory);

        ConflictCampaignHistoryEntry first =
            ConflictCampaignHistoryEntry.FromCheckpoint(checkpoint);
        ConflictCampaignHistoryEntry second =
            ConflictCampaignHistoryEntry.FromCheckpoint(checkpoint);

        Assert.Equal(first, second);
    }

    private static ConflictCampaignCheckpoint TerminalCheckpoint(
        ConflictCampaignOutcome outcome)
    {
        var template = new ConflictTheaterTemplate(
            "FICTIONAL-HISTORY-POSTURE",
            new GeoPoint(34.5, -101.2),
            RadiusNauticalMiles: 90,
            FriendlyGroundUnits: 5,
            HostileGroundUnits: 5,
            FriendlyAirUnits: 2,
            HostileAirUnits: 2);

        ConflictWorldState world =
            ConflictTheaterGenerator.Generate(
                template,
                theaterSeed: 0xC0FFEEUL,
                Epoch);

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "history-posture",
                world) with
            {
                Outcome = outcome,
                Objectives = Array.Empty<ConflictStrategicObjective>()
            };

        return ConflictCampaignCheckpoint.Create(
            "history-posture",
            world,
            MilitaryCareerState.Civilian,
            PlayerCombatState.Undamaged,
            combatSupportMissions: null,
            areaSupportMissions: null,
            airOperationMissions: null,
            savedAt: Epoch,
            campaign);
    }
}
