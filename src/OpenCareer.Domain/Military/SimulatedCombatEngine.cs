using OpenCareer.Domain.Simulation;

namespace OpenCareer.Domain.Military;

public enum SimulatedEngagementOutcome
{
    NoContact,
    ObjectiveAchieved,
    ObjectivePartiallyAchieved,
    MissionDisrupted,
    AbortRecommended
}

public sealed record SimulatedEngagementRequest(
    ulong CareerSeed,
    string EngagementKey,
    MilitaryOperationKind OperationKind,
    double MissionExecutionQuality,
    double ThreatExposure,
    double AircraftReadiness,
    double SupportFactor)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(EngagementKey);

        if (!Enum.IsDefined(OperationKind))
            throw new ArgumentOutOfRangeException(nameof(OperationKind));

        ValidateUnit(MissionExecutionQuality, nameof(MissionExecutionQuality));
        ValidateUnit(ThreatExposure, nameof(ThreatExposure));
        ValidateUnit(AircraftReadiness, nameof(AircraftReadiness));
        ValidateUnit(SupportFactor, nameof(SupportFactor));
    }

    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record SimulatedEngagementResult(
    SimulatedEngagementOutcome Outcome,
    double ObjectiveEffectiveness,
    double MissionDisruption,
    double AircraftStressIndex)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Outcome))
            throw new ArgumentOutOfRangeException(nameof(Outcome));

        ValidateUnit(ObjectiveEffectiveness, nameof(ObjectiveEffectiveness));
        ValidateUnit(MissionDisruption, nameof(MissionDisruption));

        if (!double.IsFinite(AircraftStressIndex) || AircraftStressIndex is < 0 or > 0.10)
            throw new ArgumentOutOfRangeException(nameof(AircraftStressIndex));
    }

    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public static class SimulatedCombatEngine
{
    public static SimulatedEngagementResult Resolve(SimulatedEngagementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var random = DeterministicSeed.CreateStream(
            request.CareerSeed,
            $"military-engagement:{request.EngagementKey}");

        var variation = random.NextDouble(-0.06, 0.06);

        var effectiveness = Math.Clamp(
            (0.55 * request.MissionExecutionQuality)
            + (0.25 * request.AircraftReadiness)
            + (0.20 * request.SupportFactor)
            - (0.35 * request.ThreatExposure)
            + variation,
            0,
            1);

        var disruption = Math.Clamp(
            (0.65 * request.ThreatExposure)
            + (0.20 * (1 - request.AircraftReadiness))
            - (0.15 * request.SupportFactor)
            + random.NextDouble(0, 0.08),
            0,
            1);

        var stress = Math.Clamp(
            request.ThreatExposure
            * disruption
            * (1.05 - (0.55 * request.AircraftReadiness))
            * 0.10,
            0,
            0.10);

        var outcome = DetermineOutcome(
            request,
            effectiveness,
            disruption);

        var result = new SimulatedEngagementResult(
            outcome,
            effectiveness,
            disruption,
            stress);

        result.Validate();
        return result;
    }

    private static SimulatedEngagementOutcome DetermineOutcome(
        SimulatedEngagementRequest request,
        double effectiveness,
        double disruption)
    {
        if (request.ThreatExposure < 0.05
            && request.OperationKind is MilitaryOperationKind.Training
                or MilitaryOperationKind.Readiness)
        {
            return SimulatedEngagementOutcome.NoContact;
        }

        if (disruption >= 0.80)
            return SimulatedEngagementOutcome.AbortRecommended;

        if (effectiveness >= 0.72)
            return SimulatedEngagementOutcome.ObjectiveAchieved;

        if (effectiveness >= 0.45)
            return SimulatedEngagementOutcome.ObjectivePartiallyAchieved;

        return SimulatedEngagementOutcome.MissionDisrupted;
    }
}
