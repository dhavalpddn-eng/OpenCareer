using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;

namespace OpenCareer.Domain.Planning;

/// <summary>
/// Physical requirements for one operation. RequiredRangeNauticalMiles must already
/// include whatever route, alternate, reserve, reposition, or contingency distance
/// the caller has decided to require. This layer does not invent fuel assumptions.
/// </summary>
public sealed record OperationDispatchRequirements(
    double PayloadPounds,
    double RequiredRangeNauticalMiles)
{
    public void Validate()
    {
        if (!double.IsFinite(PayloadPounds) || PayloadPounds < 0)
            throw new ArgumentOutOfRangeException(nameof(PayloadPounds));

        if (!double.IsFinite(RequiredRangeNauticalMiles) || RequiredRangeNauticalMiles < 0)
            throw new ArgumentOutOfRangeException(nameof(RequiredRangeNauticalMiles));
    }
}

/// <summary>
/// Deterministic physical screening for an operation. This is not final dispatch
/// clearance: payload/range maxima are independent envelope limits and do not model
/// payload-range tradeoff, fuel loading, weight, weather, licenses, ratings, or
/// government/military authorization.
/// </summary>
public static class OperationDispatchPhysicalEvaluator
{
    public static DispatchFeasibilityResult Evaluate(
        AircraftRegistryResolution aircraft,
        OperationDispatchRequirements requirements,
        AirportRecord origin,
        AirportRecord destination)
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

        double? maximumRange = aircraft.CapabilityValues.MaximumRangeNauticalMiles;
        ValidateResolvedNonNegative(maximumRange, AircraftRegistryField.MaximumRangeNauticalMiles);

        if (maximumRange is null)
        {
            capabilityIssues.Add(new(
                DispatchFeasibilityReason.AircraftRangeUnknown,
                AircraftField: AircraftRegistryField.MaximumRangeNauticalMiles));
        }
        else if (requirements.RequiredRangeNauticalMiles > maximumRange.Value)
        {
            capabilityIssues.Add(new(
                DispatchFeasibilityReason.RangeExceedsAircraftMaximum,
                AircraftField: AircraftRegistryField.MaximumRangeNauticalMiles,
                RequiredNauticalMiles: requirements.RequiredRangeNauticalMiles,
                AvailableNauticalMiles: maximumRange.Value));
            hasDefiniteCapabilityFailure = true;
        }

        DispatchFeasibilityResult runway = RunwayCompatibilityEvaluator.Evaluate(
            aircraft.RunwayPerformance,
            origin,
            destination);

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
