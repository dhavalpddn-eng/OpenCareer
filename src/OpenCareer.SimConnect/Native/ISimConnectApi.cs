using System.Runtime.InteropServices;

namespace OpenCareer.SimConnect.Native;

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void DispatchCallback(nint data, uint size, nint context);

// Every call, including Close, belongs to the connection's single worker thread.
internal interface ISimConnectApi
{
    int Open(out nint handle, nint notificationEvent);
    int CallDispatch(nint handle, DispatchCallback callback);
    int RequestSystemState(nint handle, uint requestId);
    int Close(nint handle);
}
