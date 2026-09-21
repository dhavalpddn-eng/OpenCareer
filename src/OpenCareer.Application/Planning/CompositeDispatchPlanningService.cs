using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Planning;

public enum CompositeDispatchPlanningStatus
{
    Evaluated = 0,
    InsufficientData = 1
}

public enum CompositeDispatchPlanningReason
{
    FuelWeightPlanUnavailable = 0
}

public sealed record CompositeDispatchPlanningResult(
    CompositeDispatchPlanningStatus Status,
    CompositeDispatchPlanningReason? Reason,
    RouteFuelWeightPlanningReason? FuelWeightReason,
    RouteFuelPlanningReason? RouteFuelReason,
    CruisePerformancePlanningReason? CruisePerformanceReason,
    RouteFuelEstimate? RouteFuelEstimate,
    RouteFuelWeightPlan? FuelWeightPlan,
    DispatchFeasibilityResult? DispatchResult);

/// <summary>
/// Composes route/cruise fuel planning with the existing physical dispatch
/// evaluator. Fuel pounds and fuel type are owned by the Slice 17 plan; all
/// runway, weather, payload, range, and weight checks remain owned by the
/// existing dispatch pipeline.
/// </summary>
public sealed class CompositeDispatchPlanningService(
    RouteFuelWeightPlanningService fuelWeightPlanning,
    OperationDispatchPlanningService dispatchPlanning)
{
    public async Task<CompositeDispatchPlanningResult> EvaluateAsync(
        string aircraftId,
        string originIcao,
        string destinationIcao,
        CruisePerformancePlanningRequest cruiseRequest,
        RouteFuelPlanningRequest routeFuelRequest,
        OperationDispatchRequirements requirements,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(originIcao);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationIcao);
        ArgumentNullException.ThrowIfNull(cruiseRequest);
        ArgumentNullException.ThrowIfNull(routeFuelRequest);
        ArgumentNullException.ThrowIfNull(requirements);

        requirements.Validate();

        if (requirements.PlannedFuelPounds is not null)
        {
            throw new ArgumentException(
                "Composite dispatch owns PlannedFuelPounds through the Slice 17 fuel-weight plan.",
                nameof(requirements));
        }

        if (requirements.PlannedFuelTypeIndex is not null)
        {
            throw new ArgumentException(
                "Composite dispatch owns PlannedFuelTypeIndex through the Slice 17 fuel-weight plan.",
                nameof(requirements));
        }

        RouteFuelWeightPlanningResult fuelResult = await fuelWeightPlanning
            .PlanAsync(
                aircraftId,
                cruiseRequest,
                routeFuelRequest,
                cancellationToken)
            .ConfigureAwait(false);

        if (fuelResult.Status != RouteFuelWeightPlanningStatus.Planned
            || fuelResult.FuelWeightPlan is null)
        {
            return new(
                CompositeDispatchPlanningStatus.InsufficientData,
                CompositeDispatchPlanningReason.FuelWeightPlanUnavailable,
                fuelResult.Reason,
                fuelResult.RouteFuelReason,
                fuelResult.CruisePerformanceReason,
                fuelResult.RouteFuelEstimate,
                null,
                null);
        }

        OperationDispatchRequirements fueledRequirements =
            fuelResult.FuelWeightPlan.ApplyTo(requirements);

        DispatchFeasibilityResult dispatchResult = await dispatchPlanning
            .EvaluateAsync(
                aircraftId,
                originIcao,
                destinationIcao,
                fueledRequirements,
                cancellationToken)
            .ConfigureAwait(false);

        return new(
            CompositeDispatchPlanningStatus.Evaluated,
            null,
            null,
            null,
            null,
            fuelResult.RouteFuelEstimate,
            fuelResult.FuelWeightPlan,
            dispatchResult);
    }
}
