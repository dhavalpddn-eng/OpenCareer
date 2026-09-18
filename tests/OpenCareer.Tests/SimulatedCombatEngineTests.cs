using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class SimulatedCombatEngineTests
{
    [Fact]
    public void SameRequestProducesSameResult()
    {
        var request = CreateRequest(
            executionQuality: 0.75,
            threatExposure: 0.45,
            readiness: 0.90,
            support: 0.60);

        var first = SimulatedCombatEngine.Resolve(request);
        var second = SimulatedCombatEngine.Resolve(request);

        Assert.Equal(first, second);
    }

    [Fact]
    public void BetterExecutionAndLowerThreatImprovesEffectiveness()
    {
        var favorable = SimulatedCombatEngine.Resolve(
            CreateRequest(
                executionQuality: 0.90,
                threatExposure: 0.15,
                readiness: 0.95,
                support: 0.80));

        var unfavorable = SimulatedCombatEngine.Resolve(
            CreateRequest(
                executionQuality: 0.35,
                threatExposure: 0.85,
                readiness: 0.55,
                support: 0.20));

        Assert.True(favorable.ObjectiveEffectiveness > unfavorable.ObjectiveEffectiveness);
        Assert.True(favorable.MissionDisruption < unfavorable.MissionDisruption);
    }

    [Fact]
    public void AircraftStressIsBoundedAndCannotBecomeCatastrophicFlatRng()
    {
        var result = SimulatedCombatEngine.Resolve(
            CreateRequest(
                executionQuality: 0.20,
                threatExposure: 1.00,
                readiness: 0.00,
                support: 0.00));

        Assert.InRange(result.AircraftStressIndex, 0, 0.10);
    }

    [Fact]
    public void ReadinessTrainingWithoutThreatProducesNoContact()
    {
        var request = new SimulatedEngagementRequest(
            CareerSeed: 1234,
            EngagementKey: "training-1",
            OperationKind: MilitaryOperationKind.Readiness,
            MissionExecutionQuality: 0.75,
            ThreatExposure: 0.01,
            AircraftReadiness: 0.90,
            SupportFactor: 0.50);

        var result = SimulatedCombatEngine.Resolve(request);

        Assert.Equal(SimulatedEngagementOutcome.NoContact, result.Outcome);
    }

    private static SimulatedEngagementRequest CreateRequest(
        double executionQuality,
        double threatExposure,
        double readiness,
        double support) =>
        new(
            CareerSeed: 1234,
            EngagementKey: "engagement-1",
            OperationKind: MilitaryOperationKind.AirSupport,
            MissionExecutionQuality: executionQuality,
            ThreatExposure: threatExposure,
            AircraftReadiness: readiness,
            SupportFactor: support);
}
