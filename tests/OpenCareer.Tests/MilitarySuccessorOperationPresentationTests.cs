using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;

namespace OpenCareer.Tests;

public sealed class MilitarySuccessorOperationPresentationTests
{
    [Fact]
    public void BuildProjectsStableUiFacingSuccessorDetails()
    {
        DateTimeOffset availableAt =
            new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

        var friendly = new ConflictFactionIdentity(
            "friendly-1",
            "Aster Coalition",
            "AST",
            ConflictSide.Friendly)
        {
            Posture = ConflictFactionOperationalPosture.AirFocused
        };

        var hostile = new ConflictFactionIdentity(
            "hostile-1",
            "Vesper Directorate",
            "VSP",
            ConflictSide.Hostile)
        {
            Posture = ConflictFactionOperationalPosture.LogisticsFocused
        };

        var planned = new ConflictCampaignSuccessorOffer(
            "campaign-successor",
            new ConflictTheaterTemplate(
                "FICTIONAL-COAST",
                new GeoPoint(36.0, -121.5),
                RadiusNauticalMiles: 110,
                FriendlyGroundUnits: 7,
                HostileGroundUnits: 7,
                FriendlyAirUnits: 4,
                HostileAirUnits: 4),
            TheaterSeed: 42,
            new ConflictCampaignIdentity(
                "operation-1",
                "Operation Harbor Lantern",
                friendly,
                hostile),
            availableAt);

        var offer = new MilitarySuccessorOperationOffer(
            "completed-campaign",
            SourceRevision: 8,
            planned);

        MilitarySuccessorOperationPresentation presentation =
            MilitarySuccessorOperationPresentationBuilder.Build(offer);

        Assert.Equal("campaign-successor", presentation.CampaignId);
        Assert.Equal("Operation Harbor Lantern", presentation.OperationName);
        Assert.Equal("FICTIONAL-COAST", presentation.TheaterId);
        Assert.Equal("Aster Coalition", presentation.FriendlyFactionName);
        Assert.Equal("AST", presentation.FriendlyFactionCode);
        Assert.Equal(
            ConflictFactionOperationalPosture.AirFocused,
            presentation.FriendlyPosture);
        Assert.Equal("Vesper Directorate", presentation.HostileFactionName);
        Assert.Equal("VSP", presentation.HostileFactionCode);
        Assert.Equal(
            ConflictFactionOperationalPosture.LogisticsFocused,
            presentation.HostilePosture);
        Assert.Equal(availableAt, presentation.AvailableAt);
    }

    [Fact]
    public void BuildRejectsNullOffer()
    {
        Assert.Throws<ArgumentNullException>(
            () => MilitarySuccessorOperationPresentationBuilder.Build(null!));
    }
}
