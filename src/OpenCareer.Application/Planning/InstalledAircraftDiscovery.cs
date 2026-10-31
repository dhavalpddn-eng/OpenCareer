using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Planning;

public enum InstalledAircraftDiscoveryAvailability
{
    Unavailable = 0,
    Available = 1
}

public sealed record InstalledAircraftDiscoverySnapshot(
    InstalledAircraftDiscoveryAvailability Availability,
    IReadOnlyList<AircraftRegistryObservation> Observations)
{
    public static InstalledAircraftDiscoverySnapshot Unavailable { get; } =
        new(
            InstalledAircraftDiscoveryAvailability.Unavailable,
            Array.Empty<AircraftRegistryObservation>());
}

public interface IInstalledAircraftDiscoverySource
{
    InstalledAircraftDiscoverySnapshot Current { get; }
}


public sealed class CompositeInstalledAircraftDiscoverySource(
    params IInstalledAircraftDiscoverySource[] sources)
    : IInstalledAircraftDiscoverySource
{
    private readonly IInstalledAircraftDiscoverySource[] _sources =
        sources?.ToArray()
        ?? throw new ArgumentNullException(nameof(sources));

    public InstalledAircraftDiscoverySnapshot Current
    {
        get
        {
            var observations =
                new Dictionary<(string ProviderId, string ProviderRecordId), AircraftRegistryObservation>();

            bool anyAvailable = false;
            bool anyUnavailable = false;

            foreach (IInstalledAircraftDiscoverySource source in _sources)
            {
                if (source is null)
                {
                    throw new InvalidOperationException(
                        "Installed-aircraft discovery sources cannot contain null entries.");
                }

                InstalledAircraftDiscoverySnapshot snapshot =
                    source.Current;

                if (snapshot.Availability
                    != InstalledAircraftDiscoveryAvailability.Available)
                {
                    anyUnavailable = true;
                    continue;
                }

                anyAvailable = true;

                foreach (AircraftRegistryObservation observation
                    in snapshot.Observations)
                {
                    ArgumentNullException.ThrowIfNull(observation);
                    observation.Validate();

                    if (!observation.IsInstalled)
                    {
                        throw new InvalidDataException(
                            "Installed-aircraft discovery cannot publish non-installed observations.");
                    }

                    var key =
                        (
                            observation.ProviderId.Trim(),
                            observation.ProviderRecordId.Trim()
                        );

                    observations.TryAdd(
                        key,
                        observation);
                }
            }

            // Empty is authoritative only if every provider actually answered. A local
            // package catalog cannot prove removal of aircraft supplied by an unavailable live source.
            if (!anyAvailable || (observations.Count == 0 && anyUnavailable))
                return InstalledAircraftDiscoverySnapshot.Unavailable;

            return new(
                InstalledAircraftDiscoveryAvailability.Available,
                observations.Values
                    .OrderBy(
                        static item =>
                            item.CanonicalAircraftId,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        static item =>
                            item.ProviderId,
                        StringComparer.Ordinal)
                    .ThenBy(
                        static item =>
                            item.ProviderRecordId,
                        StringComparer.Ordinal)
                    .ToArray());
        }
    }
}
