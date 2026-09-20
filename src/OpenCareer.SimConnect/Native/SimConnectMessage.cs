using OpenCareer.Application.Simulator;

namespace OpenCareer.SimConnect.Native;

// Values and layouts are from the official MSFS 2024 SIMCONNECT_RECV* documentation.
internal enum SimConnectMessageKind : uint
{
    None = 0,
    Exception = 1,
    Open = 2,
    Quit = 3,
    Event = 4,
    SimObjectData = 8,
    SystemState = 15,
    EnumerateSimObjectAndLiveryList = 39
}

internal sealed record SimConnectObjectLivery(
    string AircraftTitle,
    string LiveryName);

internal sealed record SimConnectMessage(
    SimConnectMessageKind Kind,
    SimulatorInfo? Simulator = null,
    uint ExceptionCode = 0,
    uint SendId = 0,
    uint ParameterIndex = 0,
    uint RequestId = 0,
    uint EventId = 0,
    uint EventData = 0,
    uint DefinitionId = 0,
    double[]? Data = null,
    uint ListEntryNumber = 0,
    uint ListOutOf = 0,
    IReadOnlyList<SimConnectObjectLivery>? ObjectLiveries = null);
