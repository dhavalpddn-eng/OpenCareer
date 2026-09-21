namespace OpenCareer.Application.Tutorials;

/// <summary>
/// Tracks the first observed connected-to-disconnected simulator transition.
/// The pending offer survives reconnect or other UI activity until the tutorial
/// layer explicitly handles it.
/// </summary>
public sealed class SimulatorDisconnectTutorialTrigger
{
    private bool _initialized;
    private bool _wasConnected;
    private bool _handled;

    public bool ShouldOffer { get; private set; }

    public void Observe(bool isConnected)
    {
        if (_handled)
            return;

        if (!_initialized)
        {
            _initialized = true;
            _wasConnected = isConnected;
            return;
        }

        if (_wasConnected && !isConnected)
            ShouldOffer = true;

        _wasConnected = isConnected;
    }

    public void MarkHandled()
    {
        _handled = true;
        ShouldOffer = false;
    }
}
