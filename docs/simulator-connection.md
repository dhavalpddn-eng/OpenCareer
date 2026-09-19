# Simulator connection and first telemetry boundary

Scope: resilient SimConnect connection/reconnect plus first normalized user-aircraft telemetry. No flight-state detector, mission completion or save mutation.

## Structure and behavior

- `OpenCareer.Application/Simulator` owns `ISimulatorConnection`, `ISimulatorTelemetrySource` and immutable status/identity boundaries. It contains no WinUI, native SDK or database code.
- `OpenCareer.SimConnect` owns the native ABI and a single dedicated worker. Open, data-definition setup, subscriptions, dispatch, system-state requests and close all run on that worker. Polly controls retry timing only; it does not move native SimConnect calls off the dedicated worker. Start is nonblocking/idempotent; Stop/Dispose await cleanup. A stopped instance may restart until disposed.
- Open success creates a handle; **only `SIMCONNECT_RECV_OPEN` plus successful telemetry setup confirms Connected**. Missing acknowledgement expires after 10 seconds. Duplicate acknowledgements do not create sessions.
- Quit, transport failure, protocol errors and an unresponsive connection close the current handle before another opens. Identity and telemetry clear when the current simulator session ends.
- Automatic reconnect uses Polly v8 (`Polly.Core` 8.8.0). Normal connection failures use exponential backoff from 1 second, capped at 15 seconds, with jitter. Missing/incompatible runtime and version mismatch use a 30-second retry delay. A previously healthy simulator session starts a fresh retry sequence after disconnect so old failures cannot inflate later reconnect delays. Cancellation interrupts Polly waits. Unexpected implementation errors stop the worker with a Faulted status.
- A Windows event wakes dispatch early; a 250 ms fallback processes messages. An empty generic `E_FAIL` is treated conservatively, not as proof that the simulator exited. A `RequestSystemState("Sim")` every five seconds checks liveness; its matching request ID must respond within 30 seconds. State 0 (simulator menus) is a valid response. This is not a flight-state detector.
- The native callback validates message lengths and copies the numeric `SIMCONNECT_RECV_SIMOBJECT_DATA` payload before returning. Managed exceptions never cross the unmanaged callback boundary. It never calls UI/domain code.
- WinUI reads immutable snapshots on its own 250 ms dispatcher timer; unchanged snapshots generate no UI notifications. Closing the window awaits asynchronous service disposal. SimConnect never runs on the UI thread.

## First telemetry subscription

After the OPEN acknowledgement the same SimConnect worker:

1. adds a single numeric data definition,
2. subscribes to the `Pause_EX1` system event,
3. requests the user aircraft once per second with `SimConnect_RequestDataOnSimObject`, and
4. maps SDK values into the existing `AircraftTelemetrySnapshot` domain record.

All fields use `SIMCONNECT_DATATYPE_FLOAT64` so the callback payload has one fixed numeric layout. Booleans are normalized as false only for zero; nonzero values, including `-1`, are true.

| Normalized field | MSFS 2024 source | Requested units / mapping |
| --- | --- | --- |
| Latitude / longitude | `PLANE LATITUDE`, `PLANE LONGITUDE` | degrees |
| MSL / AGL altitude | `PLANE ALTITUDE`, `PLANE ALT ABOVE GROUND` | feet |
| IAS / ground speed | `AIRSPEED INDICATED`, `GROUND VELOCITY` | knots |
| Vertical speed | `VERTICAL SPEED` | feet/second, converted to feet/minute |
| Heading | `PLANE HEADING DEGREES TRUE` | requested as degrees; normalized to `[0,360)` |
| Pitch / bank | `PLANE PITCH DEGREES`, `PLANE BANK DEGREES` | requested as degrees |
| Load factor | `G FORCE` | Gforce |
| Ground / parking brake | `SIM ON GROUND`, `BRAKE PARKING POSITION` | bool |
| Engines | `NUMBER OF ENGINES`, `GENERAL ENG COMBUSTION:1..4` | installed count plus bool combustion state |
| Fuel | `FUEL TOTAL QUANTITY WEIGHT EX1` | pounds, includes unusable fuel |
| Payload | `TOTAL WEIGHT - EMPTY WEIGHT - FUEL TOTAL QUANTITY WEIGHT EX1` | pounds, clamped to zero |
| Flaps | `FLAPS HANDLE PERCENT` | percent-over-100 converted to 0–100% |
| Gear | `GEAR TOTAL PCT EXTENDED` | percent; current first-pass `GearDown` threshold is >=95% |
| Slew | `IS SLEW ACTIVE` | bool |
| Pause | `Pause_EX1` system event | nonzero event data means at least one pause state is active |

