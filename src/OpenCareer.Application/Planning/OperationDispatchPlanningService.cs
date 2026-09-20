using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Planning;

/// <summary>
/// Loads provider-neutral aircraft and airport data for one physical operation
/// screening. Career qualifications, weather, authorization, economics and Jobs
/// generation remain separate gates.
/// </summary>
public sealed class OperationDispatchPlanningService(
    IAircraftRegistrySource aircraftRegistry,
    IAirportDataSource airportData)
{
    public async Task<DispatchFeasibilityResult> EvaluateAsync(
        string aircraftId,
        string originIcao,
        string destinationIcao,
        OperationDispatchRequirements requirements,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(originIcao);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationIcao);
        ArgumentNullException.ThrowIfNull(requirements);
        requirements.Validate();

        AircraftRegistryResolution? aircraft = await aircraftRegistry
            .FindAircraftAsync(aircraftId, cancellationToken)
            .ConfigureAwait(false);

        AirportRecord? origin = await airportData
            .FindAirportAsync(originIcao, cancellationToken)
            .ConfigureAwait(false);

        AirportRecord? destination = string.Equals(
            originIcao,
            destinationIcao,
            StringComparison.OrdinalIgnoreCase)
            ? origin
            : await airportData
                .FindAirportAsync(destinationIcao, cancellationToken)
                .ConfigureAwait(false);

        var missing = new List<DispatchFeasibilityIssue>();

        if (aircraft is null)
        {
            missing.Add(new(
                DispatchFeasibilityReason.AircraftNotFound));
        }

        if (origin is null)
        {
            missing.Add(new(
                DispatchFeasibilityReason.AirportNotFound,
                DispatchEndpoint.Origin,
                originIcao));
        }

        if (destination is null)
        {
            missing.Add(new(
                DispatchFeasibilityReason.AirportNotFound,
                DispatchEndpoint.Destination,
                destinationIcao));
        }

        if (missing.Count > 0)
        {
            return DispatchFeasibilityResult.Create(
                DispatchFeasibilityStatus.InsufficientData,
                null,
                null,
                missing);
        }

        return OperationDispatchPhysicalEvaluator.Evaluate(
            aircraft!,
            requirements,
            origin!,
            destination!);
    }
}
