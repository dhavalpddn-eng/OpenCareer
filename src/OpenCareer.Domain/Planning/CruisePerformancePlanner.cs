using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Domain.Planning;

public enum CruisePerformancePlanningStatus
{
    Planned = 0,
    InsufficientData = 1
}

public enum CruisePerformancePlanningReason
{
    AircraftNotFound = 0,
    AircraftDispatchPerformanceUnknown,
    AircraftConditionedPerformanceUnknown,
    CruiseProfileNotFound,
    FuelTypeMismatch,
    TrueAirspeedTableUnavailable,
    FuelConsumptionTableUnavailable,
    TrueAirspeedOutsideEnvelope,
    FuelConsumptionOutsideEnvelope,
    TrueAirspeedValueInvalid
}

public sealed record CruisePerformancePlanningRequest(
    int ProfileIndex,
    int FuelTypeIndex,
    double PlannedCruiseWeightPounds,
    double IsaDeviationCelsius,
    double PressureAltitudeFeet)
{
    public void Validate()
    {
        if (ProfileIndex is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(ProfileIndex));

        if (FuelTypeIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(FuelTypeIndex));

        if (!double.IsFinite(PlannedCruiseWeightPounds)
            || PlannedCruiseWeightPounds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PlannedCruiseWeightPounds));
        }

        if (!double.IsFinite(IsaDeviationCelsius))
            throw new ArgumentOutOfRangeException(nameof(IsaDeviationCelsius));

        if (!double.IsFinite(PressureAltitudeFeet))
            throw new ArgumentOutOfRangeException(nameof(PressureAltitudeFeet));
    }
}

public sealed record CruisePerformancePlan(
    int ProfileIndex,
    string? ProfileName,
    int FuelTypeIndex,
    double PlannedCruiseWeightPounds,
    double IsaDeviationCelsius,
    double PressureAltitudeFeet,
    double TrueAirspeedKnots,
    double FuelConsumptionGallonsPerHour,
    double? TargetMach)
{
    public void Validate()
    {
        if (ProfileIndex is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(ProfileIndex));

        if (ProfileName is not null && string.IsNullOrWhiteSpace(ProfileName))
            throw new ArgumentException("Cruise profile name must be non-empty when supplied.", nameof(ProfileName));

        if (FuelTypeIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(FuelTypeIndex));

        if (!double.IsFinite(PlannedCruiseWeightPounds)
            || PlannedCruiseWeightPounds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PlannedCruiseWeightPounds));
        }

        if (!double.IsFinite(IsaDeviationCelsius))
            throw new ArgumentOutOfRangeException(nameof(IsaDeviationCelsius));

        if (!double.IsFinite(PressureAltitudeFeet))
            throw new ArgumentOutOfRangeException(nameof(PressureAltitudeFeet));

        if (!double.IsFinite(TrueAirspeedKnots) || TrueAirspeedKnots <= 0)
            throw new ArgumentOutOfRangeException(nameof(TrueAirspeedKnots));

        if (!double.IsFinite(FuelConsumptionGallonsPerHour)
            || FuelConsumptionGallonsPerHour < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FuelConsumptionGallonsPerHour));
        }

        if (TargetMach is { } mach && (!double.IsFinite(mach) || mach < 0))
            throw new ArgumentOutOfRangeException(nameof(TargetMach));
    }
}

public sealed record CruisePerformancePlanningResult
{
    private CruisePerformancePlanningResult(
        CruisePerformancePlanningStatus status,
        CruisePerformancePlanningReason? reason,
        CruisePerformancePlan? plan)
    {
        Status = status;
        Reason = reason;
        Plan = plan;
    }

    public CruisePerformancePlanningStatus Status { get; }
    public CruisePerformancePlanningReason? Reason { get; }
    public CruisePerformancePlan? Plan { get; }

    public static CruisePerformancePlanningResult Planned(
        CruisePerformancePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();

        return new(
            CruisePerformancePlanningStatus.Planned,
            null,
            plan);
    }

