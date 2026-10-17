using OpenCareer.Domain.Conflict;

namespace OpenCareer.Tests;

public sealed class ConflictFactionBehaviorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AggressivePostureRequestsBattlefieldSupportEarlier()
    {
        GroundUnitState friendly = Unit(
            "a1000000-0000-0000-0000-000000000001",
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35.00, -97.00),
            strength: 0.80,
            readiness: 0.80,
            pressure: 0.50);

        GroundUnitState hostile = Unit(
            "a2000000-0000-0000-0000-000000000001",
            ConflictSide.Hostile,
            GroundUnitRole.Armor,
            new GeoPoint(35.03, -96.97),
            strength: 0.80,
            readiness: 0.80,
            pressure: 0);

        ConflictWorldState world = World(
            new[] { friendly, hostile });

        ConflictWorldState defensive =
            SupportRequestGenerator.Refresh(
                world,
                Epoch,
                ConflictFactionOperationalPosture.Defensive);

        ConflictWorldState aggressive =
            SupportRequestGenerator.Refresh(
                world,
                Epoch,
                ConflictFactionOperationalPosture.Aggressive);

        Assert.DoesNotContain(
            defensive.SupportRequests,
            request =>
                request.Type
                    == SupportRequestType.CloseAirSupport);

        AirSupportRequest request =
            aggressive.SupportRequests.Single(
                request =>
                    request.Type
                        == SupportRequestType.CloseAirSupport);

        Assert.Equal(
            SupportUrgency.Priority,
            request.Urgency);
    }

    [Fact]
    public void LogisticsFocusedPostureRequestsResupplyEarlier()
    {
        GroundUnitState friendly = Unit(
            "b1000000-0000-0000-0000-000000000001",
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35.00, -97.00),
            strength: 0.80,
            readiness: 0.40,
            pressure: 0);

        ConflictWorldState world = World(
            new[] { friendly });

        ConflictWorldState defensive =
            SupportRequestGenerator.Refresh(
                world,
                Epoch,
                ConflictFactionOperationalPosture.Defensive);

        ConflictWorldState logisticsFocused =
            SupportRequestGenerator.Refresh(
                world,
                Epoch,
                ConflictFactionOperationalPosture.LogisticsFocused);

        Assert.DoesNotContain(
            defensive.SupportRequests,
            request =>
                request.Type
                    == SupportRequestType.Logistics);

        AirSupportRequest request =
            logisticsFocused.SupportRequests.Single(
                request =>
                    request.Type
                        == SupportRequestType.Logistics);

        Assert.Equal(
            SupportUrgency.Priority,
            request.Urgency);
    }

    [Fact]
    public void AirFocusedPostureDetectsMoreDistantInterceptNeed()
    {
        GroundUnitState friendly = Unit(
            "c1000000-0000-0000-0000-000000000001",
            ConflictSide.Friendly,
            GroundUnitRole.Command,
            new GeoPoint(35.00, -97.00),
            strength: 0.90,
            readiness: 0.90,
            pressure: 0);

        var hostileFighter = new SimulatedAirUnitState(
            Guid.Parse(
                "c2000000-0000-0000-0000-000000000001"),
            ConflictSide.Hostile,
            AirUnitRole.Fighter,
            new GeoPoint(35.00, -94.40),
            new GeoPoint(35.00, -94.00),
            AltitudeFeet: 24_000,
            GroundSpeedKnots: 420,
            Strength: 0.85,
            Readiness: 0.85,
            Active: true);

        ConflictWorldState world = World(
            new[] { friendly },
            new[] { hostileFighter });

        double distance =
            ConflictGeometry.DistanceNauticalMiles(
                friendly.Position,
                hostileFighter.Position);

        Assert.InRange(distance, 120.01, 145);

        ConflictWorldState defensive =
            SupportRequestGenerator.Refresh(
                world,
                Epoch,
                ConflictFactionOperationalPosture.Defensive);

        ConflictWorldState airFocused =
            SupportRequestGenerator.Refresh(
                world,
                Epoch,
                ConflictFactionOperationalPosture.AirFocused);

        Assert.DoesNotContain(
            defensive.SupportRequests,
            request =>
                request.Type
                    == SupportRequestType.Intercept);

        AirSupportRequest request =
            airFocused.SupportRequests.Single(
                request =>
                    request.Type
                        == SupportRequestType.Intercept);

        Assert.Equal(
            SupportUrgency.Priority,
            request.Urgency);
    }

    [Fact]
    public void AggressivePosturePrioritizesArmorReplacements()
    {
        GroundUnitState infantry = Unit(
            "d1000000-0000-0000-0000-000000000001",
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35.00, -97.00),
            strength: 0.40,
            readiness: 0.75,
            pressure: 0);

        GroundUnitState armor = Unit(
            "d1000000-0000-0000-0000-000000000002",
            ConflictSide.Friendly,
            GroundUnitRole.Armor,
            new GeoPoint(35.01, -97.01),
            strength: 0.60,
            readiness: 0.75,
            pressure: 0);

        GroundUnitState logistics = Unit(
            "d1000000-0000-0000-0000-000000000003",
            ConflictSide.Friendly,
            GroundUnitRole.Logistics,
            new GeoPoint(35.02, -97.02),
            strength: 0.90,
            readiness: 0.90,
            pressure: 0);

        ConflictWorldState world = World(
            new[] { infantry, armor, logistics });

        ConflictCampaignState defensiveCampaign =
            Campaign(
                "replacement-defensive",
                world,
                ConflictFactionOperationalPosture.Defensive);

        ConflictCampaignState aggressiveCampaign =
            Campaign(
                "replacement-aggressive",
                world,
                ConflictFactionOperationalPosture.Aggressive);

        ConflictWorldState input = world with
        {
            UpdatedAt = Epoch.AddHours(2)
        };

        ConflictCampaignCycleResult defensive =
            ConflictCampaignCycleEngine.ApplyCycle(
                input,
                defensiveCampaign);

        ConflictCampaignCycleResult aggressive =
            ConflictCampaignCycleEngine.ApplyCycle(
                input,
                aggressiveCampaign);

        double defensiveArmor =
            defensive.World.Units.Single(
                unit => unit.UnitId == armor.UnitId).Strength;

        double aggressiveArmor =
            aggressive.World.Units.Single(
                unit => unit.UnitId == armor.UnitId).Strength;

        double defensiveInfantry =
            defensive.World.Units.Single(
                unit => unit.UnitId == infantry.UnitId).Strength;

        double aggressiveInfantry =
            aggressive.World.Units.Single(
                unit => unit.UnitId == infantry.UnitId).Strength;

        Assert.True(
            aggressiveArmor > defensiveArmor);

        Assert.True(
            defensiveInfantry > aggressiveInfantry);
    }

    [Fact]
    public void GeneratedFactionPosturesAreDeterministicAndSupported()
    {
        ConflictWorldState world = World(
            new[]
            {
                Unit(
                    "e1000000-0000-0000-0000-000000000001",
                    ConflictSide.Friendly,
                    GroundUnitRole.Command,
                    new GeoPoint(35, -97),
                    0.8,
                    0.8,
                    0),
                Unit(
                    "e2000000-0000-0000-0000-000000000001",
                    ConflictSide.Hostile,
                    GroundUnitRole.Command,
                    new GeoPoint(35.1, -96.9),
                    0.8,
                    0.8,
                    0)
            });

        ConflictCampaignIdentity first =
            ConflictCampaignIdentityGenerator.Create(
                "posture-determinism",
                world);

        ConflictCampaignIdentity second =
            ConflictCampaignIdentityGenerator.Create(
                "posture-determinism",
                world);

        Assert.Equal(first, second);
        Assert.True(
            Enum.IsDefined(
                typeof(ConflictFactionOperationalPosture),
                first.FriendlyFaction.Posture));
        Assert.True(
            Enum.IsDefined(
                typeof(ConflictFactionOperationalPosture),
                first.HostileFaction.Posture));
    }

    private static ConflictCampaignState Campaign(
        string campaignId,
        ConflictWorldState world,
        ConflictFactionOperationalPosture posture)
    {
        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                campaignId,
                world);

        ConflictCampaignIdentity identity =
            campaign.Identity!;

        return campaign with
        {
            FriendlyReplacementReserve = 0.003,
            Identity = identity with
            {
                FriendlyFaction =
                    identity.FriendlyFaction with
                    {
                        Posture = posture
                    }
            }
        };
    }

    private static ConflictWorldState World(
        IEnumerable<GroundUnitState> units,
        IEnumerable<SimulatedAirUnitState>? airUnits = null) =>
        ConflictWorldState.Create(
            "FICTIONAL-POSTURE",
            theaterSeed: 0xB3A0UL,
            updatedAt: Epoch,
            units,
            new[]
            {
                new ConflictSectorState(
                    "POSTURE-S1",
                    new GeoPoint(35.00, -97.00),
                    FriendlyControl: 0.80,
                    IntelligenceConfidence: 0.80)
            },
            airUnits: airUnits);

    private static GroundUnitState Unit(
        string id,
        ConflictSide side,
        GroundUnitRole role,
        GeoPoint position,
        double strength,
        double readiness,
        double pressure) =>
        new(
            Guid.Parse(id),
            side,
            role,
            position,
            strength,
            readiness,
            pressure,
            IsMobile: role is not (
                GroundUnitRole.AirDefense
                or GroundUnitRole.Command));
}
