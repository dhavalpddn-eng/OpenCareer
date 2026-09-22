using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Planning;

public interface IInstalledAircraftRegistryStore
{
    Task ReplaceAllAsync(
        IReadOnlyList<AircraftRegistryObservation> observations,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AircraftRegistryObservation>> FindAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default);
}

public sealed class PersistentInstalledAircraftObservationSource(
    IInstalledAircraftDiscoverySource discoverySource,
    IInstalledAircraftRegistryStore store)
    : IAircraftRegistryObservationSource
{
    public async Task<IReadOnlyList<AircraftRegistryObservation>> FindAircraftObservationsAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);

        InstalledAircraftDiscoverySnapshot current = discoverySource.Current;

        if (current.Availability == InstalledAircraftDiscoveryAvailability.Available)
        {
            AircraftRegistryObservation[] installed = ValidateInstalledSnapshot(
                current.Observations);

            await store
                .ReplaceAllAsync(installed, cancellationToken)
                .ConfigureAwait(false);

            return installed
                .Where(observation => string.Equals(
                    observation.CanonicalAircraftId,
                    canonicalAircraftId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        IReadOnlyList<AircraftRegistryObservation> persisted = await store
            .FindAsync(canonicalAircraftId, cancellationToken)
            .ConfigureAwait(false);

        AircraftRegistryObservation[] validated = ValidateInstalledSnapshot(persisted);

        if (validated.Any(observation => !string.Equals(
                observation.CanonicalAircraftId,
                canonicalAircraftId,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "Persisted installed-aircraft registry returned a different canonical aircraft identity.");
        }

        return validated;
    }

    private static AircraftRegistryObservation[] ValidateInstalledSnapshot(
        IReadOnlyList<AircraftRegistryObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var providerRecords = new HashSet<(string ProviderId, string ProviderRecordId)>();
        var result = new AircraftRegistryObservation[observations.Count];

        for (int i = 0; i < observations.Count; i++)
        {
            AircraftRegistryObservation observation =
                observations[i]
                ?? throw new InvalidDataException(
                    "Installed-aircraft registry cannot contain null observations.");

            observation.Validate();

            if (!observation.IsInstalled)
            {
                throw new InvalidDataException(
                    "Installed-aircraft registry cannot persist non-installed observations.");
            }

            var key = (
                observation.ProviderId.Trim(),
                observation.ProviderRecordId.Trim());

            if (!providerRecords.Add(key))
            {
                throw new InvalidDataException(
                    "Installed-aircraft registry contains duplicate provider record identities.");
            }

            result[i] = observation;
        }

        return result;
    }
}
