# Simulator connection boundary

Scope: connection/reconnect only. No aircraft telemetry, flight detection, mission completion or save mutation.

## Structure and behavior

- `OpenCareer.Application/Simulator` owns `ISimulatorConnection` and immutable status/identity records. It has no WinUI, native SDK or database dependency.
- `OpenCareer.SimConnect` owns the native ABI and a single dedicated worker. Open, dispatch, system-state requests and close all run on that worker. Start is nonblocking/idempotent; Stop/Dispose await cleanup. A stopped instance may restart until disposed.
- Open success creates a handle; **only `SIMCONNECT_RECV_OPEN` confirms Connected**. Missing acknowledgement expires after 10 seconds. Duplicate acknowledgements do not create sessions.
- Quit, transport failure, protocol errors and an unresponsive connection close the current handle before another opens. Identity clears as soon as the status leaves Connected.
- Automatic retries back off 1, 2, 4, 8, then 15 seconds maximum. Missing/wrong runtime and version mismatch wait 30 seconds. Cancellation interrupts the waits. Unexpected implementation errors stop the worker with a Faulted status.
- A Windows event wakes dispatch early; a 250 ms fallback processes messages. An empty generic `E_FAIL` is treated conservatively, not as proof that the simulator exited. A `RequestSystemState("Sim")` every five seconds checks liveness; its matching request ID must respond within 30 seconds. State 0 (simulator menus) is a valid response. This is not a flight-state detector. No frame-rate subscription or network service is used.
- The native callback validates message lengths and contains managed exceptions. It never calls UI/domain code. Native resources are closed on their owning worker before the event handle is disposed.
- WinUI reads immutable snapshots on its own 250 ms dispatcher timer; unchanged snapshots generate no UI notifications. Closing the window awaits asynchronous service disposal. It never runs SimConnect work on the UI thread.

## Native runtime and Windows build

The adapter uses four documented native exports through P/Invoke: Open, CallDispatch, RequestSystemState and Close. This avoids binding .NET 10 to the SDK's legacy .NET Framework managed wrapper. The boundary is deliberately small; official ABI signatures/layouts are recorded in source and decoder tests.

Supply the **x64 native `SimConnect.dll` from the installed MSFS 2024 SDK**. This is a runtime dependency, not a repository binary or a third-party NuGet wrapper. Search is restricted to the application directory. No SDK binaries are committed or downloaded by CI.

The app project uses `MSFS2024_SDK/SimConnect SDK/lib/SimConnect.dll` when that existing SDK environment path is available. An explicit path overrides it:

```powershell
dotnet build src/OpenCareer.App/OpenCareer.App.csproj --configuration Release -p:Platform=x64 -p:SimConnectNativePath="C:\MSFS 2024 SDK\SimConnect SDK\lib\SimConnect.dll"
```

The file is copied beside the executable for build/publish. An explicitly supplied nonexistent path fails the build. With no runtime path, the shell still builds/launches and reports the missing component instead of crashing. Without MSFS running, a correctly supplied runtime yields Waiting for MSFS and automatic retries. Default SDK configuration index 0 is used; no `SimConnect.cfg` is created or rewritten.

## Verification and remaining acceptance gate

```sh
dotnet test tests/OpenCareer.Tests/OpenCareer.Tests.csproj --configuration Release
dotnet run --project src/OpenCareer.SimLab/OpenCareer.SimLab.csproj --configuration Release
```

Transport tests inject native-call results and raw callback buffers. They cover simulator absence, acknowledgement, duplicate start/open, serialized ownership, quit, abrupt transport loss, heartbeat request/response failure, mismatched heartbeat IDs, cancellation, timeout, missing/wrong runtime, malformed data, restart and disposal. ViewModel tests cover status mapping and stale connection/aircraft labels. Fake-clock tests advance deadlines without waiting real timeout durations. Windows CI builds the actual WinUI app and reruns tests.

**Neither mock tests nor Windows compilation prove live SDK behavior.** The following Windows/MSFS checks remain necessary with the user's installed SDK/runtime:

1. Launch without the native DLL: shell remains responsive and identifies the missing component.
2. Supply the correct DLL and launch while MSFS is closed: Waiting for MSFS; navigation works.
3. Launch MSFS 2024: Connecting becomes Connected after acknowledgement. Leave it in menus, then load a flight; do not infer an active flight from connection alone.
4. Pause and resume; remain connected if system-state responses continue. An unresponsive loading screen lasting over 30 seconds can cause a safe reconnect.
5. Exit/restart MSFS, then separately terminate it abruptly: identity clears and connection recovers automatically. Repeat twice; verify one connection in SimConnect Inspector.
6. Close OpenCareer while connected and while retrying: window/process exits, with the native handle closed. Native API calls themselves cannot be forcibly interrupted; a hung native call remains a live-test risk.

Debug logging records state changes and server exception codes; repetitive open failures are Debug-level. No flight/session data exists to restore at this stage. Actual native DLL execution, UI interaction and the F-22/KRME flight test are still unverified.

## Official references checked 2026-09-17

- [SDK overview and thread-safety requirement](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/SimConnect_SDK.htm)
- [Open and remote-disconnect behavior](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/General/SimConnect_Open.htm)
- [CallDispatch](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/General/SimConnect_CallDispatch.htm), [callback](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/General/DispatchProc.htm), [Close](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/General/SimConnect_Close.htm)
- [Receive header](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Structures_And_Enumerations/SIMCONNECT_RECV.htm), [message IDs](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Structures_And_Enumerations/SIMCONNECT_RECV_ID.htm), [Open payload](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Structures_And_Enumerations/SIMCONNECT_RECV_OPEN.htm), [exception payload](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Structures_And_Enumerations/SIMCONNECT_RECV_EXCEPTION.htm)
- [System-state request](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/General/SimConnect_RequestSystemState.htm), [system-state response](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Structures_And_Enumerations/SIMCONNECT_RECV_SYSTEM_STATE.htm)

Context7 was used to locate documentation; exact ABI declarations were checked directly on the official pages because its generated summaries contained inconsistent signatures.
