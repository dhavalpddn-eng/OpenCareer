namespace OpenCareer.Application.Simulator;

public enum InputBindingState
{
    Bound = 0,
    Unbound = 1,
    Unknown = 2,
    Unavailable = 3
}

public sealed record InputBindingActionIdentity(
    string Id,
    string DisplayName)
{
    public InputBindingActionIdentity Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        return this;
    }
}

public sealed record InputBindingDeviceIdentity(
    string Id,
    string DisplayName);

public sealed record InputBindingProfileIdentity(
    string Id,
    string DisplayName,
    string? AircraftIdentity = null);

public sealed record InputBindingEvidence(
    InputBindingActionIdentity Action,
    InputBindingDeviceIdentity? Device,
    InputBindingProfileIdentity? Profile,
    InputBindingState State,
    string BindingDisplayText,
    string SourceDetail);

public sealed record InputBindingDiscoverySnapshot(
    InputBindingState State,
    IReadOnlyList<InputBindingEvidence> Bindings,
    string Detail)
{
    public static InputBindingDiscoverySnapshot Unavailable(string detail) =>
        new(
            InputBindingState.Unavailable,
            Array.Empty<InputBindingEvidence>(),
            detail);
}

public interface IInputBindingDiscoverySource
{
    InputBindingDiscoverySnapshot Read(
        IReadOnlyList<InputBindingActionIdentity> actions);
}
