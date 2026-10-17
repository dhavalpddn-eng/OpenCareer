using System.Globalization;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Planning;

public enum DispatchCruiseProfileCatalogStatus
{
    Ready = 0,
    AircraftNotFound,
    AircraftNotInstalled,
    DispatchPerformanceUnavailable,
    NoUsableCruiseProfiles
}

public sealed record DispatchCruiseProfileOption(
    int ProfileIndex,
    int FuelTypeIndex,
    string DisplayName,
    DispatchCruiseProfileEnvelope Envelope,
    string EvidenceDetail);

public sealed record DispatchCruiseProfileEnvelope(
    double MinimumWeightPounds,
    double MaximumWeightPounds,
    double MinimumIsaDeviationCelsius,
    double MaximumIsaDeviationCelsius,
    double MinimumPressureAltitudeFeet,
    double MaximumPressureAltitudeFeet)
{
    public void Validate()
    {
        ValidateRange(MinimumWeightPounds, MaximumWeightPounds, nameof(MinimumWeightPounds));
        ValidateRange(
            MinimumIsaDeviationCelsius,
            MaximumIsaDeviationCelsius,
            nameof(MinimumIsaDeviationCelsius));
        ValidateRange(
            MinimumPressureAltitudeFeet,
            MaximumPressureAltitudeFeet,
            nameof(MinimumPressureAltitudeFeet));
    }

    public bool Contains(
        double weightPounds,
        double isaDeviationCelsius,
        double pressureAltitudeFeet)
    {
        Validate();
        return double.IsFinite(weightPounds)
            && double.IsFinite(isaDeviationCelsius)
            && double.IsFinite(pressureAltitudeFeet)
            && weightPounds >= MinimumWeightPounds
            && weightPounds <= MaximumWeightPounds
            && isaDeviationCelsius >= MinimumIsaDeviationCelsius
            && isaDeviationCelsius <= MaximumIsaDeviationCelsius
            && pressureAltitudeFeet >= MinimumPressureAltitudeFeet
            && pressureAltitudeFeet <= MaximumPressureAltitudeFeet;
    }

    private static void ValidateRange(double minimum, double maximum, string name)
    {
        if (!double.IsFinite(minimum)
            || !double.IsFinite(maximum)
            || minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}

public sealed record DispatchCruiseProfileCatalog(
    DispatchCruiseProfileCatalogStatus Status,
    string Detail,
    IReadOnlyList<DispatchCruiseProfileOption> Profiles);

public interface IDispatchCruiseProfileSource
{
    Task<DispatchCruiseProfileCatalog> ReadAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Projects only registry-proven cruise profiles that contain both required
/// performance tables and a matching fuel-density record. The UI can select
/// these identities, but this source never chooses a profile for the pilot.
/// </summary>
public sealed class DispatchCruiseProfileSource(
    IAircraftRegistrySource aircraftRegistry)
    : IDispatchCruiseProfileSource
{
    private readonly IAircraftRegistrySource _aircraftRegistry =
        aircraftRegistry ?? throw new ArgumentNullException(nameof(aircraftRegistry));

    public async Task<DispatchCruiseProfileCatalog> ReadAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);
        string aircraftId = canonicalAircraftId.Trim();

        AircraftRegistryResolution? aircraft = await _aircraftRegistry
            .FindAircraftAsync(aircraftId, cancellationToken)
            .ConfigureAwait(false);

        if (aircraft is null)
        {
            return Empty(
                DispatchCruiseProfileCatalogStatus.AircraftNotFound,
                "The selected aircraft is not present in the current registry.");
        }

        if (!string.Equals(
                aircraft.CanonicalAircraftId,
                aircraftId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Aircraft registry returned cruise evidence for a different canonical identity.");
        }

        if (aircraft.InstallationStatus != AircraftInstallationStatus.Installed)
        {
            return Empty(
                DispatchCruiseProfileCatalogStatus.AircraftNotInstalled,
                "The selected aircraft is not proven installed.");
        }

        AircraftDispatchPerformanceProfile? dispatch = aircraft.DispatchPerformance;
        if (dispatch?.ConditionedPerformance is null)
        {
            return Empty(
                DispatchCruiseProfileCatalogStatus.DispatchPerformanceUnavailable,
                "Authoritative cruise-performance evidence is unavailable for this aircraft.");
        }

        dispatch.Validate();
        AircraftCruisePerformanceProfile[] supplied = dispatch
            .ConditionedPerformance
            .CruiseProfiles
            .OrderBy(static profile => profile.Index)
            .ToArray();

        var usable = new List<DispatchCruiseProfileOption>();
        foreach (AircraftCruisePerformanceProfile profile in supplied)
        {
            DispatchCruiseProfileOption? option = TryCreateOption(profile, dispatch);
            if (option is not null)
                usable.Add(option);
        }

        int incomplete = supplied.Length - usable.Count;
        if (usable.Count == 0)
        {
            return Empty(
                DispatchCruiseProfileCatalogStatus.NoUsableCruiseProfiles,
                supplied.Length == 0
                    ? "No cruise profiles were supplied by the aircraft's authoritative performance evidence."
                    : $"{supplied.Length} cruise profile(s) were supplied, but none has complete airspeed, fuel-consumption and fuel-density evidence with a common operating envelope.");
        }

        string source = string.IsNullOrWhiteSpace(dispatch.Source)
            ? "aircraft registry evidence"
            : dispatch.Source.Trim();
        string detail =
            $"{usable.Count} selectable cruise profile(s) from {source}."
            + (incomplete == 0
                ? string.Empty
                : $" {incomplete} incomplete profile(s) were excluded.");

        return new(
            DispatchCruiseProfileCatalogStatus.Ready,
            detail,
            usable.ToArray());
    }

    private static DispatchCruiseProfileOption? TryCreateOption(
        AircraftCruisePerformanceProfile profile,
        AircraftDispatchPerformanceProfile dispatch)
    {
        profile.Validate();
        if (profile.TrueAirspeedKnots is not { } trueAirspeed
            || profile.FuelConsumptionGallonsPerHour is not { } fuelConsumption
            || dispatch.ResolveFuelDensityPoundsPerGallon(profile.FuelTypeIndex)
                is not { } density)
        {
            return null;
        }

        var envelope = new DispatchCruiseProfileEnvelope(
            Math.Max(trueAirspeed.WeightsPounds[0], fuelConsumption.WeightsPounds[0]),
            Math.Min(trueAirspeed.WeightsPounds[^1], fuelConsumption.WeightsPounds[^1]),
            Math.Max(
                trueAirspeed.TemperaturesCelsius[0],
                fuelConsumption.TemperaturesCelsius[0]),
            Math.Min(
                trueAirspeed.TemperaturesCelsius[^1],
                fuelConsumption.TemperaturesCelsius[^1]),
            Math.Max(
                trueAirspeed.PressureAltitudesFeet[0],
                fuelConsumption.PressureAltitudesFeet[0]),
            Math.Min(
                trueAirspeed.PressureAltitudesFeet[^1],
                fuelConsumption.PressureAltitudesFeet[^1]));

        try
        {
            envelope.Validate();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        string name = string.IsNullOrWhiteSpace(profile.ProfileName)
            ? $"Profile {profile.Index}"
            : profile.ProfileName.Trim();

        return new(
            profile.Index,
            profile.FuelTypeIndex,
            $"{name} · fuel type {profile.FuelTypeIndex}",
            envelope,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Profile {profile.Index} · {dispatch.Confidence} evidence · {density:0.###} lb/gal · common envelope: {envelope.MinimumWeightPounds:0.#}–{envelope.MaximumWeightPounds:0.#} lb, ISA {envelope.MinimumIsaDeviationCelsius:0.#}–{envelope.MaximumIsaDeviationCelsius:0.#} °C, {envelope.MinimumPressureAltitudeFeet:0.#}–{envelope.MaximumPressureAltitudeFeet:0.#} ft"));
    }

    private static DispatchCruiseProfileCatalog Empty(
        DispatchCruiseProfileCatalogStatus status,
        string detail) =>
        new(status, detail, Array.Empty<DispatchCruiseProfileOption>());
}
