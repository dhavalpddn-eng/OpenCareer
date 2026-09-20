using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;

namespace OpenCareer.Tests;

public sealed class CompletedMilitaryOperationPresentationTests
{
    private static readonly DateTimeOffset EndedAt =
        new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildUsesPersistedTerminalPosturesWithoutRecalculation()
    {
        ConflictCampaignHistoryEntry entry = Entry(
            ConflictFactionOperationalPosture.AirFocused,
            ConflictFactionOperationalPosture.LogisticsFocused);

        CompletedMilitaryOperationPresentation result =
            CompletedMilitaryOperationPresentationBuilder.Build(entry);

        Assert.Equal(entry.CampaignId, result.CampaignId);
        Assert.Equal(entry.Identity.OperationName, result.OperationName);
        Assert.Equal(entry.TheaterId, result.TheaterId);
        Assert.Equal(entry.Outcome, result.Outcome);
        Assert.Equal(entry.FinalPhase, result.FinalPhase);
        Assert.Equal(entry.FinalFriendlyControlAverage, result.FinalFriendlyControlAverage);
        Assert.Equal(entry.EndedAt, result.EndedAt);
        Assert.Equal("ALP", result.FriendlyFactionCode);
        Assert.Equal("Alpha Coalition", result.FriendlyFactionName);
        Assert.Equal(ConflictFactionOperationalPosture.AirFocused, result.FriendlyPosture);
        Assert.Equal("BRV", result.HostileFactionCode);
        Assert.Equal("Bravo Directorate", result.HostileFactionName);
        Assert.Equal(ConflictFactionOperationalPosture.LogisticsFocused, result.HostilePosture);
    }

    [Fact]
    public void BuildFallsBackToIdentityPosturesForLegacyHistory()
    {
        ConflictCampaignHistoryEntry entry = Entry(null, null);

        CompletedMilitaryOperationPresentation result =
            CompletedMilitaryOperationPresentationBuilder.Build(entry);

        Assert.Equal(entry.Identity.FriendlyFaction.Posture, result.FriendlyPosture);
        Assert.Equal(entry.Identity.HostileFaction.Posture, result.HostilePosture);
    }

    private static ConflictCampaignHistoryEntry Entry(
        ConflictFactionOperationalPosture? finalFriendlyPosture,
        ConflictFactionOperationalPosture? finalHostilePosture)
    {
        var identity = new ConflictCampaignIdentity(
            "Operation Test",
            new ConflictFactionIdentity(
                "Alpha Coalition",
                "ALP",
                ConflictSide.Friendly,
                ConflictFactionOperationalPosture.Defensive),
            new ConflictFactionIdentity(
                "Bravo Directorate",
                "BRV",
                ConflictSide.Hostile,
                ConflictFactionOperationalPosture.Aggressive));

        return new ConflictCampaignHistoryEntry(
            "campaign-test",
            "FICTIONAL-TEST",
            identity,
            ConflictCampaignOutcome.Victory,
            ConflictCampaignPhase.Secured,
            9,
            0.74,
            EndedAt,
            finalFriendlyPosture,
            finalHostilePosture);
    }
}
