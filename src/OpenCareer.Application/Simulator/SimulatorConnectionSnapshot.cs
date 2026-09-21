namespace OpenCareer.Application.Simulator;

public enum SimulatorConnectionState
{
    Disconnected,
    WaitingForSimulator,
    Connecting,
    Connected,
    Reconnecting,
    Unavailable,
    Faulted
}

public enum SimulatorConnectionIssue
{
    None,
    ConnectionFailed,
    ConnectionLost,
    HandshakeTimeout,
    ResponseTimeout,
    RuntimeMissing,
    RuntimeIncompatible,
    VersionMismatch,
    UnsupportedPlatform,
    InvalidResponse,
    SimulatorError,
    UnexpectedError
}

public sealed record SimulatorInfo(string Name, Version ApplicationVersion, Version SimConnectVersion);

/// <summary>Connected confirms the SDK handshake; it does not imply an aircraft or active flight.</summary>
public sealed record SimulatorConnectionSnapshot(
    SimulatorConnectionState State,
    SimulatorConnectionIssue Issue = SimulatorConnectionIssue.None,
    SimulatorInfo? Simulator = null);
