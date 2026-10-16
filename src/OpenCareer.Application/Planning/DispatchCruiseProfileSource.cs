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
    string EvidenceDetail);

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

        DispatchCruiseProfileOption[] usable = supplied
            .Where(profile =>
                profile.TrueAirspeedKnots is not null
                && profile.FuelConsumptionGallonsPerHour is not null
                && dispatch.ResolveFuelDensityPoundsPerGallon(profile.FuelTypeIndex)
                    is not null)
            .Select(profile => CreateOption(profile, dispatch))
            .ToArray();

        int incomplete = supplied.Length - usable.Length;
        if (usable.Length == 0)
        {
            return Empty(
                DispatchCruiseProfileCatalogStatus.NoUsableCruiseProfiles,
                supplied.Length == 0
                    ? "No cruise profiles were supplied by the aircraft's authoritative performance evidence."
                    : $"{supplied.Length} cruise profile(s) were supplied, but none has complete airspeed, fuel-consumption and fuel-density evidence.");
        }

        string source = string.IsNullOrWhiteSpace(dispatch.Source)
            ? "aircraft registry evidence"
            : dispatch.Source.Trim();
        string detail =
            $"{usable.Length} selectable cruise profile(s) from {source}."
            + (incomplete == 0
                ? string.Empty
                : $" {incomplete} incomplete profile(s) were excluded.");

        return new(
            DispatchCruiseProfileCatalogStatus.Ready,
            detail,
            usable);
    }

    private static DispatchCruiseProfileOption CreateOption(
        AircraftCruisePerformanceProfile profile,
        AircraftDispatchPerformanceProfile dispatch)
    {
        profile.Validate();
        double density = dispatch
            .ResolveFuelDensityPoundsPerGallon(profile.FuelTypeIndex)
            ?? throw new InvalidOperationException(
                "A selectable cruise profile must have matching fuel-density evidence.");
        string name = string.IsNullOrWhiteSpace(profile.ProfileName)
            ? $"Profile {profile.Index}"
            : profile.ProfileName.Trim();

        return new(
            profile.Index,
            profile.FuelTypeIndex,
            $"{name} · fuel type {profile.FuelTypeIndex}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"Profile {profile.Index} · {dispatch.Confidence} evidence · {density:0.###} lb/gal"));
    }

    private static DispatchCruiseProfileCatalog Empty(
        DispatchCruiseProfileCatalogStatus status,
        string detail) =>
        new(status, detail, Array.Empty<DispatchCruiseProfileOption>());
}
