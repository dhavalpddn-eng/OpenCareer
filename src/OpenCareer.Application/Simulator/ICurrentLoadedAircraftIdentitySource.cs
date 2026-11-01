namespace OpenCareer.Application.Simulator;

public enum CurrentLoadedAircraftIdentityStatus
{
    Unavailable = 0,
    Identified = 1
}

public sealed record CurrentLoadedAircraftIdentitySnapshot(
    CurrentLoadedAircraftIdentityStatus Status,
    string? CanonicalAircraftId)
{
    public static CurrentLoadedAircraftIdentitySnapshot Unavailable { get; } =
        new(CurrentLoadedAircraftIdentityStatus.Unavailable, null);

    public static CurrentLoadedAircraftIdentitySnapshot Identified(
        string canonicalAircraftId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);

        return new(
            CurrentLoadedAircraftIdentityStatus.Identified,
            canonicalAircraftId.Trim());
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Status))
            throw new ArgumentOutOfRangeException(nameof(Status));

        if (Status == CurrentLoadedAircraftIdentityStatus.Identified)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalAircraftId);
            return;
        }

        if (CanonicalAircraftId is not null)
        {
            throw new ArgumentException(
                "Unavailable loaded-aircraft identity cannot contain a canonical aircraft ID.",
                nameof(CanonicalAircraftId));
        }
    }
}

public interface ICurrentLoadedAircraftIdentitySource
{
    CurrentLoadedAircraftIdentitySnapshot Current { get; }
}
