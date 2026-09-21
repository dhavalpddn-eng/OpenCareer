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
