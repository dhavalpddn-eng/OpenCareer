using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Planning;

/// <summary>
/// Resolves an aircraft through the provider-neutral registry and delegates
/// deterministic cruise table lookup to the Domain planner.
/// </summary>
public sealed class CruisePerformancePlanningService(
    IAircraftRegistrySource aircraftRegistry)
{
    public async Task<CruisePerformancePlanningResult> PlanAsync(
        string aircraftId,
        CruisePerformancePlanningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        AircraftRegistryResolution? aircraft = await aircraftRegistry
            .FindAircraftAsync(aircraftId, cancellationToken)
            .ConfigureAwait(false);

        if (aircraft is null)
        {
            return CruisePerformancePlanningResult.InsufficientData(
                CruisePerformancePlanningReason.AircraftNotFound);
        }

        return CruisePerformancePlanner.Plan(aircraft, request);
    }
}
