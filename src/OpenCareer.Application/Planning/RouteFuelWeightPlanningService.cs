using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Planning;

public enum RouteFuelWeightPlanningStatus
{
    Planned = 0,
    InsufficientData = 1
}

public enum RouteFuelWeightPlanningReason
{
    RouteFuelUnavailable = 0,
    AircraftNotFound,
    AircraftDispatchPerformanceUnknown,
    FuelDensityDataUnavailable,
    FuelTypeDensityUnavailable
}

public sealed record RouteFuelWeightPlanningResult(
    RouteFuelWeightPlanningStatus Status,
    RouteFuelWeightPlanningReason? Reason,
    RouteFuelPlanningReason? RouteFuelReason,
    CruisePerformancePlanningReason? CruisePerformanceReason,
    RouteFuelEstimate? RouteFuelEstimate,
    RouteFuelWeightPlan? FuelWeightPlan);

/// <summary>
/// Resolves Slice 16 route fuel, then converts its required gallons to pounds
/// using the exact selected fuel-type density from the aircraft profile.
/// </summary>
public sealed class RouteFuelWeightPlanningService(
    IAircraftRegistrySource aircraftRegistry,
    RouteFuelPlanningService routeFuelPlanning)
{
    public async Task<RouteFuelWeightPlanningResult> PlanAsync(
        string aircraftId,
        CruisePerformancePlanningRequest cruiseRequest,
        RouteFuelPlanningRequest routeFuelRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        ArgumentNullException.ThrowIfNull(cruiseRequest);
        ArgumentNullException.ThrowIfNull(routeFuelRequest);

        RouteFuelPlanningResult routeResult = await routeFuelPlanning
            .PlanAsync(
                aircraftId,
                cruiseRequest,
                routeFuelRequest,
                cancellationToken)
            .ConfigureAwait(false);

        if (routeResult.Status != RouteFuelPlanningStatus.Planned
            || routeResult.Estimate is null)
        {
            return new(
                RouteFuelWeightPlanningStatus.InsufficientData,
                RouteFuelWeightPlanningReason.RouteFuelUnavailable,
                routeResult.Reason,
                routeResult.CruisePerformanceReason,
                null,
                null);
        }

        AircraftRegistryResolution? aircraft = await aircraftRegistry
            .FindAircraftAsync(aircraftId, cancellationToken)
            .ConfigureAwait(false);

        if (aircraft is null)
        {
            return new(
                RouteFuelWeightPlanningStatus.InsufficientData,
                RouteFuelWeightPlanningReason.AircraftNotFound,
                null,
                null,
                routeResult.Estimate,
                null);
        }

        if (aircraft.DispatchPerformance is null)
        {
            return new(
                RouteFuelWeightPlanningStatus.InsufficientData,
                RouteFuelWeightPlanningReason.AircraftDispatchPerformanceUnknown,
                null,
                null,
                routeResult.Estimate,
                null);
        }

        FuelWeightConversionResult conversion = RouteFuelWeightBridge.Convert(
            routeResult.Estimate,
            aircraft.DispatchPerformance);

        if (conversion.Status != FuelWeightConversionStatus.Converted
            || conversion.Plan is null)
        {
            RouteFuelWeightPlanningReason reason = conversion.Reason switch
            {
                FuelWeightConversionReason.FuelTypeDensityUnavailable =>
                    RouteFuelWeightPlanningReason.FuelTypeDensityUnavailable,
                _ => RouteFuelWeightPlanningReason.FuelDensityDataUnavailable
            };

            return new(
                RouteFuelWeightPlanningStatus.InsufficientData,
                reason,
                null,
                null,
                routeResult.Estimate,
                null);
        }

        return new(
            RouteFuelWeightPlanningStatus.Planned,
            null,
            null,
            null,
            routeResult.Estimate,
            conversion.Plan);
    }
}
