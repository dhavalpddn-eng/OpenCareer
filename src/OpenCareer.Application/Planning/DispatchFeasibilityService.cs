using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Planning;

public interface IAircraftRegistrySource
{
    Task<AircraftRegistryRecord?> FindAircraftAsync(
        string aircraftId,
        CancellationToken cancellationToken = default);
}

public interface IAirportDataSource
{
    Task<AirportRecord?> FindAirportAsync(
        string icao,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provider-neutral orchestration boundary for baseline physical feasibility.
/// Later MSFS-installed-aircraft and airport-data adapters implement the source
/// interfaces; external aviation APIs remain reference/enrichment inputs rather
/// than gameplay authority.
/// </summary>
public sealed class DispatchFeasibilityService(
    IAircraftRegistrySource aircraftRegistry,
    IAirportDataSource airportData)
{
    public async Task<DispatchFeasibilityResult> EvaluateAsync(
        string aircraftId,
        string originIcao,
        string destinationIcao,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(originIcao);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationIcao);

        AircraftRegistryRecord? aircraft = await aircraftRegistry
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
            missing.Add(new(DispatchFeasibilityReason.AircraftNotFound));

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

        return RunwayCompatibilityEvaluator.Evaluate(
            aircraft!,
            origin!,
            destination!);
    }
}
