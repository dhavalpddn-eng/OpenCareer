namespace OpenCareer.Domain.Flights;

public enum FlightSessionAircraftKind
{
    Owned = 1,
    Provider = 2
}

// InstanceId is an ownership ID for Owned, and a provider instance GUID for Provider.
public sealed record FlightSessionAircraftIdentity(
    FlightSessionAircraftKind Kind,
    string InstanceId,
    string AircraftId)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || string.IsNullOrWhiteSpace(InstanceId)
            || string.IsNullOrWhiteSpace(AircraftId))
            throw new ArgumentException("A flight requires an explicit aircraft source and instance.");

        if (Kind == FlightSessionAircraftKind.Provider
            && (!Guid.TryParse(InstanceId, out Guid id) || id == Guid.Empty))
            throw new ArgumentException("Provider instance identity must be a nonempty GUID.");
    }
}
