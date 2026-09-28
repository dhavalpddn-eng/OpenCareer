using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;

namespace OpenCareer.Domain.Planning;

public sealed record DispatchRunwayPerformanceConditions(
    double WeightPounds,
    double OutsideAirTemperatureCelsius,
    double PressureAltitudeFeet)
{
    public void Validate()
    {
        if (!double.IsFinite(WeightPounds) || WeightPounds <= 0)
            throw new ArgumentOutOfRangeException(nameof(WeightPounds));

        if (!double.IsFinite(OutsideAirTemperatureCelsius))
            throw new ArgumentOutOfRangeException(nameof(OutsideAirTemperatureCelsius));

        if (!double.IsFinite(PressureAltitudeFeet))
            throw new ArgumentOutOfRangeException(nameof(PressureAltitudeFeet));
    }
}

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
    DispatchWeatherLimits? WeatherLimits = null,
    DispatchRunwayPerformanceConditions? TakeoffPerformanceConditions = null,
    DispatchRunwayPerformanceConditions? LandingPerformanceConditions = null,
    int? PlannedFuelTypeIndex = null)
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

        if (PlannedFuelTypeIndex is { } fuelTypeIndex && fuelTypeIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(PlannedFuelTypeIndex));

        ValidatePercentage(RangeSafetyMarginPercent, nameof(RangeSafetyMarginPercent));
        ValidatePercentage(RunwayLengthSafetyMarginPercent, nameof(RunwayLengthSafetyMarginPercent));
        WeatherLimits?.Validate();
        TakeoffPerformanceConditions?.Validate();
        LandingPerformanceConditions?.Validate();
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

        if (maximumPayload is null
            && requirements.PayloadPounds > 0)
        {
            capabilityIssues.Add(new(
                DispatchFeasibilityReason.AircraftPayloadCapacityUnknown,
                AircraftField: AircraftRegistryField.MaximumPayloadPounds));
        }
        else if (maximumPayload is { } knownPayload
            && requirements.PayloadPounds > knownPayload)
        {
            capabilityIssues.Add(new(
                DispatchFeasibilityReason.PayloadExceedsAircraftMaximum,
                AircraftField: AircraftRegistryField.MaximumPayloadPounds,
                RequiredPounds: requirements.PayloadPounds,
                AvailablePounds: knownPayload));
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
                    requirements.PlannedFuelTypeIndex,
                    capabilityIssues,
                    ref hasDefiniteCapabilityFailure);
            }
        }

        ConditionedRunwayResolution conditionedRunway =
            ResolveConditionedRunwayPerformance(
                aircraft.RunwayPerformance,
                dispatchPerformance,
                requirements,
                origin,
                destination);

        DispatchFeasibilityResult runway = RunwayCompatibilityEvaluator.Evaluate(
            conditionedRunway.EffectivePerformance,
            origin,
            destination,
            requirements.RunwayLengthSafetyMarginPercent,
            originWeather,
            destinationWeather,
            requirements.WeatherLimits);

        IEnumerable<DispatchFeasibilityIssue> runwayIssues =
            SuppressBaselineLengthUnknowns(
                runway.Issues,
                conditionedRunway.SuppressTakeoffLengthUnknown,
                conditionedRunway.SuppressLandingLengthUnknown);

        DispatchFeasibilityStatus status =
            hasDefiniteCapabilityFailure
            || runway.Status == DispatchFeasibilityStatus.Infeasible
                ? DispatchFeasibilityStatus.Infeasible
                : capabilityIssues.Count > 0
                  || conditionedRunway.Issues.Count > 0
                  || runway.Status == DispatchFeasibilityStatus.InsufficientData
                    ? DispatchFeasibilityStatus.InsufficientData
                    : DispatchFeasibilityStatus.Feasible;

        return DispatchFeasibilityResult.Create(
            status,
            runway.OriginRunwayIdentifier,
            runway.DestinationRunwayIdentifier,
            capabilityIssues
                .Concat(conditionedRunway.Issues)
                .Concat(runwayIssues));
    }

    private static ConditionedRunwayResolution ResolveConditionedRunwayPerformance(
        AircraftRunwayPerformanceProfile? baseline,
        AircraftDispatchPerformanceProfile? dispatchPerformance,
        OperationDispatchRequirements requirements,
        AirportRecord origin,
        AirportRecord destination)
    {
        AircraftConditionedPerformanceProfile? conditioned =
            dispatchPerformance?.ConditionedPerformance;

        AircraftPerformanceGrid3D? takeoffTable =
            conditioned?.TakeoffTotalDistanceFeet;

        AircraftPerformanceGrid3D? landingTable =
            conditioned?.LandingTotalDistanceFeet;

        if (takeoffTable is null && landingTable is null)
        {
            return new(
                baseline,
                Array.Empty<DispatchFeasibilityIssue>(),
                SuppressTakeoffLengthUnknown: false,
                SuppressLandingLengthUnknown: false);
        }

        double? takeoffLength = baseline?.MinimumTakeoffRunwayFeet;
        double? landingLength = baseline?.MinimumLandingRunwayFeet;
        var issues = new List<DispatchFeasibilityIssue>();

        bool suppressTakeoffUnknown = takeoffTable is not null;
        bool suppressLandingUnknown = landingTable is not null;

        if (takeoffTable is not null)
        {
            takeoffLength = ResolveConditionedDistance(
                takeoffTable,
                requirements.TakeoffPerformanceConditions,
                DispatchEndpoint.Origin,
                origin.Icao,
                DispatchFeasibilityReason.AircraftTakeoffPerformanceConditionsMissing,
                DispatchFeasibilityReason.AircraftTakeoffPerformanceOutsideEnvelope,
                DispatchFeasibilityReason.AircraftTakeoffPerformanceValueInvalid,
                issues);
        }

        if (landingTable is not null)
        {
            landingLength = ResolveConditionedDistance(
                landingTable,
                requirements.LandingPerformanceConditions,
                DispatchEndpoint.Destination,
                destination.Icao,
                DispatchFeasibilityReason.AircraftLandingPerformanceConditionsMissing,
                DispatchFeasibilityReason.AircraftLandingPerformanceOutsideEnvelope,
                DispatchFeasibilityReason.AircraftLandingPerformanceValueInvalid,
                issues);
        }

        AircraftRunwayPerformanceProfile effective =
            baseline is null
                ? new(
                    MinimumTakeoffRunwayFeet: takeoffLength,
                    MinimumLandingRunwayFeet: landingLength,
                    MinimumRunwayWidthFeet: null,
                    SupportedSurfaces: null,
                    Confidence: dispatchPerformance!.Confidence,
                    Source: dispatchPerformance.Source)
                : baseline with
                {
                    MinimumTakeoffRunwayFeet = takeoffLength,
                    MinimumLandingRunwayFeet = landingLength
                };

        effective.Validate();

        return new(
            effective,
            issues,
            suppressTakeoffUnknown,
            suppressLandingUnknown);
    }

    private static double? ResolveConditionedDistance(
        AircraftPerformanceGrid3D table,
        DispatchRunwayPerformanceConditions? conditions,
        DispatchEndpoint endpoint,
        string airportIcao,
        DispatchFeasibilityReason missingReason,
        DispatchFeasibilityReason outsideEnvelopeReason,
        DispatchFeasibilityReason invalidValueReason,
        ICollection<DispatchFeasibilityIssue> issues)
    {
        table.Validate();

        if (conditions is null)
        {
            issues.Add(new(
                missingReason,
                endpoint,
                airportIcao,
                AircraftField: AircraftRegistryField.DispatchPerformance));

            return null;
        }

        conditions.Validate();

        double? distance = table.Interpolate(
            conditions.WeightPounds,
            conditions.OutsideAirTemperatureCelsius,
            conditions.PressureAltitudeFeet);

        if (distance is { } knownDistance)
        {
            // A zero direct-distance cell cannot represent a usable runway requirement.
            // Treat malformed source data as unknown rather than weakening screening.
            if (knownDistance > 0)
                return knownDistance;

            issues.Add(new(
                invalidValueReason,
                endpoint,
                airportIcao,
                RequiredFeet: knownDistance,
                AircraftField: AircraftRegistryField.DispatchPerformance,
                RequiredPounds: conditions.WeightPounds,
                OutsideAirTemperatureCelsius: conditions.OutsideAirTemperatureCelsius,
                PressureAltitudeFeet: conditions.PressureAltitudeFeet));

            return null;
        }

        issues.Add(new(
            outsideEnvelopeReason,
            endpoint,
            airportIcao,
            AircraftField: AircraftRegistryField.DispatchPerformance,
            RequiredPounds: conditions.WeightPounds,
            OutsideAirTemperatureCelsius: conditions.OutsideAirTemperatureCelsius,
            PressureAltitudeFeet: conditions.PressureAltitudeFeet));

        return null;
    }

    private static IEnumerable<DispatchFeasibilityIssue> SuppressBaselineLengthUnknowns(
        IEnumerable<DispatchFeasibilityIssue> issues,
        bool suppressTakeoff,
        bool suppressLanding) =>
        issues.Where(issue =>
            !(suppressTakeoff
                && issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.AircraftTakeoffLengthUnknown)
            && !(suppressLanding
                && issue.Endpoint == DispatchEndpoint.Destination
                && issue.Reason == DispatchFeasibilityReason.AircraftLandingLengthUnknown));

    private static void EvaluateWeightAndFuel(
        AircraftDispatchPerformanceProfile performance,
        double payloadPounds,
        double plannedFuelPounds,
        int? plannedFuelTypeIndex,
        ICollection<DispatchFeasibilityIssue> issues,
        ref bool hasDefiniteFailure)
    {
        double? emptyWeight = performance.OperatingEmptyWeightPounds;
        double? maximumTakeoffWeight = performance.MaximumTakeoffWeightPounds;
        double? maximumFuelWeight =
            performance.ResolveMaximumFuelWeightPounds(plannedFuelTypeIndex);

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

    private sealed record ConditionedRunwayResolution(
        AircraftRunwayPerformanceProfile? EffectivePerformance,
        IReadOnlyList<DispatchFeasibilityIssue> Issues,
        bool SuppressTakeoffLengthUnknown,
        bool SuppressLandingLengthUnknown);

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
