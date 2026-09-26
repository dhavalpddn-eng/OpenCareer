using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Careers;

public sealed record CareerJobAircraftOption(
    string AircraftId,
    string DisplayName);

public sealed record CareerJobAircraftSelectionSnapshot(
    bool IsAvailable,
    IReadOnlyList<CareerJobAircraftOption> Aircraft,
    string Detail);

public sealed class CareerJobAircraftSelectionSource
{
    private readonly IInstalledAircraftDiscoverySource _discovery;

    public CareerJobAircraftSelectionSource(
        IInstalledAircraftDiscoverySource discovery)
    {
        _discovery =
            discovery
            ?? throw new ArgumentNullException(nameof(discovery));
    }

    public Task<CareerJobAircraftSelectionSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        InstalledAircraftDiscoverySnapshot current =
            _discovery.Current;

        if (current.Availability
            != InstalledAircraftDiscoveryAvailability.Available)
        {
            return Task.FromResult(
                new CareerJobAircraftSelectionSnapshot(
                    IsAvailable:
                        false,
                    Array.Empty<CareerJobAircraftOption>(),
                    "Connect MSFS so OpenCareer can read the installed-aircraft catalog before starting a career flight."));
        }

        var byId =
            new Dictionary<string, CareerJobAircraftOption>(
                StringComparer.OrdinalIgnoreCase);

        foreach (AircraftRegistryObservation observation
            in current.Observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observation.Validate();

            if (!observation.IsInstalled)
                continue;

            string aircraftId =
                observation.CanonicalAircraftId.Trim();

            string displayName =
                string.IsNullOrWhiteSpace(
                    observation.DisplayName)
                    ? observation.ProviderRecordId.Trim()
                    : observation.DisplayName.Trim();

            byId.TryAdd(
                aircraftId,
                new(
                    aircraftId,
                    displayName));
        }

        CareerJobAircraftOption[] aircraft =
            byId.Values
                .OrderBy(
                    static item => item.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static item => item.AircraftId,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        string detail =
            aircraft.Length == 0
                ? "MSFS reported no installed aircraft."
                : $"{aircraft.Length} installed aircraft available for dispatch screening.";

        return Task.FromResult(
            new CareerJobAircraftSelectionSnapshot(
                IsAvailable:
                    true,
                aircraft,
                detail));
    }
}
