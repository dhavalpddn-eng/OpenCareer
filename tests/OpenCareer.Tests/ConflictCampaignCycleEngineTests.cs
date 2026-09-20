using OpenCareer.Domain.Conflict;

namespace OpenCareer.Tests;

public sealed class ConflictCampaignCycleEngineTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NearbyLogisticsRecoversGroundUnitBetweenOperations()
    {
        GroundUnitState supported = Unit(
            "91000000-0000-0000-0000-000000000001",
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35.00, -97.00),
            strength: 0.60,
            readiness: 0.45);

        GroundUnitState logistics = Unit(
            "91000000-0000-0000-0000-000000000002",
            ConflictSide.Friendly,
            GroundUnitRole.Logistics,
            new GeoPoint(35.02, -97.02),
            strength: 0.90,
            readiness: 0.90);

        ConflictWorldState world = World(
            new[] { supported, logistics },
            airUnits: null,
            threats: null);

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "cycle-ground-recovery",
                world);

        ConflictWorldState evolved =
            ConflictCampaignCycleEngine.Apply(
                world with
                {
                    UpdatedAt = Epoch.AddHours(6)
                },
                campaign);

        GroundUnitState after = evolved.Units.Single(
            unit => unit.UnitId == supported.UnitId);

        Assert.True(after.Strength > supported.Strength);
        Assert.True(after.Readiness > supported.Readiness);
        Assert.InRange(after.Strength, 0, 0.98);
        Assert.InRange(after.Readiness, 0, 0.98);
    }

    [Fact]
    public void GroundUnitDoesNotRepairWithoutLogisticsSupport()
    {
        GroundUnitState isolated = Unit(
            "92000000-0000-0000-0000-000000000001",
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35.00, -97.00),
            strength: 0.60,
            readiness: 0.45);

        ConflictWorldState world = World(
            new[] { isolated },
            airUnits: null,
            threats: null);

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "cycle-no-logistics",
                world);

        ConflictWorldState evolved =
            ConflictCampaignCycleEngine.Apply(
                world with
                {
                    UpdatedAt = Epoch.AddHours(6)
                },
                campaign);

        GroundUnitState after = evolved.Units.Single();

        Assert.Equal(isolated.Strength, after.Strength);
        Assert.Equal(isolated.Readiness, after.Readiness);
    }

    [Fact]
    public void CampaignMomentumConsolidatesSectorControlGradually()
    {
        GroundUnitState friendly = Unit(
            "93000000-0000-0000-0000-000000000001",
            ConflictSide.Friendly,
            GroundUnitRole.Command,
            new GeoPoint(35.00, -97.00),
            strength: 0.80,
            readiness: 0.80);

        ConflictWorldState world = World(
            new[] { friendly },
            airUnits: null,
            threats: null,
            control: 0.50);

        ConflictCampaignState positive =
            ConflictCampaignDirector.Create(
                "cycle-positive-momentum",
                world) with
            {
                FriendlyMomentum = 0.75
            };

        ConflictCampaignState negative =
            ConflictCampaignDirector.Create(
                "cycle-negative-momentum",
                world) with
            {
                FriendlyMomentum = -0.75
            };

        ConflictWorldState positiveWorld =
            ConflictCampaignCycleEngine.Apply(
                world with
                {
                    UpdatedAt = Epoch.AddHours(6)
                },
                positive);

        ConflictWorldState negativeWorld =
            ConflictCampaignCycleEngine.Apply(
                world with
                {
                    UpdatedAt = Epoch.AddHours(6)
                },
                negative);

        Assert.True(
            positiveWorld.Sectors.Single().FriendlyControl > 0.50);

        Assert.True(
            negativeWorld.Sectors.Single().FriendlyControl < 0.50);

        Assert.InRange(
            positiveWorld.Sectors.Single().FriendlyControl,
            0.50,
            0.525);

        Assert.InRange(
            negativeWorld.Sectors.Single().FriendlyControl,
            0.475,
            0.50);
    }

    [Fact]
    public void LogisticsRecoversAirReadinessAndLinkedThreatSeverity()
    {
        GroundUnitState hostileLogistics = Unit(
            "94000000-0000-0000-0000-000000000001",
            ConflictSide.Hostile,
            GroundUnitRole.Logistics,
            new GeoPoint(35.10, -96.90),
            strength: 0.90,
            readiness: 0.90);

        var fighter = new SimulatedAirUnitState(
            Guid.Parse("94000000-0000-0000-0000-000000000002"),
            ConflictSide.Hostile,
            AirUnitRole.Fighter,
            new GeoPoint(35.20, -96.80),
            new GeoPoint(35.30, -96.70),
            AltitudeFeet: 22_000,
            GroundSpeedKnots: 420,
            Strength: 0.80,
            Readiness: 0.40,
            Active: true);

        var threat = new ThreatState(
            Guid.Parse("94000000-0000-0000-0000-000000000003"),
            fighter.UnitId,
            ConflictSide.Hostile,
            AirThreatType.Interceptor,
            fighter.Position,
            RadiusNauticalMiles: 25,
            Severity: fighter.Strength * fighter.Readiness,
            Active: true);

        ConflictWorldState world = World(
            new[] { hostileLogistics },
            new[] { fighter },
            new[] { threat });

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "cycle-air-recovery",
                world);

        ConflictWorldState evolved =
            ConflictCampaignCycleEngine.Apply(
                world with
                {
                    UpdatedAt = Epoch.AddHours(4)
                },
                campaign);

        SimulatedAirUnitState fighterAfter =
            evolved.AirUnits.Single();

        ThreatState threatAfter =
            evolved.Threats.Single();

        Assert.True(
            fighterAfter.Readiness > fighter.Readiness);

        Assert.Equal(
            fighterAfter.Strength * fighterAfter.Readiness,
            threatAfter.Severity,
            precision: 10);

        Assert.True(
            threatAfter.Severity > threat.Severity);
    }

    [Fact]
    public void ReplacementReserveIsFiniteAndRequiresLogistics()
    {
        GroundUnitState damaged = Unit(
            "94500000-0000-0000-0000-000000000001",
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35.00, -97.00),
            strength: 0.55,
            readiness: 0.70);

        GroundUnitState logistics = Unit(
            "94500000-0000-0000-0000-000000000002",
            ConflictSide.Friendly,
            GroundUnitRole.Logistics,
            new GeoPoint(35.01, -97.01),
            strength: 0.90,
            readiness: 0.90);

        ConflictWorldState world = World(
            new[] { damaged, logistics },
            airUnits: null,
            threats: null);

        ConflictCampaignState withReserve =
            ConflictCampaignDirector.Create(
                "cycle-replacements",
                world) with
            {
                FriendlyReplacementReserve = 0.01
            };

        ConflictCampaignState withoutReserve =
            withReserve with
            {
                FriendlyReplacementReserve = 0
            };

        ConflictWorldState input = world with
        {
            UpdatedAt = Epoch.AddHours(6)
        };

        ConflictCampaignCycleResult reinforced =
            ConflictCampaignCycleEngine.ApplyCycle(
                input,
                withReserve);

        ConflictCampaignCycleResult unreinforced =
            ConflictCampaignCycleEngine.ApplyCycle(
                input,
                withoutReserve);

        double reinforcedStrength =
            reinforced.World.Units.Single(
                unit => unit.UnitId == damaged.UnitId).Strength;

        double unreinforcedStrength =
            unreinforced.World.Units.Single(
                unit => unit.UnitId == damaged.UnitId).Strength;

        Assert.True(
            reinforcedStrength > unreinforcedStrength);
        Assert.InRange(
            reinforced.FriendlyReplacementReserve,
            0,
            0.01);
        Assert.True(
            reinforced.FriendlyReplacementReserve < 0.01);
        Assert.Equal(
            0,
            unreinforced.FriendlyReplacementReserve);
    }

    [Fact]
    public void ReplacementReserveIsNotSpentWithoutOperationalLogistics()
    {
        GroundUnitState damaged = Unit(
            "94600000-0000-0000-0000-000000000001",
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35.00, -97.00),
            strength: 0.55,
            readiness: 0.70);

        ConflictWorldState world = World(
            new[] { damaged },
            airUnits: null,
            threats: null);

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "cycle-reserve-no-logistics",
                world) with
            {
                FriendlyReplacementReserve = 0.10
            };

        ConflictCampaignCycleResult result =
            ConflictCampaignCycleEngine.ApplyCycle(
                world with
                {
                    UpdatedAt = Epoch.AddHours(8)
                },
                campaign);

        Assert.Equal(
            0.10,
            result.FriendlyReplacementReserve,
            precision: 10);
    }

    [Fact]
    public void SameCycleInputProducesSameEvolution()
    {
        GroundUnitState supported = Unit(
            "95000000-0000-0000-0000-000000000001",
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35.00, -97.00),
            strength: 0.62,
            readiness: 0.51);

        GroundUnitState logistics = Unit(
            "95000000-0000-0000-0000-000000000002",
            ConflictSide.Friendly,
            GroundUnitRole.Logistics,
            new GeoPoint(35.01, -97.01),
            strength: 0.88,
            readiness: 0.84);

        ConflictWorldState world = World(
            new[] { supported, logistics },
            airUnits: null,
            threats: null);

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "cycle-deterministic",
                world) with
            {
                FriendlyMomentum = 0.42
            };

        ConflictWorldState input = world with
        {
            UpdatedAt = Epoch.AddHours(3)
        };

        ConflictWorldState first =
            ConflictCampaignCycleEngine.Apply(
                input,
                campaign);

        ConflictWorldState second =
            ConflictCampaignCycleEngine.Apply(
                input,
                campaign);

        Assert.Equal(first.Units, second.Units);
        Assert.Equal(first.AirUnits, second.AirUnits);
        Assert.Equal(first.Sectors, second.Sectors);
        Assert.Equal(first.Threats, second.Threats);
        Assert.Equal(first.SupportRequests, second.SupportRequests);
    }

    private static GroundUnitState Unit(
        string id,
        ConflictSide side,
        GroundUnitRole role,
        GeoPoint position,
        double strength,
        double readiness) =>
        new(
            Guid.Parse(id),
            side,
            role,
            position,
            strength,
            readiness,
            Pressure: 0,
            IsMobile: role is not (
                GroundUnitRole.AirDefense
                or GroundUnitRole.Command));

    private static ConflictWorldState World(
        IEnumerable<GroundUnitState> units,
        IEnumerable<SimulatedAirUnitState>? airUnits,
        IEnumerable<ThreatState>? threats,
        double control = 0.50) =>
        ConflictWorldState.Create(
            "FICTIONAL-CYCLE",
            theaterSeed: 0xC1C1EUL,
            updatedAt: Epoch,
            units,
            new[]
            {
                new ConflictSectorState(
                    "CYCLE-S1",
                    new GeoPoint(35.05, -96.95),
                    control,
                    IntelligenceConfidence: 0.60)
            },
            threats,
            airUnits);
}
