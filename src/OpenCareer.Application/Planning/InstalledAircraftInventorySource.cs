using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Planning;

public enum InstalledAircraftInventoryStatus
{
    Ready = 0,
    NoInstalledAircraft,
    DiscoveryUnavailable
}

public sealed record InstalledAircraftInventoryItem(
    string CanonicalAircraftId,
    string DisplayName,
    string InstallationText,
    string TypeIdentityText,
    string PowerplantText,
    string CapabilityText,
    string FleetStatusText,
    string ReservationText,
    string AccessText);

public sealed record InstalledAircraftInventorySnapshot(
    InstalledAircraftInventoryStatus Status,
    string Detail,
    IReadOnlyList<InstalledAircraftInventoryItem> Aircraft);

public interface IInstalledAircraftInventorySource
{
    Task<InstalledAircraftInventorySnapshot> ReadAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only projection of the current installed-aircraft discovery snapshot.
/// Installation comes only from discovery. Registry data may enrich the row,
/// but missing metadata remains explicit and never implies career access.
/// </summary>
public sealed class InstalledAircraftInventorySource(
    IInstalledAircraftDiscoverySource discovery,
    IAircraftRegistrySource registry,
    IAircraftAvailabilityStore availability)
    : IInstalledAircraftInventorySource
{
    private readonly IInstalledAircraftDiscoverySource _discovery =
        discovery ?? throw new ArgumentNullException(nameof(discovery));
    private readonly IAircraftRegistrySource _registry =
        registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly IAircraftAvailabilityStore _availability =
        availability ?? throw new ArgumentNullException(nameof(availability));

    public async Task<InstalledAircraftInventorySnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        InstalledAircraftDiscoverySnapshot snapshot = _discovery.Current;
        if (snapshot.Availability != InstalledAircraftDiscoveryAvailability.Available)
        {
            return new(
                InstalledAircraftInventoryStatus.DiscoveryUnavailable,
                "Installed-aircraft discovery is unavailable. OpenCareer will not guess which aircraft are installed.",
                Array.Empty<InstalledAircraftInventoryItem>());
        }

        string[] aircraftIds = snapshot.Observations
            .Select(ValidateInstalledObservation)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        if (aircraftIds.Length == 0)
        {
            return new(
                InstalledAircraftInventoryStatus.NoInstalledAircraft,
                "Discovery completed, but no installed aircraft were reported.",
                Array.Empty<InstalledAircraftInventoryItem>());
        }

        var items = new List<InstalledAircraftInventoryItem>(aircraftIds.Length);
        foreach (string aircraftId in aircraftIds)
        {
            AircraftRegistryResolution? resolution = await _registry
                .FindAircraftAsync(aircraftId, cancellationToken)
                .ConfigureAwait(false);
            AircraftAvailabilityState? availability = await _availability
                .FindAsync(aircraftId, cancellationToken)
                .ConfigureAwait(false);

            items.Add(CreateItem(aircraftId, resolution, availability));
        }

        InstalledAircraftInventoryItem[] ordered = items
            .OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.CanonicalAircraftId, StringComparer.Ordinal)
            .ToArray();

        return new(
            InstalledAircraftInventoryStatus.Ready,
            $"{ordered.Length} installed aircraft record(s) reported by current discovery.",
            ordered);
    }

    private static string ValidateInstalledObservation(
        AircraftRegistryObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        observation.Validate();

        if (!observation.IsInstalled)
        {
            throw new InvalidDataException(
                "Installed-aircraft inventory cannot consume a non-installed discovery observation.");
        }

        return observation.CanonicalAircraftId.Trim();
    }

    private static InstalledAircraftInventoryItem CreateItem(
        string aircraftId,
        AircraftRegistryResolution? resolution,
        AircraftAvailabilityState? availability)
    {
        string displayName = resolution?.CapabilityValues.DisplayName?.Trim()
            ?? (AircraftCanonicalIdentity.TryGetMsfsTitle(aircraftId, out string title)
                ? title
                : aircraftId);

        string typeIdentity = resolution?.TypeIdentity is { } type
            ? $"ICAO type {type.IcaoTypeDesignator}"
            : "ICAO type unknown";

        string powerplant = resolution?.PowerplantClassification is { } classification
            ? FormatPowerplant(classification)
            : "Powerplant unknown";

        string capabilities = resolution is null
            ? "Registry metadata unavailable"
            : resolution.HasCompleteCapabilityProfile
                ? "Capability profile available"
                : $"Capability data incomplete ({resolution.UnresolvedCapabilityFields.Count} field(s) unknown)";

        (string fleetStatus, string reservation) = FormatAvailability(
            aircraftId,
            availability);

        return new(
            aircraftId,
            displayName,
            "INSTALLED",
            typeIdentity,
            powerplant,
            capabilities,
            fleetStatus,
            reservation,
            "Career access unknown — installation does not prove ownership, rental, or assignment");
    }

    private static (string Status, string Reservation) FormatAvailability(
        string aircraftId,
        AircraftAvailabilityState? availability)
    {
        if (availability is null)
        {
            return (
                "Fleet status not tracked",
                "No operational availability record exists");
        }

        AircraftAvailabilityState normalized = availability.Normalize();
        if (!string.Equals(
                normalized.CanonicalAircraftId,
                aircraftId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Aircraft availability store returned state for a different canonical identity.");
        }

        if (normalized.IsAvailable)
            return ("AVAILABLE", "No reservation recorded");

        return normalized.IsReserved
            ? ("RESERVED", "Authoritative Fleet reservation recorded")
            : ("UNAVAILABLE", "No reservation identity recorded");
    }

    private static string FormatPowerplant(
        AircraftPowerplantClassification classification)
    {
        classification.Validate();

        string engineType = classification.EngineType switch
        {
            AircraftEngineType.Piston => "piston",
            AircraftEngineType.TurbopropOrTurboshaft => "turboprop/turboshaft",
            AircraftEngineType.Jet => "jet",
            AircraftEngineType.Electric => "electric",
            AircraftEngineType.Rocket => "rocket",
            AircraftEngineType.None => "no engine",
            _ => throw new ArgumentOutOfRangeException(nameof(classification))
        };

        return classification.Multiplicity switch
        {
            AircraftEngineMultiplicity.None => "No powerplant",
            AircraftEngineMultiplicity.Single => $"Single {engineType} engine",
            AircraftEngineMultiplicity.Multi => $"{classification.EngineCount}-engine {engineType}",
            _ => throw new ArgumentOutOfRangeException(nameof(classification))
        };
    }
}
