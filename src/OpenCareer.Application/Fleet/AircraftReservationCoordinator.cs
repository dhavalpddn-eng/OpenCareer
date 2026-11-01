using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Fleet;

public enum AircraftReservationRequestStatus
{
    Acquired = 0,
    AlreadyHeld = 1,
    Unavailable = 2,
    HeldByAnotherReservation = 3,
    AircraftNotFound = 4,
    AircraftNotInstalled = 5
}

public sealed record AircraftReservationRequestResult(
    AircraftReservationRequestStatus Status,
    string? CanonicalAircraftId);

public enum AircraftReservationReleaseRequestStatus
{
    Released = 0,
    AlreadyReleased = 1,
    NotReserved = 2,
    HeldByAnotherReservation = 3,
    AircraftNotFound = 4
}

public sealed record AircraftReservationReleaseRequestResult(
    AircraftReservationReleaseRequestStatus Status,
    string? CanonicalAircraftId);

public sealed class AircraftReservationCoordinator(
    IAircraftRegistrySource aircraftRegistry,
    IAircraftReservationStore reservationStore)
{
    public async Task<AircraftReservationRequestResult> ReserveAsync(
        string aircraftId,
        string reservationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationId);

        AircraftRegistryResolution? resolution = await aircraftRegistry
            .FindAircraftAsync(aircraftId, cancellationToken)
            .ConfigureAwait(false);

        if (resolution is null)
        {
            return new(
                AircraftReservationRequestStatus.AircraftNotFound,
                null);
        }

        if (resolution.InstallationStatus != AircraftInstallationStatus.Installed)
        {
            return new(
                AircraftReservationRequestStatus.AircraftNotInstalled,
                resolution.CanonicalAircraftId);
        }

        AircraftReservationAcquireResult result = await reservationStore
            .TryReserveAsync(
                resolution.CanonicalAircraftId,
                reservationId,
                cancellationToken)
            .ConfigureAwait(false);

        return new(
            result switch
            {
                AircraftReservationAcquireResult.Acquired =>
                    AircraftReservationRequestStatus.Acquired,
                AircraftReservationAcquireResult.AlreadyHeld =>
                    AircraftReservationRequestStatus.AlreadyHeld,
                AircraftReservationAcquireResult.Unavailable =>
                    AircraftReservationRequestStatus.Unavailable,
                AircraftReservationAcquireResult.HeldByAnotherReservation =>
                    AircraftReservationRequestStatus.HeldByAnotherReservation,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(result),
                    result,
                    "Unknown aircraft reservation result.")
            },
            resolution.CanonicalAircraftId);
    }

    public async Task<AircraftReservationReleaseRequestResult> ReleaseAsync(
        string aircraftId,
        string reservationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationId);

        AircraftRegistryResolution? resolution = await aircraftRegistry
            .FindAircraftAsync(aircraftId, cancellationToken)
            .ConfigureAwait(false);

        if (resolution is null)
        {
            return new(
                AircraftReservationReleaseRequestStatus.AircraftNotFound,
                null);
        }

        AircraftReservationReleaseResult result = await reservationStore
            .ReleaseReservationAsync(
                resolution.CanonicalAircraftId,
                reservationId,
                cancellationToken)
            .ConfigureAwait(false);

        return new(
            result switch
            {
                AircraftReservationReleaseResult.Released =>
                    AircraftReservationReleaseRequestStatus.Released,
                AircraftReservationReleaseResult.AlreadyReleased =>
                    AircraftReservationReleaseRequestStatus.AlreadyReleased,
                AircraftReservationReleaseResult.NotReserved =>
                    AircraftReservationReleaseRequestStatus.NotReserved,
                AircraftReservationReleaseResult.HeldByAnotherReservation =>
                    AircraftReservationReleaseRequestStatus.HeldByAnotherReservation,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(result),
                    result,
                    "Unknown aircraft reservation release result.")
            },
            resolution.CanonicalAircraftId);
    }
}
