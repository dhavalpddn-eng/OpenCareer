using System.Text.Json.Serialization;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Fleet;

public enum AirframeReliabilityEvidenceSource { OpenCareerFallback }
/// <summary>Known persisted game facts; no empirical or simulator component-reliability claim.</summary>
public enum AirframeReliabilityConfidence { FallbackOnly }
public enum AirframeReliabilityStatus { Nominal, AttentionRequired, Unavailable }
[JsonConverter(typeof(JsonStringEnumConverter<AirframeReliabilityDataAvailability>))]
public enum AirframeReliabilityDataAvailability { Unavailable }

[Flags]
public enum AirframeReliabilityReason
{
    None = 0,
    RecordedDamage = 1,
    DamageGrounding = 2,
    InspectionDue = 4
}

/// <summary>
/// Deterministic interpretation of one coherent persisted snapshot. Nominal is not a failure
/// probability, component-health estimate, dispatch authorization or simulator observation.
/// </summary>
public sealed record AirframeReliabilityAssessment
{
    private AirframeReliabilityAssessment(AirframeStoreRecord current, AirframeServiceState serviceState, DateTimeOffset evaluatedAt)
    {
        Current = current;
        ServiceState = serviceState;
        EvaluatedAt = evaluatedAt.ToUniversalTime();
    }

    public AirframeStoreRecord Current { get; }
    public AirframeServiceState ServiceState { get; }
    public DateTimeOffset EvaluatedAt { get; }
    public AirframeId AirframeId => Current.Airframe.AirframeId;
    public string CanonicalAircraftId => Current.Airframe.CanonicalAircraftId;
    public long ConditionRevision => Current.Revision;
    public long ServiceRevision => ServiceState.Revision;
    public AirframeReliabilityEvidenceSource Source => AirframeReliabilityEvidenceSource.OpenCareerFallback;
    public AirframeReliabilityConfidence Confidence => AirframeReliabilityConfidence.FallbackOnly;
    public double WearFraction => Current.Condition.WearFraction;
    public AirframeDamageState Damage => Current.Condition.Damage;
    public AirframeInspectionStatus InspectionStatus => ServiceState.InspectionStatus;
    public TimeSpan TotalTrackedAirborneTime => ServiceState.TotalTrackedAirborneTime;
    public long TotalTrackedLandingCycles => ServiceState.TotalTrackedLandingCycles;
    public AirframeUsageOrigin LandingCycleOrigin => ServiceState.LandingCycleOrigin;
    // Explicit unknowns, never numeric zeroes that could be mistaken for measured safety.
    public AirframeReliabilityDataAvailability FailureProbabilityAvailability => AirframeReliabilityDataAvailability.Unavailable;
    public AirframeReliabilityDataAvailability ComponentReliabilityAvailability => AirframeReliabilityDataAvailability.Unavailable;
    public AirframeReliabilityDataAvailability WearStatusThresholdAvailability => AirframeReliabilityDataAvailability.Unavailable;

    public AirframeReliabilityReason Reasons => (Damage switch
    {
        AirframeDamageState.Recorded => AirframeReliabilityReason.RecordedDamage,
        AirframeDamageState.Grounding => AirframeReliabilityReason.DamageGrounding,
        _ => AirframeReliabilityReason.None
    }) | (InspectionStatus == AirframeInspectionStatus.InspectionDue ? AirframeReliabilityReason.InspectionDue : AirframeReliabilityReason.None);

    public AirframeReliabilityStatus Status => Current.Condition.RequiresGrounding || InspectionStatus == AirframeInspectionStatus.InspectionDue
        ? AirframeReliabilityStatus.Unavailable
        : Damage == AirframeDamageState.Recorded ? AirframeReliabilityStatus.AttentionRequired : AirframeReliabilityStatus.Nominal;

    public static AirframeReliabilityAssessment Evaluate(AirframeStoreRecord current, AirframeServiceState serviceState, DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(current); ArgumentNullException.ThrowIfNull(serviceState);
        current.Validate(); serviceState.Validate();
        if (serviceState.AirframeId != current.Airframe.AirframeId || serviceState.UpdatedAt < current.Airframe.CreatedAt)
            throw new InvalidDataException("Reliability inputs do not belong to the same physical airframe.");
        if (evaluatedAt == default) throw new ArgumentOutOfRangeException(nameof(evaluatedAt));
        return new(current, serviceState, evaluatedAt);
    }
}
