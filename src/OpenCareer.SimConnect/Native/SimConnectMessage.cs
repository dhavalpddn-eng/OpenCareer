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
    FacilityData = 28,
    FacilityDataEnd = 29,
    EnumerateSimObjectAndLiveryList = 38
}

internal enum SimConnectFacilityDataType : uint
{
    Airport = 0,
    Runway = 1
}

internal sealed record SimConnectObjectLivery(
    string AircraftTitle,
    string LiveryName);

internal sealed record SimConnectAirportFacilityData(
    double LatitudeDegrees,
    double LongitudeDegrees,
    string Name,
    string Icao);

internal sealed record SimConnectRunwayFacilityData(
    double CenterLatitudeDegrees,
    double CenterLongitudeDegrees,
    float HeadingTrueDegrees,
    float LengthMeters,
    float WidthMeters,
    int Surface,
    int PrimaryNumber,
    int PrimaryDesignator,
    int SecondaryNumber,
    int SecondaryDesignator,
    bool PrimaryClosed,
    bool SecondaryClosed);

internal sealed record SimConnectFacilityDecodeDiagnostic(
    uint CallbackBufferSize,
    uint DeclaredRecvSize,
    uint MessageKind,
    uint RequestId,
    uint? UniqueRequestId,
    uint? ParentUniqueRequestId,
    uint? FacilityType,
    uint? IsListItem,
    uint? ItemIndex,
    uint? ListSize,
    uint RequiredMinimumSize,
    string FailureReason);

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
    string? StringData = null,
    uint ListEntryNumber = 0,
    uint ListOutOf = 0,
    IReadOnlyList<SimConnectObjectLivery>? ObjectLiveries = null,
    SimConnectFacilityDataType? FacilityDataType = null,
    uint UniqueRequestId = 0,
    uint ParentUniqueRequestId = 0,
    bool IsListItem = false,
    uint ItemIndex = 0,
    uint ListSize = 0,
    SimConnectAirportFacilityData? AirportFacilityData = null,
    SimConnectRunwayFacilityData? RunwayFacilityData = null,
    string? FacilityDecodeError = null,
    SimConnectFacilityDecodeDiagnostic? FacilityDecodeDiagnostic = null);
