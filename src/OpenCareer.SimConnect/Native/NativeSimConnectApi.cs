using System.Runtime.InteropServices;

namespace OpenCareer.SimConnect.Native;

internal sealed class NativeSimConnectApi : ISimConnectApi
{
    private const uint SimConnectObjectIdUser = 0;
    private const uint SimConnectDataTypeFloat64 = 4;
    private const uint SimConnectUnused = uint.MaxValue;

    public int Open(out nint handle, nint notificationEvent)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("SimConnect requires Windows x64.");

        return SimConnect_Open(out handle, "OpenCareer", nint.Zero, 0, notificationEvent, 0);
    }

    public int CallDispatch(nint handle, DispatchCallback callback) =>
        SimConnect_CallDispatch(handle, callback, nint.Zero);

    public int AddToDataDefinition(nint handle, uint definitionId, string datumName, string unitsName) =>
        SimConnect_AddToDataDefinition(
            handle,
            definitionId,
            datumName,
            unitsName,
            SimConnectDataTypeFloat64,
            0,
            SimConnectUnused);

    public int RequestDataOnUserAircraft(
        nint handle,
        uint requestId,
        uint definitionId,
        SimConnectPeriod period) =>
        RequestDataOnObject(
            handle,
            requestId,
            definitionId,
            SimConnectObjectIdUser,
            period);

    public int RequestDataOnObject(
        nint handle,
        uint requestId,
        uint definitionId,
        uint objectId,
        SimConnectPeriod period) =>
        SimConnect_RequestDataOnSimObject(
            handle,
            requestId,
            definitionId,
            objectId,
            (uint)period,
            0,
            0,
            0,
            0);

    public int SubscribeToSystemEvent(nint handle, uint eventId, string eventName) =>
        SimConnect_SubscribeToSystemEvent(handle, eventId, eventName);

    public int RequestSystemState(nint handle, uint requestId) =>
        SimConnect_RequestSystemState(handle, requestId, "Sim");

    public int CreateEnrouteAtcAircraftEx1(
        nint handle,
        string containerTitle,
        string livery,
        string tailNumber,
        int flightNumber,
        string flightPlanPath,
        double flightPlanPosition,
        bool touchAndGo,
        uint requestId) =>
        SimConnect_AICreateEnrouteATCAircraft_EX1(
            handle,
            containerTitle,
            livery,
            tailNumber,
            flightNumber,
            flightPlanPath,
            flightPlanPosition,
            touchAndGo ? 1 : 0,
            requestId);

    public int RemoveObject(nint handle, uint objectId, uint requestId) =>
        SimConnect_AIRemoveObject(handle, objectId, requestId);

    public int Close(nint handle) => SimConnect_Close(handle);

    // Official native ABI; no dependency on the legacy .NET Framework managed wrapper.
    // The Windows application supplies the MSFS 2024 SDK's x64 SimConnect.dll beside its executable.
    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    private static extern int SimConnect_Open(
        out nint handle,
        string name,
        nint window,
        uint windowMessage,
        nint notificationEvent,
        uint configIndex);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    private static extern int SimConnect_CallDispatch(
        nint handle,
        DispatchCallback callback,
        nint context);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    private static extern int SimConnect_AddToDataDefinition(
        nint handle,
        uint definitionId,
        string datumName,
        string unitsName,
        uint datumType,
        float epsilon,
        uint datumId);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    private static extern int SimConnect_RequestDataOnSimObject(
        nint handle,
        uint requestId,
        uint definitionId,
        uint objectId,
        uint period,
        uint flags,
        uint origin,
        uint interval,
        uint limit);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    private static extern int SimConnect_SubscribeToSystemEvent(
        nint handle,
        uint eventId,
        string eventName);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    private static extern int SimConnect_RequestSystemState(
        nint handle,
        uint requestId,
        string state);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    private static extern int SimConnect_AICreateEnrouteATCAircraft_EX1(
        nint handle,
        string containerTitle,
        string livery,
        string tailNumber,
        int flightNumber,
        string flightPlanPath,
        double flightPlanPosition,
        int touchAndGo,
        uint requestId);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    private static extern int SimConnect_AIRemoveObject(
        nint handle,
        uint objectId,
        uint requestId);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    private static extern int SimConnect_Close(nint handle);
}
