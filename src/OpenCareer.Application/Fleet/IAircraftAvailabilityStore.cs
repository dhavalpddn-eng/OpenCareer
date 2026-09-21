using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Fleet;

public enum AircraftReservationAcquireResult
{
    Acquired = 0,
    AlreadyHeld = 1,
    Unavailable = 2,
    HeldByAnotherReservation = 3
}

public enum AircraftReservationReleaseResult
{
    Released = 0,
    AlreadyReleased = 1,
    NotReserved = 2,
    HeldByAnotherReservation = 3
}

public interface IAircraftAvailabilityStore
{
    Task<AircraftAvailabilityState?> FindAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        AircraftAvailabilityState state,
        CancellationToken cancellationToken = default);

    Task<AircraftReservationAcquireResult> TryReserveAsync(
        string canonicalAircraftId,
        string reservationId,
        CancellationToken cancellationToken = default);

    Task<AircraftReservationReleaseResult> ReleaseReservationAsync(
        string canonicalAircraftId,
        string reservationId,
        CancellationToken cancellationToken = default);
}
