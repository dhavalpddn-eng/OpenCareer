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

// Every call, including AI-object calls, subscriptions and Close, belongs to the connection's single worker thread.
internal interface ISimConnectApi
{
    int Open(out nint handle, nint notificationEvent);
    int CallDispatch(nint handle, DispatchCallback callback);
    int AddToDataDefinition(nint handle, uint definitionId, string datumName, string unitsName);
    int RequestDataOnUserAircraft(nint handle, uint requestId, uint definitionId, SimConnectPeriod period);
    int RequestDataOnObject(nint handle, uint requestId, uint definitionId, uint objectId, SimConnectPeriod period);
    int SubscribeToSystemEvent(nint handle, uint eventId, string eventName);
    int RequestSystemState(nint handle, uint requestId);
    int CreateEnrouteAtcAircraftEx1(
        nint handle,
        string containerTitle,
        string livery,
        string tailNumber,
        int flightNumber,
        string flightPlanPath,
        double flightPlanPosition,
        bool touchAndGo,
        uint requestId);
    int RemoveObject(nint handle, uint objectId, uint requestId);
    int Close(nint handle);
}
