using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Planning;

/// <summary>
/// Composes Slice 15 cruise performance lookup with deterministic route/fuel
/// arithmetic. It preserves Slice 15 failure reasons and introduces no fallback
/// cruise profile, fuel type, or performance data.
/// </summary>
public sealed class RouteFuelPlanningService(
    CruisePerformancePlanningService cruisePerformancePlanning)
{
    public async Task<RouteFuelPlanningResult> PlanAsync(
        string aircraftId,
        CruisePerformancePlanningRequest cruiseRequest,
        RouteFuelPlanningRequest routeFuelRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        ArgumentNullException.ThrowIfNull(cruiseRequest);
        ArgumentNullException.ThrowIfNull(routeFuelRequest);

        cruiseRequest.Validate();
        routeFuelRequest.Validate();

        CruisePerformancePlanningResult cruiseResult =
            await cruisePerformancePlanning
                .PlanAsync(aircraftId, cruiseRequest, cancellationToken)
                .ConfigureAwait(false);

        if (cruiseResult.Status != CruisePerformancePlanningStatus.Planned
            || cruiseResult.Plan is null)
        {
            return RouteFuelPlanningResult.InsufficientData(
                RouteFuelPlanningReason.CruisePerformanceUnavailable,
                cruiseResult.Reason);
        }

        return RouteFuelPlanningResult.Planned(
            RouteFuelEstimator.Estimate(
                cruiseResult.Plan,
                routeFuelRequest));
    }
}
