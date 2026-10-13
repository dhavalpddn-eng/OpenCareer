using OpenCareer.Application.Simulator;

namespace OpenCareer.SimConnect;

public sealed class SimConnectInputBindingDiscoverySource(
    ISimulatorConnection connection) : IInputBindingDiscoverySource
{
    private const string UnsupportedDetail =
        "Official SimConnect can enumerate connected controllers and current-aircraft input-event descriptors, but it does not expose the user's active Controls profile or action-to-key/button assignments.";

    private readonly ISimulatorConnection _connection =
        connection ?? throw new ArgumentNullException(nameof(connection));

    public InputBindingDiscoverySnapshot Read(
        IReadOnlyList<InputBindingActionIdentity> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        InputBindingActionIdentity[] validatedActions =
            actions
                .Select(action =>
                    (action ?? throw new ArgumentException(
                        "Binding actions cannot contain null entries.",
                        nameof(actions))).Validate())
                .ToArray();

        string connectionDetail =
            _connection.Current.State == SimulatorConnectionState.Connected
                ? "MSFS is connected."
                : "MSFS is not connected.";

        InputBindingEvidence[] bindings =
            validatedActions
                .Select(action =>
                    new InputBindingEvidence(
                        action,
                        Device: null,
                        Profile: null,
                        InputBindingState.Unavailable,
                        "UNAVAILABLE",
                        UnsupportedDetail))
                .ToArray();

        return new InputBindingDiscoverySnapshot(
            InputBindingState.Unavailable,
            bindings,
            $"{connectionDetail} {UnsupportedDetail}");
    }
}
