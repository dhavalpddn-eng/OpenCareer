namespace OpenCareer.Domain.Aircraft;

public enum AircraftAvailabilityStatus
{
    Available = 0,
    Unavailable = 1
}

public sealed record AircraftAvailabilityState(
    string CanonicalAircraftId,
    AircraftAvailabilityStatus Status,
    string? ReservationId = null)
{
    public bool IsAvailable => Status == AircraftAvailabilityStatus.Available;
    public bool IsReserved => ReservationId is not null;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalAircraftId);

        if (!Enum.IsDefined(Status))
            throw new ArgumentOutOfRangeException(nameof(Status));

        if (ReservationId is not null && string.IsNullOrWhiteSpace(ReservationId))
            throw new ArgumentException("Reservation id cannot be blank.", nameof(ReservationId));

        if (Status == AircraftAvailabilityStatus.Available && ReservationId is not null)
        {
            throw new ArgumentException(
                "An available aircraft cannot carry a reservation id.",
                nameof(ReservationId));
        }
    }

    public AircraftAvailabilityState Normalize()
    {
        Validate();

        return this with
        {
            CanonicalAircraftId = CanonicalAircraftId.Trim(),
            ReservationId = ReservationId?.Trim()
        };
    }
}
