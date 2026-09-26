using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.SimConnect;

public sealed class SimConnectInstalledAircraftObservationSource(
    SimConnectConnection connection)
    : IAircraftRegistryObservationSource, IInstalledAircraftDiscoverySource
{
    public const string ProviderId = "msfs-simconnect";

    /// <summary>
    /// The exact current MSFS TITLE, exposed read-only for isolated live-test diagnostics.
    /// It is not a canonical aircraft identity or career authority.
    /// </summary>
    public string? CurrentAircraftTitle => connection.CurrentAircraftTitle;

    public InstalledAircraftDiscoverySnapshot Current
    {
        get
        {
            SimConnectAircraftCatalogSnapshot snapshot = connection.AircraftCatalog;
            string? currentAircraftTitle = connection.CurrentAircraftTitle;

            string[] titles =
                snapshot.AircraftTitles
                    .Concat(
                        string.IsNullOrWhiteSpace(currentAircraftTitle)
                            ? Array.Empty<string>()
                            : [currentAircraftTitle])
                    .Where(static title => !string.IsNullOrWhiteSpace(title))
                    .Select(static title => title.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static title => title, StringComparer.Ordinal)
                    .ToArray();

            if (titles.Length == 0)
                return InstalledAircraftDiscoverySnapshot.Unavailable;

            AircraftRegistryObservation[] observations = titles
                .Select(CreateObservation)
                .GroupBy(
                    static observation => observation.CanonicalAircraftId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(static group => group
                    .OrderBy(
                        static observation => observation.ProviderRecordId,
                        StringComparer.Ordinal)
                    .First())
                .OrderBy(
                    static observation => observation.DisplayName,
                    StringComparer.Ordinal)
                .ToArray();

            return new(
                InstalledAircraftDiscoveryAvailability.Available,
                observations);
        }
    }

    public Task<IReadOnlyList<AircraftRegistryObservation>> FindAircraftObservationsAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);
        cancellationToken.ThrowIfCancellationRequested();

        InstalledAircraftDiscoverySnapshot current = Current;
        IReadOnlyList<AircraftRegistryObservation> result =
            current.Availability == InstalledAircraftDiscoveryAvailability.Available
                ? current.Observations
                    .Where(observation => string.Equals(
                        observation.CanonicalAircraftId,
                        canonicalAircraftId,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : Array.Empty<AircraftRegistryObservation>();

        return Task.FromResult(result);
    }

    public static string CreateCanonicalAircraftId(string aircraftTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftTitle);
        return AircraftCanonicalIdentity.FromMsfsTitle(
            MsfsAircraftTitleCanonicalizer.Resolve(aircraftTitle));
    }

    private static AircraftRegistryObservation CreateObservation(string aircraftTitle) =>
        new(
            CreateCanonicalAircraftId(aircraftTitle),
            ProviderId,
            aircraftTitle,
            AircraftDataConfidence.Verified,
            IsInstalled: true,
            DisplayName: aircraftTitle);
}
