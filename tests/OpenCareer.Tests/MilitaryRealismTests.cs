using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Military;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Tests;

public sealed class MilitaryRealismTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly EscortObjectiveProfile EscortProfile = new(
        MaximumHorizontalSeparationNauticalMiles: 3,
        MaximumVerticalSeparationFeet: 2_000,
        MaximumGroundSpeedDifferenceKnots: 150,
        MinimumQualifiedSeconds: 10,
        MinimumQualifiedFraction: 0.75,
        MaximumTelemetrySkewSeconds: 5);

    [Fact]
    public void EscortRequiresQualifiedTimeAndFractionInsideEnvelope()
    {
        var progress = EscortObjectiveProgress.Create(Epoch);
        var player = CreateTelemetry(Epoch.AddSeconds(5), 43.0, -75.0, 10_000, 320);
        var protectedAircraft = CreateTelemetry(Epoch.AddSeconds(5), 43.0, -75.0, 10_200, 300);

        progress = EscortObjectiveValidator.Advance(
            EscortProfile,
            progress,
            player,
            protectedAircraft,
            deltaSeconds: 5);

        Assert.True(progress.InEnvelope);
        Assert.False(progress.Assess(EscortProfile).IsSatisfied);

        player = player with { Timestamp = Epoch.AddSeconds(10) };
        protectedAircraft = protectedAircraft with { Timestamp = Epoch.AddSeconds(10) };
        progress = EscortObjectiveValidator.Advance(
            EscortProfile,
            progress,
            player,
            protectedAircraft,
            deltaSeconds: 5);

        var assessment = progress.Assess(EscortProfile);
        Assert.True(assessment.IsSatisfied);
        Assert.Equal(1, assessment.Quality);
    }

    [Fact]
    public void OutOfEnvelopeTimeReducesEscortQuality()
    {
        var progress = EscortObjectiveProgress.Create(Epoch);

        var player = CreateTelemetry(Epoch.AddSeconds(5), 43.0, -75.0, 10_000, 300);
        var protectedAircraft = CreateTelemetry(Epoch.AddSeconds(5), 43.0, -74.90, 10_000, 300);
        progress = EscortObjectiveValidator.Advance(
            EscortProfile,
            progress,
            player,
            protectedAircraft,
            deltaSeconds: 5);

        for (int i = 2; i <= 3; i++)
        {
            DateTimeOffset time = Epoch.AddSeconds(i * 5);
            player = CreateTelemetry(time, 43.0, -75.0, 10_000, 300);
            protectedAircraft = CreateTelemetry(time, 43.0, -75.0, 10_000, 300);
            progress = EscortObjectiveValidator.Advance(
                EscortProfile,
                progress,
                player,
                protectedAircraft,
                deltaSeconds: 5);
        }

        Assert.False(progress.Assess(EscortProfile).IsSatisfied);

        player = player with { Timestamp = Epoch.AddSeconds(20) };
        protectedAircraft = protectedAircraft with { Timestamp = Epoch.AddSeconds(20) };
        progress = EscortObjectiveValidator.Advance(
            EscortProfile,
            progress,
            player,
            protectedAircraft,
            deltaSeconds: 5);

        var assessment = progress.Assess(EscortProfile);
        Assert.True(assessment.IsSatisfied);
        Assert.Equal(0.75, assessment.Quality, precision: 6);
    }

    [Fact]
    public void PausedSlewAndStaleTelemetryDoNotAccumulateEscortTime()
    {
        var progress = EscortObjectiveProgress.Create(Epoch);
        var player = CreateTelemetry(Epoch.AddSeconds(5), 43.0, -75.0, 10_000, 300);
        var protectedAircraft = CreateTelemetry(Epoch.AddSeconds(5), 43.0, -75.0, 10_000, 300);

        progress = EscortObjectiveValidator.Advance(
            EscortProfile,
            progress,
            player with { Paused = true },
            protectedAircraft,
            deltaSeconds: 5);
        Assert.Equal(0, progress.EvaluatedSeconds);

        player = player with { Timestamp = Epoch.AddSeconds(10), Paused = false, SlewActive = true };
        protectedAircraft = protectedAircraft with { Timestamp = Epoch.AddSeconds(10) };
        progress = EscortObjectiveValidator.Advance(
            EscortProfile,
            progress,
            player,
            protectedAircraft,
            deltaSeconds: 5);
        Assert.Equal(0, progress.EvaluatedSeconds);

        player = player with { Timestamp = Epoch.AddSeconds(20), SlewActive = false };
        protectedAircraft = protectedAircraft with { Timestamp = Epoch.AddSeconds(10) };
        progress = EscortObjectiveValidator.Advance(
            EscortProfile,
            progress,
            player,
            protectedAircraft,
            deltaSeconds: 5);

        Assert.Equal(0, progress.EvaluatedSeconds);
        Assert.False(progress.InEnvelope);
    }

    [Fact]
    public void StructuredObjectiveAssessmentCanSatisfyMissionObjective()
    {
        var plan = new MilitaryOperationPlan(
            Guid.NewGuid(),
            MilitaryOperationKind.AirSupport,
            "KRME",
            "KRME",
            "SIM-AREA-1",
            MilitaryOperationRequirementsCatalog.For(MilitaryOperationKind.AirSupport),
            MinimumOnStationSeconds: 0,
            RequiresObjectiveAction: true,
            CampaignId: "campaign-1",
            SupportedSideId: "side-a");

        var progress = MilitaryMissionProgress.Briefed(plan, Epoch) with
        {
            Phase = MilitaryOperationPhase.EnRoute,
            UpdatedAt = Epoch.AddSeconds(1)
        };

        progress = MilitaryMissionEngine.Advance(
            plan,
            progress,
            new MilitaryMissionEvidence(
                Epoch.AddSeconds(2),
                HasStableTelemetry: true,
                AtOrigin: false,
                Airborne: true,
                InObjectiveArea: true,
                ObjectiveActionVerified: false,
                AtRecoveryAirfield: false,
                ParkedAndSecured: false,
                ObjectiveAssessment: new MilitaryObjectiveAssessment(
                    EscortObjectiveValidator.ValidatorId,
                    IsSatisfied: true,
                    Quality: 0.90)));

        Assert.Equal(MilitaryOperationPhase.Objective, progress.Phase);
        Assert.True(progress.ObjectiveActionVerified);
    }

    [Fact]
    public void ManyWeakSameCategoryThreatsAccumulateWithDiminishingCorrelation()
    {
        var zones = Enumerable.Range(0, 20)
            .Select(i => CreateThreat() with
            {
                ThreatId = $"correlated-{i}",
                Severity = 0.10,
                Confidence = 1
            })
            .ToArray();

        var exposure = MilitaryThreatEvaluator.Evaluate(
            new MilitaryThreatSample(Epoch, 43.0, -75.0, 10_000),
            zones);

        Assert.InRange(exposure.Pressure, 0.32, 0.33);
    }

    [Fact]
    public void IndependentThreatCategoriesCompoundMoreThanCorrelatedDuplicates()
    {
        var sample = new MilitaryThreatSample(Epoch, 43.0, -75.0, 10_000);

        var correlated = MilitaryThreatEvaluator.Evaluate(
            sample,
            [
                CreateThreat() with { ThreatId = "same-1", Severity = 0.10, Confidence = 1 },
                CreateThreat() with { ThreatId = "same-2", Severity = 0.10, Confidence = 1 }
            ]);

        var independent = MilitaryThreatEvaluator.Evaluate(
            sample,
            [
                CreateThreat() with
                {
                    ThreatId = "category-1",
                    Category = SimulatedThreatCategory.GroundBasedOpposition,
                    Severity = 0.10,
                    Confidence = 1
                },
                CreateThreat() with
                {
                    ThreatId = "category-2",
                    Category = SimulatedThreatCategory.AirborneOpposition,
                    Severity = 0.10,
                    Confidence = 1
                }
            ]);

        Assert.True(independent.Pressure > correlated.Pressure);
        Assert.InRange(independent.Pressure, 0.189, 0.191);
    }

    [Fact]
    public void DisruptionUncertaintyCanMoveEitherSideOfDeterministicBaseline()
    {
        const double threat = 0.40;
        const double readiness = 0.80;
        const double support = 0.50;
        double baseline = (0.65 * threat)
            + (0.20 * (1 - readiness))
            - (0.15 * support);

        double[] disruptions = Enumerable.Range(0, 128)
            .Select(index => SimulatedCombatEngine.Resolve(
                new SimulatedEngagementRequest(
                    CareerSeed: 1234,
                    EngagementKey: $"zero-centered-{index}",
                    OperationKind: MilitaryOperationKind.AirSupport,
                    MissionExecutionQuality: 0.70,
                    ThreatExposure: threat,
                    AircraftReadiness: readiness,
                    SupportFactor: support))
                .MissionDisruption)
            .ToArray();

        Assert.Contains(disruptions, value => value < baseline);
        Assert.Contains(disruptions, value => value > baseline);
        Assert.InRange(disruptions.Average(value => value - baseline), -0.01, 0.01);
    }

    private static SimulatedThreatZone CreateThreat() =>
        new(
            "threat",
            SimulatedThreatCategory.GroundBasedOpposition,
            43.0,
            -75.0,
            30,
            0.8,
            0.5,
            Epoch.AddHours(-1),
            Epoch.AddHours(1));

    private static AircraftTelemetrySnapshot CreateTelemetry(
        DateTimeOffset time,
        double latitude,
        double longitude,
        double altitudeMslFeet,
        double groundSpeedKnots) =>
        new(
            time,
            latitude,
            longitude,
            altitudeMslFeet,
            AltitudeAglFeet: 5_000,
            IndicatedAirspeedKnots: groundSpeedKnots,
            GroundSpeedKnots: groundSpeedKnots,
            VerticalSpeedFeetPerMinute: 0,
            HeadingDegrees: 90,
            PitchDegrees: 0,
            BankDegrees: 0,
            NormalAccelerationG: 1,
            OnGround: false,
            ParkingBrakeSet: false,
            EnginesRunning: 2,
            FuelTotalPounds: 1_000,
            PayloadPounds: 1_000,
            FlapsPositionPercent: 0,
            GearDown: false,
            Paused: false,
            SlewActive: false);
}
