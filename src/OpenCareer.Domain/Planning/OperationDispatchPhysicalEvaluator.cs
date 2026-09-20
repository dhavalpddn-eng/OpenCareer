using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;

namespace OpenCareer.Domain.Planning;

/// <summary>
/// Physical requirements for one operation. RequiredRangeNauticalMiles must already
/// include route, alternate, reserve, reposition, or contingency distance selected
/// by the caller. Optional percentage margins are explicit extra screening margins.
/// Supplying PlannedFuelPounds activates the weight/fuel gate.
/// </summary>
public sealed record OperationDispatchRequirements(
    double PayloadPounds,
    double RequiredRangeNauticalMiles,
    double? PlannedFuelPounds = null,
    double RangeSafetyMarginPercent = 0,
    double RunwayLengthSafetyMarginPercent = 0,
    DispatchWeatherLimits? WeatherLimits = null)
{
    public void Validate()
    {
        if (!double.IsFinite(PayloadPounds) || PayloadPounds < 0)
            throw new ArgumentOutOfRangeException(nameof(PayloadPounds));

        if (!double.IsFinite(RequiredRangeNauticalMiles) || RequiredRangeNauticalMiles < 0)
            throw new ArgumentOutOfRangeException(nameof(RequiredRangeNauticalMiles));

        if (PlannedFuelPounds is { } fuel
            && (!double.IsFinite(fuel) || fuel < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(PlannedFuelPounds));
        }

        ValidatePercentage(RangeSafetyMarginPercent, nameof(RangeSafetyMarginPercent));
        ValidatePercentage(RunwayLengthSafetyMarginPercent, nameof(RunwayLengthSafetyMarginPercent));
        WeatherLimits?.Validate();
    }

    private static void ValidatePercentage(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 100)
            throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>
/// Deterministic physical screening for an operation. When authoritative
/// dispatch-performance data exists it can enforce loaded takeoff weight, fuel
/// capacity and a payload-range envelope. Weather, licenses, ratings, economic
/// authority and government/military authorization remain separate gates.
/// </summary>
public static class OperationDispatchPhysicalEvaluator
{
    public static DispatchFeasibilityResult Evaluate(
        AircraftRegistryResolution aircraft,
        OperationDispatchRequirements requirements,
        AirportRecord origin,
        AirportRecord destination,
        AirportDispatchWeatherObservation? originWeather = null,
        AirportDispatchWeatherObservation? destinationWeather = null)
    {
        ArgumentNullException.ThrowIfNull(aircraft);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraft.CanonicalAircraftId);

        requirements.Validate();
        origin.Validate();
        destination.Validate();

        var capabilityIssues = new List<DispatchFeasibilityIssue>();
        bool hasDefiniteCapabilityFailure = false;

        double? maximumPayload = aircraft.CapabilityValues.MaximumPayloadPounds;
        ValidateResolvedNonNegative(maximumPayload, AircraftRegistryField.MaximumPayloadPounds);

        if (maximumPayload is null)
        {
            capabilityIssues.Add(new(
                DispatchFeasibilityReason.AircraftPayloadCapacityUnknown,
                AircraftField: AircraftRegistryField.MaximumPayloadPounds));
        }
        else if (requirements.PayloadPounds > maximumPayload.Value)
        {
            capabilityIssues.Add(new(
                DispatchFeasibilityReason.PayloadExceedsAircraftMaximum,
                AircraftField: AircraftRegistryField.MaximumPayloadPounds,
                RequiredPounds: requirements.PayloadPounds,
                AvailablePounds: maximumPayload.Value));
            hasDefiniteCapabilityFailure = true;
        }

        double requiredRangeWithMargin = ApplySafetyMargin(
            requirements.RequiredRangeNauticalMiles,
            requirements.RangeSafetyMarginPercent);

        double? maximumRange = aircraft.CapabilityValues.MaximumRangeNauticalMiles;
        ValidateResolvedNonNegative(maximumRange, AircraftRegistryField.MaximumRangeNauticalMiles);

        if (maximumRange is null)
        {
            capabilityIssues.Add(new(
                DispatchFeasibilityReason.AircraftRangeUnknown,
                AircraftField: AircraftRegistryField.MaximumRangeNauticalMiles,
                RequiredNauticalMiles: requiredRangeWithMargin));
        }
        else if (requiredRangeWithMargin > maximumRange.Value)
        {
            capabilityIssues.Add(new(
                DispatchFeasibilityReason.RangeExceedsAircraftMaximum,
                AircraftField: AircraftRegistryField.MaximumRangeNauticalMiles,
                RequiredNauticalMiles: requiredRangeWithMargin,
                AvailableNauticalMiles: maximumRange.Value));
            hasDefiniteCapabilityFailure = true;
        }

        AircraftDispatchPerformanceProfile? dispatchPerformance =
            aircraft.DispatchPerformance;

        if (dispatchPerformance is not null)
        {
            dispatchPerformance.Validate();

            if (dispatchPerformance.PayloadRangeEnvelope is { Count: > 0 } envelope)
            {
                double? payloadLimitedRange = ResolvePayloadLimitedRange(
                    envelope,
                    requirements.PayloadPounds);

                if (payloadLimitedRange is null)
                {
                    capabilityIssues.Add(new(
                        DispatchFeasibilityReason.PayloadRangeEnvelopeDoesNotCoverPayload,
                        AircraftField: AircraftRegistryField.DispatchPerformance,
                        RequiredPounds: requirements.PayloadPounds));
                }
                else if (requiredRangeWithMargin > payloadLimitedRange.Value)
                {
                    capabilityIssues.Add(new(
                        DispatchFeasibilityReason.PayloadRangeExceeded,
                        AircraftField: AircraftRegistryField.DispatchPerformance,
                        RequiredPounds: requirements.PayloadPounds,
                        RequiredNauticalMiles: requiredRangeWithMargin,
                        AvailableNauticalMiles: payloadLimitedRange.Value));
                    hasDefiniteCapabilityFailure = true;
                }
            }
        }

        if (requirements.PlannedFuelPounds is { } plannedFuel)
        {
            if (dispatchPerformance is null)
            {
                capabilityIssues.Add(new(
                    DispatchFeasibilityReason.AircraftDispatchPerformanceUnknown,
                    AircraftField: AircraftRegistryField.DispatchPerformance));
            }
            else
            {
                EvaluateWeightAndFuel(
                    dispatchPerformance,
                    requirements.PayloadPounds,
                    plannedFuel,
                    capabilityIssues,
                    ref hasDefiniteCapabilityFailure);
            }
        }

        DispatchFeasibilityResult runway = RunwayCompatibilityEvaluator.Evaluate(
            aircraft.RunwayPerformance,
            origin,
            destination,
            requirements.RunwayLengthSafetyMarginPercent,
            originWeather,
            destinationWeather,
            requirements.WeatherLimits);

        DispatchFeasibilityStatus status =
            hasDefiniteCapabilityFailure
            || runway.Status == DispatchFeasibilityStatus.Infeasible
                ? DispatchFeasibilityStatus.Infeasible
                : capabilityIssues.Count > 0
                  || runway.Status == DispatchFeasibilityStatus.InsufficientData
                    ? DispatchFeasibilityStatus.InsufficientData
                    : DispatchFeasibilityStatus.Feasible;

        return DispatchFeasibilityResult.Create(
            status,
            runway.OriginRunwayIdentifier,
            runway.DestinationRunwayIdentifier,
            capabilityIssues.Concat(runway.Issues));
    }

    private static void EvaluateWeightAndFuel(
        AircraftDispatchPerformanceProfile performance,
        double payloadPounds,
        double plannedFuelPounds,
        ICollection<DispatchFeasibilityIssue> issues,
        ref bool hasDefiniteFailure)
    {
        double? emptyWeight = performance.OperatingEmptyWeightPounds;
        double? maximumTakeoffWeight = performance.MaximumTakeoffWeightPounds;
        double? maximumFuelWeight = performance.MaximumFuelWeightPounds;

        if (emptyWeight is null)
        {
            issues.Add(new(
                DispatchFeasibilityReason.AircraftOperatingEmptyWeightUnknown,
                AircraftField: AircraftRegistryField.DispatchPerformance));
        }

        if (maximumTakeoffWeight is null)
        {
            issues.Add(new(
                DispatchFeasibilityReason.AircraftMaximumTakeoffWeightUnknown,
                AircraftField: AircraftRegistryField.DispatchPerformance));
        }

        if (maximumFuelWeight is null)
        {
            issues.Add(new(
                DispatchFeasibilityReason.AircraftMaximumFuelWeightUnknown,
                AircraftField: AircraftRegistryField.DispatchPerformance));
        }
        else if (plannedFuelPounds > maximumFuelWeight.Value)
        {
            issues.Add(new(
                DispatchFeasibilityReason.FuelExceedsAircraftMaximum,
                AircraftField: AircraftRegistryField.DispatchPerformance,
                RequiredPounds: plannedFuelPounds,
                AvailablePounds: maximumFuelWeight.Value));
            hasDefiniteFailure = true;
        }

        if (emptyWeight is null || maximumTakeoffWeight is null)
            return;

        double plannedTakeoffWeight =
            emptyWeight.Value + payloadPounds + plannedFuelPounds;

        if (!double.IsFinite(plannedTakeoffWeight))
            throw new ArgumentOutOfRangeException(nameof(plannedFuelPounds));

        if (plannedTakeoffWeight > maximumTakeoffWeight.Value)
        {
            issues.Add(new(
                DispatchFeasibilityReason.TakeoffWeightExceedsMaximum,
                AircraftField: AircraftRegistryField.DispatchPerformance,
                RequiredPounds: plannedTakeoffWeight,
                AvailablePounds: maximumTakeoffWeight.Value));
            hasDefiniteFailure = true;
        }
    }

    private static double? ResolvePayloadLimitedRange(
        IReadOnlyList<AircraftPayloadRangePoint> envelope,
        double payloadPounds)
    {
        AircraftPayloadRangePoint[] points = envelope
            .OrderBy(static point => point.PayloadPounds)
            .ToArray();

        if (payloadPounds <= points[0].PayloadPounds)
            return points[0].MaximumRangeNauticalMiles;

        if (payloadPounds > points[^1].PayloadPounds)
            return null;

        for (int i = 1; i < points.Length; i++)
        {
            AircraftPayloadRangePoint upper = points[i];
            if (payloadPounds > upper.PayloadPounds)
                continue;

            AircraftPayloadRangePoint lower = points[i - 1];
            double payloadSpan = upper.PayloadPounds - lower.PayloadPounds;
            double ratio = (payloadPounds - lower.PayloadPounds) / payloadSpan;

            return lower.MaximumRangeNauticalMiles
                + ((upper.MaximumRangeNauticalMiles
                    - lower.MaximumRangeNauticalMiles) * ratio);
        }

        return null;
    }

    private static double ApplySafetyMargin(double value, double percentage) =>
        value * (1 + (percentage / 100d));

    private static void ValidateResolvedNonNegative(
        double? value,
        AircraftRegistryField field)
    {
        if (value is { } number && (!double.IsFinite(number) || number < 0))
        {
            throw new ArgumentException(
                $"Resolved aircraft field {field} contains an invalid value.",
                nameof(value));
        }
    }
}
