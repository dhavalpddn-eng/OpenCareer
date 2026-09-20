using OpenCareer.Domain.Conflict;

namespace OpenCareer.Tests;

public sealed class ConflictCampaignDirectorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateProducesDeterministicStrategicObjectives()
    {
        var world = World(control: 0.50, intelligence: 0.30);

        var first = ConflictCampaignDirector.Create("campaign-a", world);
        var second = ConflictCampaignDirector.Create("campaign-a", world);

        Assert.Equal(first.Phase, second.Phase);
        Assert.Equal(first.FriendlyControlAverage, second.FriendlyControlAverage);
        Assert.Equal(first.Objectives, second.Objectives);
        Assert.Contains(
            first.Objectives,
            item => item.Kind == StrategicObjectiveKind.GainSectorControl);
        Assert.Contains(
            first.Objectives,
            item => item.Kind == StrategicObjectiveKind.ImproveIntelligence);
    }

    [Fact]
    public void AdvanceTracksControlMomentumAndPressurePhase()
    {
        var initialWorld = World(control: 0.50, intelligence: 0.60);
        var campaign = ConflictCampaignDirector.Create("campaign-b", initialWorld);

        var improvedWorld = World(control: 0.64, intelligence: 0.70) with
        {
            UpdatedAt = Epoch.AddHours(2)
        };

        var advanced = ConflictCampaignDirector.Advance(
            campaign,
            improvedWorld);

        Assert.Equal(1, advanced.EvaluationSequence);
        Assert.True(advanced.FriendlyMomentum > 0);
        Assert.Equal(
            ConflictCampaignPhase.FriendlyPressure,
            advanced.Phase);
    }

    [Fact]
    public void CampaignCannotAdvanceWithAnotherTheater()
    {
        var campaign = ConflictCampaignDirector.Create(
            "campaign-c",
            World(0.50, 0.50));

        var other = World(0.50, 0.50) with
        {
            TheaterId = "FICTIONAL-OTHER"
        };

        Assert.Throws<InvalidOperationException>(
            () => ConflictCampaignDirector.Advance(campaign, other));
    }

    private static ConflictWorldState World(
        double control,
        double intelligence)
    {
        var friendly = new GroundUnitState(
            Guid.Parse("71000000-0000-0000-0000-000000000001"),
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35.0, -97.0),
            Strength: 0.85,
            Readiness: 0.60,
            Pressure: 0.30,
            IsMobile: true);

        var hostile = new GroundUnitState(
            Guid.Parse("72000000-0000-0000-0000-000000000001"),
            ConflictSide.Hostile,
            GroundUnitRole.AirDefense,
            new GeoPoint(35.05, -96.95),
            Strength: 0.75,
            Readiness: 0.80,
            Pressure: 0.10,
            IsMobile: false);

        var threat = new ThreatState(
            Guid.Parse("73000000-0000-0000-0000-000000000001"),
            hostile.UnitId,
            ConflictSide.Hostile,
            AirThreatType.AirDefense,
            hostile.Position,
            RadiusNauticalMiles: 14,
            Severity: 0.60,
            Active: true);

        return ConflictWorldState.Create(
            "FICTIONAL-CAMPAIGN",
            0x123456UL,
            Epoch,
            new[] { friendly, hostile },
            new[]
            {
                new ConflictSectorState(
                    "CAMPAIGN-S1",
                    new GeoPoint(35.02, -96.98),
                    control,
                    intelligence)
            },
            new[] { threat });
    }
}
