using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Planning;

public interface IAirportDispatchWeatherSource
{
    Task<AirportDispatchWeatherObservation?> FindWeatherAsync(
        string icao,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads provider-neutral aircraft, airport and optional normalized weather data for
/// one physical operation screening. Weather is queried only when explicit weather
/// limits are requested. Career qualifications, authorization, economics and Jobs
/// generation remain separate gates.
/// </summary>
public sealed class OperationDispatchPlanningService(
    IAircraftRegistrySource aircraftRegistry,
    IAirportDataSource airportData,
    IAirportDispatchWeatherSource? weatherSource = null,
    IAircraftAvailabilityStore? aircraftAvailability = null)
{
    /// <summary>
    /// Screens the exact immutable aircraft resolution already used by the caller.
    /// Airport, weather and Fleet evidence are still read normally; aircraft evidence is not reloaded.
    /// </summary>
    public Task<DispatchFeasibilityResult> EvaluateAsync(
        AircraftRegistryResolution aircraft,
        string originIcao,
        string destinationIcao,
        OperationDispatchRequirements requirements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aircraft);

        return EvaluateResolvedAsync(
            aircraft, originIcao, destinationIcao, requirements,
            reservationId: null, cancellationToken);
    }

    public Task<DispatchFeasibilityResult> EvaluateAsync(
        string aircraftId,
        string originIcao,
        string destinationIcao,
        OperationDispatchRequirements requirements,
        CancellationToken cancellationToken = default) =>
        EvaluateCoreAsync(
            aircraftId,
            originIcao,
            destinationIcao,
            requirements,
            reservationId: null,
            cancellationToken);

    public Task<DispatchFeasibilityResult> EvaluateAsync(
        string aircraftId,
        string originIcao,
        string destinationIcao,
        OperationDispatchRequirements requirements,
        string reservationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationId);

        return EvaluateCoreAsync(
            aircraftId,
            originIcao,
            destinationIcao,
            requirements,
            reservationId.Trim(),
            cancellationToken);
    }

    private async Task<DispatchFeasibilityResult> EvaluateCoreAsync(
        string aircraftId,
        string originIcao,
        string destinationIcao,
        OperationDispatchRequirements requirements,
        string? reservationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(originIcao);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationIcao);
        ArgumentNullException.ThrowIfNull(requirements);
        requirements.Validate();

        AircraftRegistryResolution? aircraft = await aircraftRegistry
            .FindAircraftAsync(aircraftId, cancellationToken)
            .ConfigureAwait(false);

        return await EvaluateResolvedAsync(
            aircraft, originIcao, destinationIcao, requirements, reservationId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<DispatchFeasibilityResult> EvaluateResolvedAsync(
        AircraftRegistryResolution? aircraft,
        string originIcao,
        string destinationIcao,
        OperationDispatchRequirements requirements,
        string? reservationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originIcao);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationIcao);
        ArgumentNullException.ThrowIfNull(requirements);
        requirements.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        if (aircraft is not null
            && aircraft.InstallationStatus != AircraftInstallationStatus.Installed)
        {
            return DispatchFeasibilityResult.Create(
                DispatchFeasibilityStatus.Infeasible,
                null,
                null,
                [new(DispatchFeasibilityReason.AircraftNotInstalled)]);
        }

        if (aircraft is not null && aircraftAvailability is not null)
        {
            AircraftAvailabilityState? availability = await aircraftAvailability
                .FindAsync(aircraft.CanonicalAircraftId, cancellationToken)
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

        AirportDispatchWeatherObservation? originWeather = null;
        AirportDispatchWeatherObservation? destinationWeather = null;

        if (requirements.WeatherLimits is not null && weatherSource is not null)
        {
            originWeather = await weatherSource
                .FindWeatherAsync(originIcao, cancellationToken)
                .ConfigureAwait(false);

            destinationWeather = string.Equals(
                originIcao,
                destinationIcao,
                StringComparison.OrdinalIgnoreCase)
                ? originWeather
                : await weatherSource
                    .FindWeatherAsync(destinationIcao, cancellationToken)
                    .ConfigureAwait(false);
        }

        return OperationDispatchPhysicalEvaluator.Evaluate(
            aircraft!,
            requirements,
            origin!,
            destination!,
            originWeather,
            destinationWeather);
    }
}
