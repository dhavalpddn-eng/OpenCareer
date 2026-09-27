using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Planning;

public interface IAircraftRegistrySource
{
    Task<AircraftRegistryResolution?> FindAircraftAsync(
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
/// Aircraft and airport providers stay behind Application contracts. Local
/// simulator airport/runway observations outrank reference data, and external
/// aviation sources remain fallback/reference inputs rather than gameplay authority.
/// </summary>
public sealed class DispatchFeasibilityService(
    IAircraftRegistrySource aircraftRegistry,
    IAirportDataSource airportData,
    IAircraftAvailabilityStore? aircraftAvailability = null)
{
    public Task<DispatchFeasibilityResult> EvaluateAsync(
        string aircraftId,
        string originIcao,
        string destinationIcao,
        CancellationToken cancellationToken = default) =>
        EvaluateCoreAsync(
            aircraftId,
            originIcao,
            destinationIcao,
            reservationId: null,
            cancellationToken);

    public Task<DispatchFeasibilityResult> EvaluateAsync(
        string aircraftId,
        string originIcao,
        string destinationIcao,
        string reservationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationId);

        return EvaluateCoreAsync(
            aircraftId,
            originIcao,
            destinationIcao,
            reservationId.Trim(),
            cancellationToken);
    }

    private async Task<DispatchFeasibilityResult> EvaluateCoreAsync(
        string aircraftId,
        string originIcao,
        string destinationIcao,
        string? reservationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(originIcao);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationIcao);

        AircraftRegistryResolution? resolution = await aircraftRegistry
            .FindAircraftAsync(aircraftId, cancellationToken)
            .ConfigureAwait(false);

        if (resolution is not null
            && resolution.InstallationStatus != AircraftInstallationStatus.Installed)
        {
            return DispatchFeasibilityResult.Create(
                DispatchFeasibilityStatus.Infeasible,
                null,
                null,
                [new(DispatchFeasibilityReason.AircraftNotInstalled)]);
        }

        if (resolution is not null && aircraftAvailability is not null)
        {
            AircraftAvailabilityState? availability = await aircraftAvailability
                .FindAsync(resolution.CanonicalAircraftId, cancellationToken)
                .ConfigureAwait(false);

            if (availability?.Status == AircraftAvailabilityStatus.Unavailable)
            {
                bool heldByCaller =
                    availability.ReservationId is not null
                    && reservationId is not null
                    && string.Equals(
                        availability.ReservationId,
                        reservationId,
                        StringComparison.Ordinal);

                if (!heldByCaller)
                {
                    return DispatchFeasibilityResult.Create(
                        DispatchFeasibilityStatus.Infeasible,
                        null,
                        null,
                        [new(DispatchFeasibilityReason.AircraftUnavailable)]);
                }
            }
        }

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

        AircraftRegistryRecord? aircraft = resolution?.TryCreateRegistryRecord();

        if (resolution is null)
        {
            missing.Add(new(DispatchFeasibilityReason.AircraftNotFound));
        }
        else if (aircraft is null)
        {
            missing.AddRange(
                resolution.UnresolvedCapabilityFields.Select(
                    static field => new DispatchFeasibilityIssue(
                        DispatchFeasibilityReason.AircraftCapabilityDataIncomplete,
                        AircraftField: field)));
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

        return RunwayCompatibilityEvaluator.Evaluate(
            aircraft!,
            origin!,
            destination!);
    }
}
