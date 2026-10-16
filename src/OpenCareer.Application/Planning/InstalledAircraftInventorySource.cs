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
    IAircraftRegistrySource registry)
    : IInstalledAircraftInventorySource
{
    private readonly IInstalledAircraftDiscoverySource _discovery =
        discovery ?? throw new ArgumentNullException(nameof(discovery));
    private readonly IAircraftRegistrySource _registry =
        registry ?? throw new ArgumentNullException(nameof(registry));

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

            items.Add(CreateItem(aircraftId, resolution));
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
        AircraftRegistryResolution? resolution)
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

        return new(
            aircraftId,
            displayName,
            "INSTALLED",
            typeIdentity,
            powerplant,
            capabilities,
            "Career access unknown — installation does not prove ownership, rental, or assignment");
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
