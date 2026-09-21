using System.Runtime.InteropServices;

namespace OpenCareer.SimConnect.Native;

internal sealed class NativeSimConnectApi : ISimConnectApi
{
    private const string SimConnectLibraryName = "SimConnect.dll";
    private const uint SimConnectObjectIdUser = 0;
    private const uint SimConnectDataTypeFloat64 = 4;
    private const uint SimConnectUnused = uint.MaxValue;

    static NativeSimConnectApi()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(NativeSimConnectApi).Assembly,
            static (libraryName, _, _) =>
            {
                if (!string.Equals(
                        libraryName,
                        SimConnectLibraryName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return nint.Zero;
                }

                string localRuntimePath =
                    Path.Combine(
                        AppContext.BaseDirectory,
                        SimConnectLibraryName);

                return NativeLibrary.TryLoad(
                    localRuntimePath,
                    out nint handle)
                    ? handle
                    : nint.Zero;
            });
    }

    public int Open(out nint handle, nint notificationEvent)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("SimConnect requires Windows x64.");

        return SimConnect_Open(out handle, "OpenCareer", nint.Zero, 0, notificationEvent, 0);
    }

    public int CallDispatch(nint handle, DispatchCallback callback) =>
        SimConnect_CallDispatch(handle, callback, nint.Zero);

    public int AddToDataDefinition(nint handle, uint definitionId, string datumName, string unitsName) =>
        SimConnect_AddToDataDefinition(handle, definitionId, datumName, unitsName,
            SimConnectDataTypeFloat64, 0, SimConnectUnused);

    public int RequestDataOnUserAircraft(
        nint handle,
        uint requestId,
        uint definitionId,
        SimConnectPeriod period) =>
        SimConnect_RequestDataOnSimObject(
            handle,
            requestId,
            definitionId,
            SimConnectObjectIdUser,
            (uint)period,
            0,
            0,
            0,
            0);

    public int SubscribeToSystemEvent(nint handle, uint eventId, string eventName) =>
        SimConnect_SubscribeToSystemEvent(handle, eventId, eventName);

    public int RequestSystemState(nint handle, uint requestId) =>
        SimConnect_RequestSystemState(handle, requestId, "Sim");

    public int Close(nint handle) => SimConnect_Close(handle);

    // Official native ABI; no dependency on the legacy .NET Framework managed wrapper.
    // The Windows application supplies the MSFS 2024 SDK's x64 SimConnect.dll beside its executable.
    [DllImport(SimConnectLibraryName, ExactSpelling = true, CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int SimConnect_Open(
        out nint handle, string name, nint window, uint windowMessage, nint notificationEvent, uint configIndex);

    [DllImport(SimConnectLibraryName, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int SimConnect_CallDispatch(nint handle, DispatchCallback callback, nint context);

    [DllImport(SimConnectLibraryName, ExactSpelling = true, CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int SimConnect_AddToDataDefinition(
        nint handle,
        uint definitionId,
        string datumName,
        string unitsName,
        uint datumType,
        float epsilon,
        uint datumId);

    [DllImport(SimConnectLibraryName, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
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

    [DllImport(SimConnectLibraryName, ExactSpelling = true, CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int SimConnect_SubscribeToSystemEvent(
        nint handle, uint eventId, string eventName);

    [DllImport(SimConnectLibraryName, ExactSpelling = true, CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int SimConnect_RequestSystemState(nint handle, uint requestId, string state);

    [DllImport(SimConnectLibraryName, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int SimConnect_Close(nint handle);
}
