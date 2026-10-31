using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;

namespace OpenCareer.Tests;

public sealed class CompletedMilitaryOperationDetailProjectionTests
{
    [Fact]
    public void BuildCarriesPersistedTerminalFactsIntoDrilldownWithoutRecalculation()
    {
        var identity = new ConflictCampaignIdentity(
            "operation:detail",
            "Operation Detail",
            new ConflictFactionIdentity(
                "faction:alpha",
                "Alpha Coalition",
                "ALP",
                ConflictSide.Friendly)
            {
                Posture = ConflictFactionOperationalPosture.Defensive
            },
            new ConflictFactionIdentity(
                "faction:bravo",
                "Bravo Directorate",
                "BRV",
                ConflictSide.Hostile)
            {
                Posture = ConflictFactionOperationalPosture.Aggressive
            });
        var endedAt = new DateTimeOffset(2026, 9, 20, 17, 0, 0, TimeSpan.Zero);
        var entry = new ConflictCampaignHistoryEntry(
            "campaign-detail",
            "FICTIONAL-DETAIL",
            identity,
            ConflictCampaignOutcome.Ceasefire,
            ConflictCampaignPhase.HostilePressure,
            12,
            0.37,
            endedAt,
            ConflictFactionOperationalPosture.LogisticsFocused,
            ConflictFactionOperationalPosture.AirFocused);

        CompletedMilitaryOperationDetailProjection result =
            CompletedMilitaryOperationDetailProjectionBuilder.Build(entry);

        Assert.Equal(entry.CampaignId, result.CampaignId);
        Assert.Equal(identity.OperationName, result.OperationName);
        Assert.Equal(entry.TheaterId, result.TheaterId);
        Assert.Equal(entry.Outcome, result.Outcome);
        Assert.Equal(entry.FinalPhase, result.FinalPhase);
        Assert.Equal(entry.FinalFriendlyControlAverage, result.FinalFriendlyControlAverage);
        Assert.Equal(endedAt, result.EndedAt);
        Assert.Equal("ALP", result.FriendlyFactionCode);
        Assert.Equal("Alpha Coalition", result.FriendlyFactionName);
        Assert.Equal(ConflictFactionOperationalPosture.LogisticsFocused, result.FriendlyPosture);
        Assert.Equal("BRV", result.HostileFactionCode);
        Assert.Equal("Bravo Directorate", result.HostileFactionName);
        Assert.Equal(ConflictFactionOperationalPosture.AirFocused, result.HostilePosture);
    }

    [Fact]
    public void BuildPreservesLegacyPostureFallbackFromExistingPresentationContract()
    {
        var identity = new ConflictCampaignIdentity(
            "operation:legacy-detail",
            "Operation Legacy Detail",
            new ConflictFactionIdentity(
                "faction:friendly",
                "Friendly Coalition",
                "FRN",
                ConflictSide.Friendly)
            {
                Posture = ConflictFactionOperationalPosture.Defensive
            },
            new ConflictFactionIdentity(
                "faction:hostile",
                "Hostile Directorate",
                "HST",
                ConflictSide.Hostile)
            {
                Posture = ConflictFactionOperationalPosture.Aggressive
            });
        var entry = new ConflictCampaignHistoryEntry(
            "campaign-legacy-detail",
            "FICTIONAL-LEGACY",
            identity,
            ConflictCampaignOutcome.Victory,
            ConflictCampaignPhase.FriendlySecured,
            8,
            0.81,
            new DateTimeOffset(2026, 9, 20, 16, 0, 0, TimeSpan.Zero),
            null,
            null);

        CompletedMilitaryOperationDetailProjection result =
            CompletedMilitaryOperationDetailProjectionBuilder.Build(entry);

        Assert.Equal(identity.FriendlyFaction.Posture, result.FriendlyPosture);
        Assert.Equal(identity.HostileFaction.Posture, result.HostilePosture);
    }
}
