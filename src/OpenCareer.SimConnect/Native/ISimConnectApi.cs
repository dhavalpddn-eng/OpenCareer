using System.Runtime.InteropServices;

namespace OpenCareer.SimConnect.Native;

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void DispatchCallback(nint data, uint size, nint context);

internal enum SimConnectPeriod : uint
{
    Never = 0,
    Once = 1,
    VisualFrame = 2,
    SimFrame = 3,
    Second = 4
}

internal enum SimConnectSimObjectType : uint
{
    User = 0,
    All = 1,
    Aircraft = 2
}

// Every call, including subscriptions and Close, belongs to the connection's single worker thread.
internal interface ISimConnectApi
{
    int Open(out nint handle, nint notificationEvent);
    int CallDispatch(nint handle, DispatchCallback callback);
    int AddToDataDefinition(nint handle, uint definitionId, string datumName, string unitsName);
    int AddStringToDataDefinition(nint handle, uint definitionId, string datumName);
    int RequestDataOnUserAircraft(nint handle, uint requestId, uint definitionId, SimConnectPeriod period);
    int SubscribeToSystemEvent(nint handle, uint eventId, string eventName);
    int MapClientEventToSimEvent(nint handle, uint eventId, string eventName);
    int TransmitClientEvent(nint handle, uint objectId, uint eventId, uint data, uint groupId, uint flags);
    int RequestSystemState(nint handle, uint requestId);
    int EnumerateSimObjectsAndLiveries(nint handle, uint requestId, SimConnectSimObjectType type);
    int AddToFacilityDefinition(nint handle, uint definitionId, string fieldName);
    int RequestFacilityData(
        nint handle,
        uint definitionId,
        uint requestId,
        string icao,
        string region);
    int GetLastSentPacketId(nint handle, out uint sendId);
    int Close(nint handle);
}
