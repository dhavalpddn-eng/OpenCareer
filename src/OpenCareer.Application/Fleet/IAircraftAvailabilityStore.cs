using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Fleet;

public interface IAircraftAvailabilityStore
{
    Task<AircraftAvailabilityState?> FindAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        AircraftAvailabilityState state,
        CancellationToken cancellationToken = default);
}
