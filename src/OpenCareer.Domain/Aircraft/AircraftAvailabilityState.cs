namespace OpenCareer.Domain.Aircraft;

public enum AircraftAvailabilityStatus
{
    Available = 0,
    Unavailable = 1
}

public sealed record AircraftAvailabilityState(
    string CanonicalAircraftId,
    AircraftAvailabilityStatus Status)
{
    public bool IsAvailable => Status == AircraftAvailabilityStatus.Available;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalAircraftId);

        if (!Enum.IsDefined(Status))
            throw new ArgumentOutOfRangeException(nameof(Status));
    }

    public AircraftAvailabilityState Normalize()
    {
        Validate();

        return this with
        {
            CanonicalAircraftId = CanonicalAircraftId.Trim()
        };
    }
}
