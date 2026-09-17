using OpenCareer.Domain.Events;

namespace OpenCareer.Tests;

public sealed class ConflictSimulationTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CuratedRealWorldRegionRequiresProvenance()
    {
        var invalid = new ConflictRegionProfile(
            "region-a",
            BaselineTension: 0.5,
            InternalInstability: 0.2,
            BorderSecurityPressure: 0.1,
            CivilAviationResilience: 0.8,
            ConflictBaselineSource.CuratedRealWorld);

        Assert.Throws<ArgumentException>(invalid.Validate);

        var valid = invalid with
        {
            BaselineAsOf = Epoch,
            SourceReference = "source:curated:example"
        };
        valid.Validate();

        var amplified = ConflictRiskModel.EffectiveBaselineTension(
            valid,
            ConflictSimulationPolicy.Default);
        var simulated = ConflictRiskModel.EffectiveBaselineTension(
            valid with { BaselineSource = ConflictBaselineSource.Simulated },
            ConflictSimulationPolicy.Default);

        Assert.True(amplified > simulated);
    }

    [Fact]
    public void LongPeacefulBorderCreatesSurveillanceDemandWithoutWarPressure()
    {
        var a = new ConflictRegionProfile(
            "region-a",
            0.15,
            0.05,
            BorderSecurityPressure: 0.80,
            CivilAviationResilience: 0.95);
        var b = new ConflictRegionProfile(
            "region-b",
            0.12,
            0.05,
            BorderSecurityPressure: 0.55,
            CivilAviationResilience: 0.95);
        var connection = new ConflictRegionConnection(
            "region-a",
            "region-b",
            LandBorderExposure: 1.0,
            MaritimeExposure: 0,
            DisputeFriction: 0.02,
            AllianceStrength: 0.20,
            EconomicInterdependence: 0.80);

        var postureA = new RegionalConflictPosture(
            "region-a",
            ConflictEscalationStage.Normal,
            0.20,
            0,
            Epoch,
            Epoch);
        var postureB = new RegionalConflictPosture(
            "region-b",
            ConflictEscalationStage.Normal,
            0.20,
            0,
            Epoch,
            Epoch);

        var pressure = ConflictRiskModel.ConnectionEscalationPressure(
            connection,
            postureA,
            postureB);
        var outbreak = ConflictRiskModel.DailyOutbreakProbability(pressure);
        var surveillance = ConflictRiskModel.BorderSurveillanceDemand(a, [connection]);

        Assert.True(surveillance >= 0.80);
        Assert.True(pressure < 0.10);
        Assert.True(outbreak < 0.0001);
    }

    [Fact]
    public void CuratedConflictStartsActiveAndChangesAviationDemand()
    {
        var profile = BuildProfile(
            curatedConflicts:
            [
                new CuratedConflictSeed(
                    "baseline-conflict",
                    ["region-a", "region-b"],
                    "side-a",
                    "side-b",
                    Severity: 0.85,
                    BaselineAsOf: Epoch,
                    SourceReference: "source:curated:baseline")
            ]);

        var state = ConflictWorldEngine.Create(profile, 12345, Epoch);

        var security = ConflictWorldEngine.GetRegionalSecurityState(
            state,
            profile,
            "region-a");
        var demand = ConflictWorldEngine.GetAviationDemand(
            state,
            profile,
            "region-a");

        Assert.Equal(RegionalSecurityPhase.ActiveConflict, security.Phase);
        Assert.True(demand.MilitaryTransportMultiplier > 2);
        Assert.True(demand.FighterOperationsMultiplier > 2);
        Assert.True(demand.PassengerContinuityMultiplier < 1);
    }

    [Fact]
    public void PlayerOperationalSupportIsBoundedAndSupportingBothSidesCancels()
    {
        var profile = BuildProfile(
            curatedConflicts:
            [
                new CuratedConflictSeed(
                    "baseline-conflict",
                    ["region-a", "region-b"],
                    "side-a",
                    "side-b",
                    Severity: 0.80,
                    BaselineAsOf: Epoch,
                    SourceReference: "source:curated:baseline")
            ]);
        var initial = ConflictWorldEngine.Create(profile, 9001, Epoch);

        var oneSide = ConflictWorldEngine.Advance(
            initial,
            profile,
            Epoch.AddDays(1),
            [
                new ConflictPlayerContribution(
                    "baseline-conflict",
                    "side-a",
                    ConflictContributionKind.TroopTransport,
                    EffortPoints: 1_000_000_000,
                    OccurredAt: Epoch.AddHours(2))
            ]);

        var shifted = oneSide.Campaigns.Single(c => c.CampaignId == "baseline-conflict");
        Assert.InRange(
            shifted.OutcomeBalance,
            0,
            ConflictSimulationPolicy.Default.MaxPlayerOutcomeShiftPerDay);

        var bothSides = ConflictWorldEngine.Advance(
            initial,
            profile,
            Epoch.AddDays(1),
            [
                new ConflictPlayerContribution(
                    "baseline-conflict",
                    "side-a",
                    ConflictContributionKind.CargoLogistics,
                    50_000,
                    Epoch.AddHours(2)),
                new ConflictPlayerContribution(
                    "baseline-conflict",
                    "side-b",
                    ConflictContributionKind.CargoLogistics,
                    50_000,
                    Epoch.AddHours(3))
            ]);

        var balanced = bothSides.Campaigns.Single(c => c.CampaignId == "baseline-conflict");
        Assert.Equal(0, balanced.OutcomeBalance, precision: 10);
    }

    [Fact]
    public void HumanitarianWorkHelpsWithoutChoosingAConflictSide()
    {
        var profile = BuildProfile(
            curatedConflicts:
            [
                new CuratedConflictSeed(
                    "baseline-conflict",
                    ["region-a"],
                    "side-a",
                    "side-b",
                    Severity: 0.90,
                    BaselineAsOf: Epoch,
                    SourceReference: "source:curated:baseline")
            ]);
        var initial = ConflictWorldEngine.Create(profile, 777, Epoch);
        var original = initial.Campaigns.Single();

        var next = ConflictWorldEngine.Advance(
            initial,
            profile,
            Epoch.AddDays(1),
            [
                new ConflictPlayerContribution(
                    "baseline-conflict",
                    SupportedSideId: null,
                    ConflictContributionKind.Humanitarian,
                    EffortPoints: 100_000,
                    OccurredAt: Epoch.AddHours(5))
            ]);

        var changed = next.Campaigns.Single();
        Assert.Equal(0, changed.OutcomeBalance);
        Assert.True(changed.Severity <= original.Severity + 0.02);
    }

    [Fact]
    public void SystemicConflictMobilizesAlliesButPreservesCivilianAviation()
    {
        var profile = BuildProfile();
        var regions = new[]
        {
            new RegionalConflictPosture(
                "region-a",
                ConflictEscalationStage.ActiveConflict,
                0.90,
                0.90,
                Epoch,
                Epoch),
            new RegionalConflictPosture(
                "region-b",
                ConflictEscalationStage.ActiveConflict,
                0.88,
                0.85,
                Epoch,
                Epoch),
            new RegionalConflictPosture(
                "region-c",
                ConflictEscalationStage.LogisticsBuildup,
                0.60,
                0.50,
                Epoch,
                Epoch)
        };

        var campaigns = new[]
        {
            new ConflictCampaignState(
                "campaign-1",
                ["region-a"],
                "side-a",
                "side-x",
                ConflictCampaignPhase.ActiveConflict,
                0.90,
                0,
                300,
                Epoch,
                Epoch),
            new ConflictCampaignState(
                "campaign-2",
                ["region-b"],
                "side-b",
                "side-y",
                ConflictCampaignPhase.ActiveConflict,
                0.85,
                0,
                300,
                Epoch,
                Epoch)
        };

        var state = new ConflictWorldState(
            ConflictWorldState.CurrentSchemaVersion,
            42,
            Epoch,
            regions,
            campaigns,
            new SystemicConflictState(
                true,
                0.85,
                Epoch,
                ["region-a", "region-b", "region-c"]));

        state.Validate(profile);
        var demand = ConflictWorldEngine.GetAviationDemand(
            state,
            profile,
            "region-c");

        Assert.True(demand.CargoMultiplier > 1.5);
        Assert.True(demand.MilitaryTransportMultiplier > 2);
        Assert.True(demand.PassengerContinuityMultiplier >= 0.70);
    }

    [Fact]
    public void ConflictSimulationIsDeterministicAcrossSaveReloadBoundaries()
    {
        var profile = BuildProfile();

        var start = ConflictWorldEngine.Create(profile, 55555, Epoch);
        var direct = ConflictWorldEngine.Advance(
            start,
            profile,
            Epoch.AddDays(365));

        var halfway = ConflictWorldEngine.Advance(
            start,
            profile,
            Epoch.AddDays(180));
        var resumed = ConflictWorldEngine.Advance(
            halfway,
            profile,
            Epoch.AddDays(365));

        Assert.Equal(direct.UpdatedAt, resumed.UpdatedAt);
        Assert.Equal(direct.SystemicConflict.IsActive, resumed.SystemicConflict.IsActive);
        Assert.Equal(direct.Campaigns.Count, resumed.Campaigns.Count);
        Assert.Equal(direct.Regions.Count, resumed.Regions.Count);

        foreach (var region in direct.Regions)
        {
            var restored = resumed.Regions.Single(x => x.RegionId == region.RegionId);
            Assert.Equal(region.Stage, restored.Stage);
            Assert.Equal(region.Tension, restored.Tension, precision: 12);
            Assert.Equal(region.Severity, restored.Severity, precision: 12);
        }

        foreach (var campaign in direct.Campaigns)
        {
            var restored = resumed.Campaigns.Single(x => x.CampaignId == campaign.CampaignId);
            Assert.Equal(campaign.Phase, restored.Phase);
            Assert.Equal(campaign.Severity, restored.Severity, precision: 12);
            Assert.Equal(campaign.OutcomeBalance, restored.OutcomeBalance, precision: 12);
            Assert.Equal(campaign.Resolution, restored.Resolution);
        }
    }

    private static ConflictWorldProfile BuildProfile(
        IReadOnlyList<CuratedConflictSeed>? curatedConflicts = null)
    {
        var regions = new[]
        {
            new ConflictRegionProfile(
                "region-a",
                BaselineTension: 0.42,
                InternalInstability: 0.20,
                BorderSecurityPressure: 0.35,
                CivilAviationResilience: 0.85),
            new ConflictRegionProfile(
                "region-b",
                BaselineTension: 0.38,
                InternalInstability: 0.18,
                BorderSecurityPressure: 0.25,
                CivilAviationResilience: 0.80),
            new ConflictRegionProfile(
                "region-c",
                BaselineTension: 0.18,
                InternalInstability: 0.05,
                BorderSecurityPressure: 0.20,
                CivilAviationResilience: 0.95)
        };

        var connections = new[]
        {
            new ConflictRegionConnection(
                "region-a",
                "region-b",
                LandBorderExposure: 0.90,
                MaritimeExposure: 0.10,
                DisputeFriction: 0.55,
                AllianceStrength: 0.10,
                EconomicInterdependence: 0.45),
            new ConflictRegionConnection(
                "region-b",
                "region-c",
                LandBorderExposure: 0.50,
                MaritimeExposure: 0.10,
                DisputeFriction: 0.08,
                AllianceStrength: 0.85,
                EconomicInterdependence: 0.65)
        };

        return new ConflictWorldProfile(regions, connections, curatedConflicts);
    }
}