    public static CruisePerformancePlanningResult InsufficientData(
        CruisePerformancePlanningReason reason)
    {
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));

        return new(
            CruisePerformancePlanningStatus.InsufficientData,
            reason,
            null);
    }
}

/// <summary>
/// Resolves one explicitly selected MSFS cruise profile at one explicitly
/// predicted cruise condition. It never chooses a profile, substitutes a fuel
/// type, or extrapolates beyond the supplied performance grids.
/// </summary>
public static class CruisePerformancePlanner
{
    public static CruisePerformancePlanningResult Plan(
        AircraftRegistryResolution aircraft,
        CruisePerformancePlanningRequest request)
    {
        ArgumentNullException.ThrowIfNull(aircraft);
        ArgumentNullException.ThrowIfNull(request);

        request.Validate();

        AircraftDispatchPerformanceProfile? dispatchPerformance =
            aircraft.DispatchPerformance;

        if (dispatchPerformance is null)
        {
            return CruisePerformancePlanningResult.InsufficientData(
                CruisePerformancePlanningReason.AircraftDispatchPerformanceUnknown);
        }

        dispatchPerformance.Validate();

        AircraftConditionedPerformanceProfile? conditionedPerformance =
            dispatchPerformance.ConditionedPerformance;

        if (conditionedPerformance is null)
        {
            return CruisePerformancePlanningResult.InsufficientData(
                CruisePerformancePlanningReason.AircraftConditionedPerformanceUnknown);
        }

        AircraftCruisePerformanceProfile? profile =
            conditionedPerformance.CruiseProfiles
                .SingleOrDefault(candidate =>
                    candidate.Index == request.ProfileIndex);

        if (profile is null)
        {
            return CruisePerformancePlanningResult.InsufficientData(
                CruisePerformancePlanningReason.CruiseProfileNotFound);
        }

        profile.Validate();

        if (profile.FuelTypeIndex != request.FuelTypeIndex)
        {
            return CruisePerformancePlanningResult.InsufficientData(
                CruisePerformancePlanningReason.FuelTypeMismatch);
        }

        if (profile.TrueAirspeedKnots is null)
        {
            return CruisePerformancePlanningResult.InsufficientData(
                CruisePerformancePlanningReason.TrueAirspeedTableUnavailable);
        }

        if (profile.FuelConsumptionGallonsPerHour is null)
        {
            return CruisePerformancePlanningResult.InsufficientData(
                CruisePerformancePlanningReason.FuelConsumptionTableUnavailable);
        }

        double? trueAirspeed = profile.TrueAirspeedKnots.Interpolate(
            request.PlannedCruiseWeightPounds,
            request.IsaDeviationCelsius,
            request.PressureAltitudeFeet);

        if (trueAirspeed is null)
        {
            return CruisePerformancePlanningResult.InsufficientData(
                CruisePerformancePlanningReason.TrueAirspeedOutsideEnvelope);
        }

        double? fuelConsumption =
            profile.FuelConsumptionGallonsPerHour.Interpolate(
                request.PlannedCruiseWeightPounds,
                request.IsaDeviationCelsius,
                request.PressureAltitudeFeet);

        if (fuelConsumption is null)
        {
            return CruisePerformancePlanningResult.InsufficientData(
                CruisePerformancePlanningReason.FuelConsumptionOutsideEnvelope);
        }

        if (trueAirspeed.Value <= 0)
        {
            return CruisePerformancePlanningResult.InsufficientData(
                CruisePerformancePlanningReason.TrueAirspeedValueInvalid);
        }

        return CruisePerformancePlanningResult.Planned(
            new CruisePerformancePlan(
                profile.Index,
                profile.ProfileName,
                profile.FuelTypeIndex,
                request.PlannedCruiseWeightPounds,
                request.IsaDeviationCelsius,
                request.PressureAltitudeFeet,
                trueAirspeed.Value,
                fuelConsumption.Value,
                profile.TargetMach));
    }
}
