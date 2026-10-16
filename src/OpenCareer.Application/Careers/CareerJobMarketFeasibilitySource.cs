using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Careers;

public enum CareerJobMarketFeasibilityStatus
{
    Ready = 0,
    AircraftDiscoveryUnavailable,
    NoAuthoritativeAircraft,
    NoFeasibleDestinations,
    AirportDataUnavailable
}

public sealed record CareerJobMarketFeasibilitySnapshot(
    CareerJobMarketFeasibilityStatus Status,
    IReadOnlyList<JobMarketDestination> Destinations,
    string Detail)
{
    public bool IsReady => Status == CareerJobMarketFeasibilityStatus.Ready;
}

public interface ICareerJobMarketFeasibilitySource
{
    Task<CareerJobMarketFeasibilitySnapshot> FilterAsync(
        string originIcao,
        IReadOnlyList<JobMarketDestination> destinations,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies the same fail-closed installed-aircraft capability projection used
/// by the final job-start gate before market offers are generated, then requires
/// authoritative static runway compatibility at both route endpoints. This is
/// an early breadth filter only; final dispatch still rechecks current aircraft,
/// airport, weather, reservation and operation evidence.
/// </summary>
public sealed class CareerJobMarketFeasibilitySource(
    CareerJobAircraftSelectionSource aircraftSelection,
    IAircraftRegistrySource aircraftRegistry,
    IAirportDataSource airportData)
    : ICareerJobMarketFeasibilitySource
{
    private readonly CareerJobAircraftSelectionSource _aircraftSelection =
        aircraftSelection
        ?? throw new ArgumentNullException(nameof(aircraftSelection));
    private readonly IAircraftRegistrySource _aircraftRegistry =
        aircraftRegistry
        ?? throw new ArgumentNullException(nameof(aircraftRegistry));
    private readonly IAirportDataSource _airportData =
        airportData
        ?? throw new ArgumentNullException(nameof(airportData));

    public async Task<CareerJobMarketFeasibilitySnapshot> FilterAsync(
        string originIcao,
        IReadOnlyList<JobMarketDestination> destinations,
        CancellationToken cancellationToken = default)
    {
        string normalizedOrigin = NormalizeIcao(originIcao);
        ArgumentNullException.ThrowIfNull(destinations);
        foreach (JobMarketDestination destination in destinations)
        {
            ArgumentNullException.ThrowIfNull(destination);
            destination.Validate();
        }

        CareerJobAircraftSelectionSnapshot selection = await _aircraftSelection
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!selection.IsAvailable)
        {
            return Blocked(
                CareerJobMarketFeasibilityStatus.AircraftDiscoveryUnavailable,
                selection.Detail);
        }

        var aircraft = new List<MarketAircraftEvidence>();
        foreach (CareerJobAircraftOption option in selection.Aircraft)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AircraftRegistryResolution? resolution = await _aircraftRegistry
                .FindAircraftAsync(option.AircraftId, cancellationToken)
                .ConfigureAwait(false);

            if (resolution is null)
                continue;

            if (!string.Equals(
                    resolution.CanonicalAircraftId,
                    option.AircraftId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Aircraft registry returned market-feasibility evidence for a different canonical identity.");
            }

            if (CareerJobAircraftCapabilityProjection.TryCreate(resolution)
                is { } record)
            {
                aircraft.Add(new(resolution, record));
            }
        }

        if (aircraft.Count == 0)
        {
            return Blocked(
                CareerJobMarketFeasibilityStatus.NoAuthoritativeAircraft,
                "No installed aircraft has enough authoritative capability and access evidence for job generation.");
        }

        AirportRecord? origin = await _airportData
            .FindAirportAsync(normalizedOrigin, cancellationToken)
            .ConfigureAwait(false);

        if (origin is null)
        {
            return Blocked(
                CareerJobMarketFeasibilityStatus.AirportDataUnavailable,
                $"Authoritative runway data is unavailable for market origin {normalizedOrigin}.");
        }

        origin.Validate();
        EnsureAirportIdentity(normalizedOrigin, origin);

        var feasible = new List<JobMarketDestination>();
        foreach (JobMarketDestination destination in destinations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AirportRecord? destinationAirport = await _airportData
                .FindAirportAsync(destination.NormalizedIcao, cancellationToken)
                .ConfigureAwait(false);

            if (destinationAirport is null)
                continue;

            destinationAirport.Validate();
            EnsureAirportIdentity(destination.NormalizedIcao, destinationAirport);

            if (aircraft.Any(item => IsFeasible(
                    item,
                    origin,
                    destinationAirport,
                    destination)))
            {
                feasible.Add(destination);
            }
        }

        if (feasible.Count == 0)
        {
            return Blocked(
                CareerJobMarketFeasibilityStatus.NoFeasibleDestinations,
                "No supported market destination has both proven installed civilian-aircraft capability and authoritative endpoint runway compatibility.");
        }

        return new(
            CareerJobMarketFeasibilityStatus.Ready,
            feasible.ToArray(),
            $"{feasible.Count} of {destinations.Count} market destination(s) have at least one capability- and runway-qualified installed aircraft.");
    }

    private static bool IsFeasible(
        MarketAircraftEvidence aircraft,
        AirportRecord origin,
        AirportRecord destinationAirport,
        JobMarketDestination destination)
    {
        if (!aircraft.Record.Capabilities.Satisfies(Requirements(destination))
            || aircraft.Resolution.RunwayPerformance is null
            || !aircraft.Resolution.Provenance.TryGetValue(
                AircraftRegistryField.RunwayPerformance,
                out AircraftRegistryFieldProvenance? runwayProvenance)
            || runwayProvenance is null)
        {
            return false;
        }

        DispatchFeasibilityResult runway =
            RunwayCompatibilityEvaluator.Evaluate(
                aircraft.Resolution.RunwayPerformance,
                origin,
                destinationAirport);

        return runway.Status == DispatchFeasibilityStatus.Feasible;
    }

    private static AircraftMissionRequirements Requirements(
        JobMarketDestination destination) =>
        new(
            AllowedAccess: AircraftAccess.Civilian,
            MinimumRangeNauticalMiles: destination.DistanceNm,
            MinimumSeats: 0);

    private static string NormalizeIcao(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 4
            || normalized.Any(static character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "A four-letter ICAO airport identifier is required.",
                nameof(value));
        }

        return normalized;
    }

    private static void EnsureAirportIdentity(
        string requestedIcao,
        AirportRecord airport)
    {
        string returnedIcao = NormalizeIcao(airport.Icao);
        if (!string.Equals(
                requestedIcao,
                returnedIcao,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Airport source returned {returnedIcao} for requested market airport {requestedIcao}.");
        }
    }

    private static CareerJobMarketFeasibilitySnapshot Blocked(
        CareerJobMarketFeasibilityStatus status,
        string detail) =>
        new(status, Array.Empty<JobMarketDestination>(), detail);

    private sealed record MarketAircraftEvidence(
        AircraftRegistryResolution Resolution,
        AircraftRegistryRecord Record);
}
