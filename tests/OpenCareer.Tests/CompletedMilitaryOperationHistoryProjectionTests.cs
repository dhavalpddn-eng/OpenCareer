using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;

namespace OpenCareer.Tests;

public sealed class CompletedMilitaryOperationHistoryProjectionTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildOrdersNewestFirstWithStableCampaignIdTieBreak()
    {
        ConflictCampaignHistoryEntry older = Entry("older", Epoch.AddHours(-2));
        ConflictCampaignHistoryEntry tiedZ = Entry("z-tied", Epoch.AddHours(-1));
        ConflictCampaignHistoryEntry tiedA = Entry("a-tied", Epoch.AddHours(-1));

        IReadOnlyList<CompletedMilitaryOperationPresentation> result =
            CompletedMilitaryOperationHistoryProjection.Build([older, tiedZ, tiedA]);

        Assert.Equal(
            new[] { "a-tied", "z-tied", "older" },
            result.Select(static item => item.CampaignId));
    }

    [Fact]
    public void BuildPreservesPersistedTerminalPostures()
    {
        ConflictCampaignHistoryEntry entry = Entry("campaign-test", Epoch) with
        {
            FinalFriendlyPosture = ConflictFactionOperationalPosture.AirFocused,
            FinalHostilePosture = ConflictFactionOperationalPosture.LogisticsFocused
        };

        CompletedMilitaryOperationPresentation result =
            Assert.Single(CompletedMilitaryOperationHistoryProjection.Build([entry]));

        Assert.Equal(
            ConflictFactionOperationalPosture.AirFocused,
            result.FriendlyPosture);
        Assert.Equal(
            ConflictFactionOperationalPosture.LogisticsFocused,
            result.HostilePosture);
    }

    [Fact]
    public void BuildDoesNotMutateSourceHistory()
    {
        ConflictCampaignHistoryEntry[] history =
        [
            Entry("older", Epoch.AddHours(-2)),
            Entry("newer", Epoch)
        ];

        _ = CompletedMilitaryOperationHistoryProjection.Build(history);

        Assert.Equal(new[] { "older", "newer" }, history.Select(static item => item.CampaignId));
    }

    private static ConflictCampaignHistoryEntry Entry(
        string campaignId,
        DateTimeOffset endedAt)
    {
        var identity = new ConflictCampaignIdentity(
            $"operation:{campaignId}",
            $"Operation {campaignId}",
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

        return new ConflictCampaignHistoryEntry(
            campaignId,
            "FICTIONAL-TEST",
            identity,
            ConflictCampaignOutcome.Victory,
            ConflictCampaignPhase.FriendlySecured,
            9,
            0.74,
            endedAt,
            ConflictFactionOperationalPosture.Defensive,
            ConflictFactionOperationalPosture.Aggressive);
    }
}
