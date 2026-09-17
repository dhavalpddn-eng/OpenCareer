using System.Runtime.InteropServices;

namespace OpenCareer.SimConnect.Native;

internal sealed class NativeSimConnectApi : ISimConnectApi
{
    public int Open(out nint handle, nint notificationEvent)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("SimConnect requires Windows x64.");

        return SimConnect_Open(out handle, "OpenCareer", nint.Zero, 0, notificationEvent, 0);
    }

    public int CallDispatch(nint handle, DispatchCallback callback) =>
        SimConnect_CallDispatch(handle, callback, nint.Zero);

    public int Close(nint handle) => SimConnect_Close(handle);

    public int RequestSystemState(nint handle, uint requestId) =>
        SimConnect_RequestSystemState(handle, requestId, "Sim");

    // Official native ABI; no dependency on the legacy .NET Framework managed wrapper.
    // The Windows application supplies the MSFS 2024 SDK's x64 SimConnect.dll beside its executable.
    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int SimConnect_Open(
        out nint handle, string name, nint window, uint windowMessage, nint notificationEvent, uint configIndex);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int SimConnect_CallDispatch(nint handle, DispatchCallback callback, nint context);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int SimConnect_Close(nint handle);

    [DllImport("SimConnect.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
    private static extern int SimConnect_RequestSystemState(nint handle, uint requestId, string state);
}
