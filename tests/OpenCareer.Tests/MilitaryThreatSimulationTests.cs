using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class MilitaryThreatSimulationTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OutsideThreatZoneProducesNoExposure()
    {
        var zone = CreateZone();
        var sample = new MilitaryThreatSample(
            Epoch.AddHours(1),
            LatitudeDegrees: 42.0,
            LongitudeDegrees: -75.0,
            AltitudeFeet: 10_000);

        var exposure = MilitaryThreatEvaluator.Evaluate(sample, [zone]);

        Assert.Equal(0, exposure.Pressure);
        Assert.Equal(SimulatedThreatLevel.None, exposure.Level);
        Assert.Empty(exposure.ContributingThreatIds);
    }

    [Fact]
    public void CenterOfThreatZoneUsesSeverityAndConfidence()
    {
        var zone = CreateZone();
        var sample = new MilitaryThreatSample(
            Epoch.AddHours(1),
            LatitudeDegrees: 43.0,
            LongitudeDegrees: -75.0,
            AltitudeFeet: 10_000);

        var exposure = MilitaryThreatEvaluator.Evaluate(sample, [zone]);

        Assert.Equal(0.40, exposure.Pressure, precision: 6);
        Assert.Equal(SimulatedThreatLevel.Moderate, exposure.Level);
        Assert.Contains("threat-1", exposure.ContributingThreatIds);
    }

    [Fact]
    public void AltitudeBoundaryPreventsUnrelatedThreatExposure()
    {
        var zone = CreateZone() with
        {
            MinimumAltitudeFeet = 5_000,
            MaximumAltitudeFeet = 15_000
        };

        var sample = new MilitaryThreatSample(
            Epoch.AddHours(1),
            LatitudeDegrees: 43.0,
            LongitudeDegrees: -75.0,
            AltitudeFeet: 20_000);

        var exposure = MilitaryThreatEvaluator.Evaluate(sample, [zone]);

        Assert.Equal(0, exposure.Pressure);
    }

    [Fact]
    public void MultipleThreatsRemainBounded()
    {
        var zones = Enumerable.Range(0, 20)
            .Select(i => CreateZone() with
            {
                ThreatId = $"threat-{i}",
                Severity = 1,
                Confidence = 1
            })
            .ToArray();

        var sample = new MilitaryThreatSample(
            Epoch.AddHours(1),
            LatitudeDegrees: 43.0,
            LongitudeDegrees: -75.0,
            AltitudeFeet: 10_000);

        var exposure = MilitaryThreatEvaluator.Evaluate(sample, zones);

        Assert.InRange(exposure.Pressure, 0, 1);
        Assert.Equal(SimulatedThreatLevel.Critical, exposure.Level);
    }

    [Fact]
    public void ExposureSummaryWeightsDurationAndPeak()
    {
        var low = new MilitaryThreatExposure(
            0.10,
            SimulatedThreatLevel.Low,
            ["low"]);
        var high = new MilitaryThreatExposure(
            0.80,
            SimulatedThreatLevel.Critical,
            ["high"]);

        var summary = MilitaryThreatEvaluator.Summarize(
            [
                (low, 90.0),
                (high, 10.0)
            ]);

        Assert.Equal(0.17, summary.MeanPressure, precision: 6);
        Assert.Equal(0.80, summary.PeakPressure, precision: 6);
        Assert.Equal(100, summary.ExposedSeconds);
        Assert.Equal(100, summary.TotalSeconds);
        Assert.InRange(summary.MissionRiskIndex, 0, 1);
    }

    private static SimulatedThreatZone CreateZone() =>
        new(
            "threat-1",
            SimulatedThreatCategory.GroundBasedOpposition,
            LatitudeDegrees: 43.0,
            LongitudeDegrees: -75.0,
            RadiusNauticalMiles: 30,
            Severity: 0.8,
            Confidence: 0.5,
            ActiveFrom: Epoch,
            ActiveUntil: Epoch.AddDays(1));
}
