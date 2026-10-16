using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public enum CareerJobMarketFeasibilityStatus
{
    Ready = 0,
    AircraftDiscoveryUnavailable,
    NoAuthoritativeAircraft,
    NoFeasibleDestinations
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
        IReadOnlyList<JobMarketDestination> destinations,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies the same fail-closed installed-aircraft capability projection used
/// by the final job-start gate before market offers are generated. This is an
/// early breadth filter only; final dispatch still rechecks current aircraft,
/// airport, weather, reservation and operation evidence.
/// </summary>
public sealed class CareerJobMarketFeasibilitySource(
    CareerJobAircraftSelectionSource aircraftSelection,
    IAircraftRegistrySource aircraftRegistry)
    : ICareerJobMarketFeasibilitySource
{
    private readonly CareerJobAircraftSelectionSource _aircraftSelection =
        aircraftSelection
        ?? throw new ArgumentNullException(nameof(aircraftSelection));
    private readonly IAircraftRegistrySource _aircraftRegistry =
        aircraftRegistry
        ?? throw new ArgumentNullException(nameof(aircraftRegistry));

    public async Task<CareerJobMarketFeasibilitySnapshot> FilterAsync(
        IReadOnlyList<JobMarketDestination> destinations,
        CancellationToken cancellationToken = default)
    {
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

        var aircraft = new List<AircraftRegistryRecord>();
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
                aircraft.Add(record);
            }
        }

        if (aircraft.Count == 0)
        {
            return Blocked(
                CareerJobMarketFeasibilityStatus.NoAuthoritativeAircraft,
                "No installed aircraft has enough authoritative capability and access evidence for job generation.");
        }

        JobMarketDestination[] feasible = destinations
            .Where(destination => aircraft.Any(item =>
                item.Capabilities.Satisfies(Requirements(destination))))
            .ToArray();

        if (feasible.Length == 0)
        {
            return Blocked(
                CareerJobMarketFeasibilityStatus.NoFeasibleDestinations,
                "No supported market destination is within the proven capability of an installed civilian aircraft.");
        }

        return new(
            CareerJobMarketFeasibilityStatus.Ready,
            feasible,
            $"{feasible.Length} of {destinations.Count} market destination(s) have at least one capability-qualified installed aircraft.");
    }

    private static AircraftMissionRequirements Requirements(
        JobMarketDestination destination) =>
        new(
            AllowedAccess: AircraftAccess.Civilian,
            MinimumRangeNauticalMiles: destination.DistanceNm,
            MinimumSeats: 0);

    private static CareerJobMarketFeasibilitySnapshot Blocked(
        CareerJobMarketFeasibilityStatus status,
        string detail) =>
        new(status, Array.Empty<JobMarketDestination>(), detail);
}