The Current Flight page may display these values but still says **No active flight**. Telemetry availability does not create a FlightSession or prove takeoff/landing/mission state.

The first pass intentionally does not request aircraft title/type, autopilot detail, per-tank fuel or arbitrary payload-station arrays. Add fields only when the flight detector/session tracker has a concrete need and official SDK semantics are verified.

## Native runtime and Windows build

The adapter currently uses seven documented native exports through P/Invoke: Open, CallDispatch, AddToDataDefinition, RequestDataOnSimObject, SubscribeToSystemEvent, RequestSystemState and Close. This avoids binding .NET 10 to the SDK's legacy .NET Framework managed wrapper. Official ABI signatures/layouts are recorded in source and decoder tests.

Supply the **x64 native `SimConnect.dll` from the installed MSFS 2024 SDK**. This is a runtime dependency, not a repository binary or a third-party NuGet wrapper. Search is restricted to the application directory plus Windows System32 for SimConnect's native OS/runtime dependencies. No SDK binaries are committed or downloaded by CI.

The app project uses `MSFS2024_SDK/SimConnect SDK/lib/SimConnect.dll` when that existing SDK environment path is available. An explicit path overrides it:

```powershell
dotnet build src/OpenCareer.App/OpenCareer.App.csproj --configuration Release -p:Platform=x64 -p:SimConnectNativePath="C:\MSFS 2024 SDK\SimConnect SDK\lib\SimConnect.dll"
```

The file is copied beside the executable for build/publish. An explicitly supplied nonexistent path fails the build. With no runtime path, the shell still builds/launches and reports the missing component instead of crashing. Without MSFS running, a correctly supplied runtime should yield Waiting for MSFS and automatic retries. Default SDK configuration index 0 is used; no `SimConnect.cfg` is created or rewritten.

## Live runtime probe

`tools/OpenCareer.LiveProbe` is a Windows x64 console probe for the acceptance gate. It references the real `OpenCareer.SimConnect` project and instantiates the production `SimConnectConnection`; it does not duplicate native API calls or implement flight-state logic.

From the repository root:

```powershell
.\tools\run-live-probe.ps1
```

When `MSFS2024_SDK` is not configured, provide the installed SDK DLL explicitly:

```powershell
.\tools\run-live-probe.ps1 -SimConnectNativePath "C:\MSFS 2024 SDK\SimConnect SDK\lib\SimConnect.dll"
```

The probe prints connection transitions and compact telemetry lines, and writes a JSONL trace under `%LOCALAPPDATA%\OpenCareer\Diagnostics` by default. Use `-Output` to choose a path or `-DurationSeconds` for a bounded run. The trace records session metadata, simulator identity/version, connection issues, every normalized telemetry sample, telemetry clearing and the final clean-stop summary.

The probe must be run on the user's Windows machine with the real SDK runtime and MSFS 2024. A successful CI build only proves the tool compiles.

## CI verification

Tested implementation/tooling head: `2f63e56a8da585c7cbab4eb2d53d4a6b19a1b404`. Production telemetry implementation: `7fddbe1cc5d30fbe17411eef341f8f22dbbba92f`.

