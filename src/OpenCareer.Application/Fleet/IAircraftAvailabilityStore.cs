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
}

public sealed record AircraftReservationOwnership(
    string CanonicalAircraftId,
    string ReservationId)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            CanonicalAircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            ReservationId);
    }
}

public interface IAircraftReservationLookup
{
    Task<AircraftReservationOwnership?> FindByReservationIdAsync(
        string reservationId,
        CancellationToken cancellationToken = default);
}

public interface IAircraftReservationStore
{
    Task<AircraftReservationAcquireResult> TryReserveAsync(
        string canonicalAircraftId,
        string reservationId,
        CancellationToken cancellationToken = default);

    Task<AircraftReservationReleaseResult> ReleaseReservationAsync(
        string canonicalAircraftId,
        string reservationId,
        CancellationToken cancellationToken = default);
}
