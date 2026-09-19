namespace OpenCareer.SimConnect;

public sealed record SimConnectConnectionOptions
{
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan RuntimeRetryDelay { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan DispatchInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        ValidateWait(InitialRetryDelay, nameof(InitialRetryDelay));
        ValidateWait(MaximumRetryDelay, nameof(MaximumRetryDelay));
        ValidateWait(RuntimeRetryDelay, nameof(RuntimeRetryDelay));
        ValidateWait(HandshakeTimeout, nameof(HandshakeTimeout));
        ValidateWait(DispatchInterval, nameof(DispatchInterval));
        ValidateWait(HeartbeatInterval, nameof(HeartbeatInterval));
        ValidateWait(HeartbeatTimeout, nameof(HeartbeatTimeout));
        if (MaximumRetryDelay < InitialRetryDelay)
            throw new ArgumentException("Maximum retry delay must not be shorter than the initial delay.");
    }

    private static void ValidateWait(TimeSpan value, string name)
    {
        if (value.TotalMilliseconds < 1 || value.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(name, "Waits must be between 1 ms and Int32.MaxValue ms.");
    }
}