- Windows x64 Release: [run 35283864087](https://github.com/dhavalpddn-eng/OpenCareer/actions/runs/35283864087) — WinUI and `OpenCareer.LiveProbe` builds both succeeded with **0 warnings, 0 errors**; **74/74 xUnit tests passed**.
- Linux: [run 35283863937](https://github.com/dhavalpddn-eng/OpenCareer/actions/runs/35283863937) — **74/74 xUnit tests** and **29/29 SimLab scenarios** passed.

The tests inject native-call results and raw SDK-shaped callback buffers. Coverage includes simulator absence, acknowledgement, serialized ownership, quit/loss/retry, heartbeat failures, malformed messages, EVENT/SIMOBJECT_DATA decoding, telemetry setup failure, normalization, pause updates, stale-data clearing, restart/disposal and ViewModel display refresh.

**Neither mock tests nor Windows compilation prove live SDK behavior.**

## Live validation 2026-09-18

A real Windows/MSFS 2024 trace from the production `SimConnectConnection` is now captured and reviewed.

Observed:
- native x64 `SimConnect.dll` load passed after allowing application-directory plus `System32` dependency resolution;
- simulator connected and reported `SunRise`, app version `12.2.282174.999`, SimConnect `12.2.0.0`;
- 4,280 telemetry samples were recorded with no sequence gaps;
- steady-state sample interval median was ~1.000 s and p95 ~1.013 s;
- parked telemetry was coherent for position, MSL/AGL altitude, IAS, ground speed, vertical speed, heading, engine count, fuel, parking brake, pause and slew;
- `Pause_EX1` changed true/false without disconnect;
- live taxi/takeoff/airborne/landing/shutdown evidence was observed;
- a reconnect cycle cleared telemetry before reconnecting;
- final simulator loss also cleared stale telemetry.

Important findings:
- loading/menu transitions produced false `OnGround=false` samples with zero speed at placeholder position near 0/90 and ~206 ft AGL. Flight-state logic must require stable loaded-aircraft evidence plus multiple movement signals; one `OnGround` edge is not enough.
- `GearDown` stayed false for every sample, including while parked on the runway/ramp. `GEAR TOTAL PCT EXTENDED` is therefore not reliable enough by itself for this aircraft and needs an officially documented fallback/diagnostic before gear state is used for scoring or mission validation.
- touchdown telemetry at 1 Hz is sufficient to prove a ground transition but not sufficient for authoritative landing-rate/G scoring. Keep the planned bounded high-rate landing buffer.
- the trace contains no final `sessionEnd` record, so clean probe shutdown after the final simulator disconnect is still unverified.

Trace SHA-256: `955fa7b5a68ffbdafdf59f72bc174e02c1c6f42846d5b059a08b3977df5d3e7a`.

## Remaining live acceptance gate

Use the installed MSFS 2024 SDK/runtime on the user's Windows machine. Start `tools/run-live-probe.ps1` so the session produces a shareable JSONL trace. Record observed behavior; do not infer success from CI.

1. Launch without the native DLL: shell remains responsive and identifies the missing component.
2. Supply the correct DLL and launch while MSFS is closed: Waiting for MSFS; navigation works.
3. Launch MSFS 2024 and remain in menus: connection reaches Connected only after acknowledgement/setup; no active flight is claimed.
4. Load the first test at **KRME with the F-22**. Compare displayed position, MSL/AGL altitude, IAS/GS, vertical speed, heading, pitch/bank/G, on-ground state, engine state, fuel, flaps, gear and slew against the simulator/Developer Mode/SimConnect Inspector where practical. Check for sign, unit, range or aircraft-specific discrepancies.
5. Pause and resume using the simulator UI and verify `Pause_EX1` changes the displayed state without creating a disconnect. Exercise menu/load transitions too.
6. Taxi, take off, maneuver and land. This is telemetry validation only; do not add/claim a flight detector from one test event.
7. Exit/restart MSFS, then separately terminate it abruptly: telemetry and identity clear, then the connection recovers automatically. Repeat twice; verify one connection in SimConnect Inspector.
8. Close OpenCareer while connected and while retrying: window/process exits, with the native handle closed. Native API calls themselves cannot be forcibly interrupted; a hung native call remains a live-test risk.

Any live discrepancy should be corrected at the SimConnect mapping boundary before flight-state logic is built.

## Official references checked 2026-09-17

- [SDK overview and thread-safety requirement](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/SimConnect_SDK.htm)
- [Open and remote-disconnect behavior](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/General/SimConnect_Open.htm)
- [CallDispatch](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/General/SimConnect_CallDispatch.htm), [callback](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/General/DispatchProc.htm), [Close](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/General/SimConnect_Close.htm)
- [AddToDataDefinition](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Events_And_Data/SimConnect_AddToDataDefinition.htm), [RequestDataOnSimObject](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Events_And_Data/SimConnect_RequestDataOnSimObject.htm), [SubscribeToSystemEvent](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Events_And_Data/SimConnect_SubscribeToSystemEvent.htm)
- [SIMOBJECT_DATA payload](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Structures_And_Enumerations/SIMCONNECT_RECV_SIMOBJECT_DATA.htm), [periods](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Structures_And_Enumerations/SIMCONNECT_PERIOD.htm), [data types](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Structures_And_Enumerations/SIMCONNECT_DATATYPE.htm)
- [System events including `Pause_EX1`](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Events_And_Data/SimConnect_SubscribeToSystemEvent.htm)
- MSFS 2024 Aircraft SimVar tables for misc/flight-model/control/fuel variables under the official SDK documentation.

Context7 was used to locate current SDK documentation. Exact native ABI/layout details and SimVar semantics were kept behind tests because generated documentation summaries can omit or normalize SDK-specific details.
