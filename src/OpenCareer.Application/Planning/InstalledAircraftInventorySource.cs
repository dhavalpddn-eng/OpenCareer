using System.Globalization;
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
    string FamilyIdentityText,
    string PowerplantClassText,
    string PowerplantText,
    string CapabilityText,
    string FleetStatusText,
    string ReservationText,
    string AccessText,
    string MaximumPayloadText = "Maximum payload unknown",
    string MaximumRangeText = "Maximum range unknown",
    string SeatsText = "Seats unknown",
    string IfrCapabilityText = "IFR capability unknown",
    string PressurizationText = "Pressurization unknown",
    string LandingGearText = "Landing gear configuration unknown",
    string CapabilityEvidenceText = "Capability evidence sources unavailable",
    string PassengerCapacityText = "Passenger capacity unknown",
    string TypicalCruiseText = "Typical cruise unknown",
    string ObjectClassText = "Aircraft class unknown",
    string CertifiedCeilingText = "Certified ceiling unknown",
    string OperatingEmptyWeightText = "Operating empty weight unknown",
    string ConfiguredEmptyWeightText = "Configured empty weight unknown",
    string MaximumTakeoffWeightText = "Maximum takeoff weight unknown",
    string MaximumLandingWeightText = "Maximum landing weight unknown",
    string MaximumZeroFuelWeightText = "Maximum zero-fuel weight unknown",
    string MaximumFuelWeightText = "Maximum fuel weight unknown",
    string FuelCapacityText = "Fuel capacity unknown");

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
            if (resolution is not null
                && !string.Equals(
                    resolution.CanonicalAircraftId,
                    aircraftId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Aircraft registry returned inventory evidence for a different canonical identity.");
            }

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

        string familyIdentity = resolution?.FamilyIdentity is { } family
            ? FormatFamilyIdentity(family)
            : "ICAO family unknown";

        string powerplantClass = resolution?.PowerplantClassIdentity is { } classIdentity
            ? FormatPowerplantClass(classIdentity)
            : "Powerplant class unknown";

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
        ResolvedAircraftCapabilities? facts = resolution?.CapabilityValues;
        double? maximumPayload = ProvenValue(
            resolution,
            AircraftRegistryField.MaximumPayloadPounds,
            facts?.MaximumPayloadPounds);
        double? maximumRange = ProvenValue(
            resolution,
            AircraftRegistryField.MaximumRangeNauticalMiles,
            facts?.MaximumRangeNauticalMiles);
        double? typicalCruise = ProvenValue(
            resolution,
            AircraftRegistryField.TypicalCruiseKnots,
            facts?.TypicalCruiseKnots);
        int? seats = ProvenValue(
            resolution,
            AircraftRegistryField.Seats,
            facts?.Seats);
        bool? ifrCapable = ProvenValue(
            resolution,
            AircraftRegistryField.IfrCapable,
            facts?.IfrCapable);
        bool? pressurized = ProvenValue(
            resolution,
            AircraftRegistryField.Pressurized,
            facts?.Pressurized);
        bool? retractableGear = ProvenValue(
            resolution,
            AircraftRegistryField.RetractableGear,
            facts?.RetractableGear);
        int? passengerCapacity = ProvenValue(
            resolution,
            AircraftRegistryField.PassengerCapacity,
            facts?.PassengerCapacity);
        AircraftObjectClass? objectClass = ProvenValue(
            resolution,
            AircraftRegistryField.ObjectClass,
            resolution?.ObjectClass);
        double? certifiedCeiling = ProvenValue(
            resolution,
            AircraftRegistryField.CertifiedCeilingFeet,
            facts?.CertifiedCeilingFeet);
        AircraftDispatchPerformanceProfile? dispatchPerformance =
            resolution?.DispatchPerformance is { } candidate
            && resolution.Provenance.ContainsKey(
                AircraftRegistryField.DispatchPerformance)
                ? candidate
                : null;
        dispatchPerformance?.Validate();
        bool hasWeightFuelEnvelope = dispatchPerformance is not null
            && (dispatchPerformance.OperatingEmptyWeightPounds is not null
                || dispatchPerformance.ConfiguredEmptyWeightPounds is not null
                || dispatchPerformance.MaximumTakeoffWeightPounds is not null
                || dispatchPerformance.MaximumLandingWeightPounds is not null
                || dispatchPerformance.MaximumZeroFuelWeightPounds is not null
                || dispatchPerformance.MaximumFuelWeightPounds is not null
                || dispatchPerformance.FuelCapacityGallons is not null);

        return new(
            aircraftId,
            displayName,
            "INSTALLED",
            typeIdentity,
            familyIdentity,
            powerplantClass,
            powerplant,
            capabilities,
            fleetStatus,
            reservation,
            "Career access unknown — installation does not prove ownership, rental, or assignment",
            FormatOptionalNumber(
                maximumPayload,
                "Maximum payload",
                "lb"),
            FormatOptionalNumber(
                maximumRange,
                "Maximum range",
                "nm"),
            seats is { } seatCount
                ? $"{seatCount} seat(s)"
                : "Seats unknown",
            FormatOptionalFlag(
                ifrCapable,
                "IFR capable",
                "Not IFR capable",
                "IFR capability unknown"),
            FormatOptionalFlag(
                pressurized,
                "Pressurized",
                "Not pressurized",
                "Pressurization unknown"),
            FormatOptionalFlag(
                retractableGear,
                "Retractable gear",
                "Fixed/non-retractable gear",
                "Landing gear configuration unknown"),
            FormatCapabilityEvidence(
                resolution,
                maximumPayload,
                maximumRange,
                seats,
                ifrCapable,
                pressurized,
                retractableGear,
                passengerCapacity,
                typicalCruise,
                objectClass,
                certifiedCeiling,
                hasWeightFuelEnvelope),
            passengerCapacity is { } displayCapacity
                ? $"Passenger capacity {displayCapacity} (cabin value; crew and total seats not inferred)"
                : "Passenger capacity unknown",
            FormatOptionalNumber(typicalCruise, "Typical cruise", "kt"),
            FormatObjectClass(objectClass),
            FormatOptionalNumber(certifiedCeiling, "Certified ceiling", "ft"),
            FormatOptionalNumber(
                dispatchPerformance?.OperatingEmptyWeightPounds,
                "Operating empty weight",
                "lb"),
            FormatOptionalNumber(
                dispatchPerformance?.ConfiguredEmptyWeightPounds,
                "Configured empty weight",
                "lb"),
            FormatOptionalNumber(
                dispatchPerformance?.MaximumTakeoffWeightPounds,
                "Maximum takeoff weight",
                "lb"),
            FormatOptionalNumber(
                dispatchPerformance?.MaximumLandingWeightPounds,
                "Maximum landing weight",
                "lb"),
            FormatOptionalNumber(
                dispatchPerformance?.MaximumZeroFuelWeightPounds,
                "Maximum zero-fuel weight",
                "lb"),
            FormatOptionalNumber(
                dispatchPerformance?.MaximumFuelWeightPounds,
                "Maximum fuel weight",
                "lb"),
            FormatOptionalNumber(
                dispatchPerformance?.FuelCapacityGallons,
                "Fuel capacity",
                "gal"));
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

    private static string FormatFamilyIdentity(AircraftFamilyIdentity identity)
    {
        identity.Validate();
        return $"{identity.IcaoManufacturer} {identity.IcaoModel}";
    }

    private static string FormatPowerplantClass(
        AircraftPowerplantClassIdentity identity)
    {
        identity.Validate();

        string engineType = identity.EngineType switch
        {
            AircraftEngineType.Piston => "piston",
            AircraftEngineType.TurbopropOrTurboshaft => "turboprop/turboshaft",
            AircraftEngineType.Jet => "jet",
            AircraftEngineType.Electric => "electric",
            AircraftEngineType.Rocket => "rocket",
            AircraftEngineType.None => "unpowered",
            _ => throw new ArgumentOutOfRangeException(nameof(identity))
        };
        string multiplicity = identity.Multiplicity switch
        {
            AircraftEngineMultiplicity.None => "none",
            AircraftEngineMultiplicity.Single => "single-engine",
            AircraftEngineMultiplicity.Multi => "multi-engine",
            _ => throw new ArgumentOutOfRangeException(nameof(identity))
        };
        return $"{multiplicity} {engineType}";
    }

    private static string FormatObjectClass(AircraftObjectClass? objectClass) =>
        objectClass switch
        {
            AircraftObjectClass.Airplane => "Aircraft class Airplane",
            AircraftObjectClass.Helicopter => "Aircraft class Helicopter",
            AircraftObjectClass.Glider => "Aircraft class Glider",
            AircraftObjectClass.Balloon => "Aircraft class Balloon",
            AircraftObjectClass.Airship => "Aircraft class Airship",
            AircraftObjectClass.Gyroplane => "Aircraft class Gyroplane",
            AircraftObjectClass.Ornithopter => "Aircraft class Ornithopter",
            AircraftObjectClass.TiltRotor => "Aircraft class TiltRotor",
            AircraftObjectClass.Miscellaneous => "Aircraft class Misc",
            null => "Aircraft class unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(objectClass))
        };

    private static string FormatOptionalNumber(
        double? value,
        string label,
        string unit) =>
        value is { } number
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{label} {number:0.#} {unit}")
            : $"{label} unknown";

    private static string FormatOptionalFlag(
        bool? value,
        string whenTrue,
        string whenFalse,
        string whenUnknown) =>
        value switch
        {
            true => whenTrue,
            false => whenFalse,
            null => whenUnknown
        };

    private static T? ProvenValue<T>(
        AircraftRegistryResolution? resolution,
        AircraftRegistryField field,
        T? value)
        where T : struct =>
        value is not null
        && resolution?.Provenance.ContainsKey(field) == true
            ? value
            : null;

    private static string FormatCapabilityEvidence(
        AircraftRegistryResolution? resolution,
        double? maximumPayload,
        double? maximumRange,
        int? seats,
        bool? ifrCapable,
        bool? pressurized,
        bool? retractableGear,
        int? passengerCapacity,
        double? typicalCruise,
        AircraftObjectClass? objectClass,
        double? certifiedCeiling,
        bool hasWeightFuelEnvelope)
    {
        if (resolution is null)
            return "Capability evidence sources unavailable";

        var evidence = new List<string>();
        Add(AircraftRegistryField.MaximumPayloadPounds, "payload", maximumPayload is not null);
        Add(AircraftRegistryField.MaximumRangeNauticalMiles, "range", maximumRange is not null);
        Add(AircraftRegistryField.Seats, "seats", seats is not null);
        Add(AircraftRegistryField.TypicalCruiseKnots, "typical cruise", typicalCruise is not null);
        Add(AircraftRegistryField.IfrCapable, "IFR", ifrCapable is not null);
        Add(AircraftRegistryField.Pressurized, "pressurization", pressurized is not null);
        Add(AircraftRegistryField.RetractableGear, "landing gear", retractableGear is not null);
        Add(AircraftRegistryField.PassengerCapacity, "passenger capacity", passengerCapacity is not null);
        Add(AircraftRegistryField.ObjectClass, "aircraft class", objectClass is not null);
        Add(AircraftRegistryField.CertifiedCeilingFeet, "certified ceiling", certifiedCeiling is not null);
        Add(
            AircraftRegistryField.DispatchPerformance,
            "weight/fuel envelope",
            hasWeightFuelEnvelope);

        return evidence.Count == 0
            ? "Capability evidence sources unavailable"
            : "Evidence: " + string.Join(" · ", evidence);

        void Add(
            AircraftRegistryField field,
            string label,
            bool displayed)
        {
            if (!displayed
                || !resolution.Provenance.TryGetValue(
                    field,
                    out AircraftRegistryFieldProvenance? provenance)
                || provenance is null)
            {
                return;
            }

            evidence.Add(
                $"{label} — {provenance.ProviderId} ({provenance.Confidence})");
        }
    }
}
