# Simulator connection and first telemetry boundary

Scope: resilient SimConnect connection/reconnect, normalized user-aircraft telemetry, installed-aircraft enumeration, read-only airport/runway facility lookup, and user-position local weather sampling for dispatch. No mission completion or save mutation occurs in the SimConnect boundary.

## Structure and behavior

- `OpenCareer.Application/Simulator` owns `ISimulatorConnection`, `ISimulatorTelemetrySource` and immutable status/identity boundaries. It contains no WinUI, native SDK or database code.
- `OpenCareer.SimConnect` owns the native ABI and a single dedicated worker. Open, telemetry/facility definition setup, subscriptions, dispatch, system-state requests, aircraft enumeration, facility requests and close all run on that worker. Start is nonblocking/idempotent; Stop/Dispose await cleanup. A stopped instance may restart until disposed.
- Open success creates a handle; **only `SIMCONNECT_RECV_OPEN` plus successful telemetry setup confirms Connected**. Missing acknowledgement expires after 10 seconds. Duplicate acknowledgements do not create sessions.
- Quit, transport failure, protocol errors and an unresponsive connection close the current handle before another opens. Identity, telemetry and local-weather samples clear when the current simulator session ends.
- Automatic retries back off 1, 2, 4, 8, then 15 seconds maximum. Missing/wrong runtime and version mismatch wait 30 seconds. Cancellation interrupts the waits. Unexpected implementation errors stop the worker with a Faulted status.
- A Windows event wakes dispatch early; a 250 ms fallback processes messages. An empty generic `E_FAIL` is treated conservatively, not as proof that the simulator exited. A `RequestSystemState("Sim")` every five seconds checks liveness; its matching request ID must respond within 30 seconds. State 0 (simulator menus) is a valid response. This is not a flight-state detector.
- The native callback validates message lengths and copies the numeric `SIMCONNECT_RECV_SIMOBJECT_DATA` payload before returning. Managed exceptions never cross the unmanaged callback boundary. It never calls UI/domain code.
- WinUI reads immutable snapshots on its own 250 ms dispatcher timer; unchanged snapshots generate no UI notifications. Closing the window awaits asynchronous service disposal. SimConnect never runs on the UI thread.

## First telemetry subscription

After the OPEN acknowledgement the same SimConnect worker:

1. adds the mandatory aircraft-telemetry numeric data definition,
2. subscribes to the `Pause_EX1` system event,
3. requests the user aircraft once per second with `SimConnect_RequestDataOnSimObject`,
4. maps those SDK values into the existing `AircraftTelemetrySnapshot` domain record, and
5. separately attempts an optional local-weather numeric definition/request. Failure of the optional weather definition leaves the normal telemetry connection healthy.

All fields use `SIMCONNECT_DATATYPE_FLOAT64` so the callback payload has one fixed numeric layout. Booleans are normalized as false only for zero; nonzero values, including `-1`, are true.

## Airport / runway facility lookup

On each SimConnect session the worker installs one documented nested facility definition: `OPEN AIRPORT` -> airport `LATITUDE`, `LONGITUDE`, `NAME64`, `ICAO` -> `OPEN RUNWAY` -> runway `LATITUDE`, `LONGITUDE`, `HEADING`, `LENGTH`, `WIDTH`, `SURFACE`, primary/secondary runway number/designator and closed flags -> close markers. Application requests are queued and only one `SimConnect_RequestFacilityData` request is active at a time.

`SIMCONNECT_RECV_FACILITY_DATA` AIRPORT/RUNWAY messages are correlated by request/parent IDs and completed only at `SIMCONNECT_RECV_FACILITY_DATA_END`. Incomplete or inconsistent child lists fail closed. Runway meters are converted to feet; invalid/nonpositive dimensions and unknown/undefined surface codes remain unknown. The adapter publishes `AirportDataAuthority.LocalSimulator` observations, which outrank lower-authority reference providers and are never cross-filled from them.

Facility definition/request failure is nonfatal to telemetry. Immediate HRESULT failures, timeout/cancellation, disconnect and request-specific asynchronous `SIMCONNECT_RECV_EXCEPTION` responses return no local airport observation. Asynchronous failures are isolated using `SimConnect_GetLastSentPacketID` / exception send-ID correlation.

Hosted tests use synthetic native buffers and verify decoder layouts, one-worker ownership, mapping and failure isolation. They do not execute the native DLL. A focused real-MSFS airport/local-weather lookup remains required before calling these paths live-validated; it does not require another full FlightSession flight.

## Local simulator weather boundary

MSFS 2024's legacy SimConnect weather-station request functions are deprecated. OpenCareer therefore does not use them for arbitrary-airport weather. The optional local-weather definition requests the current user-aircraft position plus:

- `AMBIENT WIND DIRECTION` in degrees,
- `AMBIENT WIND VELOCITY` in knots, and
- `DENSITY ALTITUDE` in feet.

The SDK documents ambient wind direction/velocity as values at the user-aircraft position, with wind direction relative to true north. The adapter consequently refuses to reuse that sample for a remote airport. It first obtains the requested airport/runway geometry through the facility path and accepts a weather sample only when it is fresh (currently <=5 seconds) and geographically local to that airport/runway footprint.

Runway-relative headwind/crosswind is calculated against the facility runway `HEADING` using true-heading semantics. Primary/secondary closed flags are honored; for a physical two-ended runway, the best currently open end is represented in the normalized weather observation. Gust values remain unknown because Slice 10 does not claim a supported current SimConnect gust-observation source. Remote-airport weather remains unavailable until a separate supported source/bridge is verified.

| Local weather field | MSFS 2024 source | Requested units / mapping |
| --- | --- | --- |
| Sample position | `PLANE LATITUDE`, `PLANE LONGITUDE` | degrees; used only to prove locality to requested facility geometry |
| Wind direction | `AMBIENT WIND DIRECTION` | degrees true; normalized to `[0,360)` |
| Wind velocity | `AMBIENT WIND VELOCITY` | knots; negative/invalid values rejected |
| Density altitude | `DENSITY ALTITUDE` | feet |
| Runway reference | facility `RUNWAY LATITUDE`, `LONGITUDE`, `HEADING` | facility geometry; heading treated as true-north runway heading |
| Gust | none claimed | remains unknown; never fabricated |

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

## Production planning composition

Slice 11 registers the existing planning sources and orchestrator in the WinUI production service graph without moving simulator logic into UI code:

- `SimConnectInstalledAircraftObservationSource` and read-only `MsfsAircraftCfgObservationSource` feed `AircraftRegistryCatalogService`.
- `SimConnectAirportDataObservationSource` feeds `CachedAirportDataSource`; local simulator observations retain authority and no lower-authority runway facts are merged into them.
- `SimConnectLocalAirportWeatherSource` is the current `IAirportDispatchWeatherSource`.
- `OperationDispatchPlanningService` consumes only the Application interfaces.
- `MsfsUserConfigLocator` locates existing Steam/Store `UserCfg.opt` files and supports the optional `OPENCAREER_MSFS2024_USERCFG` override. It reads `InstalledPackagesPath` only; OpenCareer does not edit `UserCfg.opt`.

No external airport/weather source, qualification gate, authorization gate or Jobs generator is introduced by this composition.

## Native runtime and Windows build

The adapter currently uses eleven documented native exports through P/Invoke: Open, CallDispatch, AddToDataDefinition, RequestDataOnSimObject, SubscribeToSystemEvent, RequestSystemState, EnumerateSimObjectsAndLiveries, AddToFacilityDefinition, RequestFacilityData, GetLastSentPacketID and Close. Slice 10 adds no second worker and no deprecated weather-station API. This avoids binding .NET 10 to the SDK's legacy .NET Framework managed wrapper. Official ABI signatures/layouts are recorded in source and decoder tests.

Supply the **x64 native `SimConnect.dll` from the installed MSFS 2024 SDK**. This is a runtime dependency, not a repository binary or a third-party NuGet wrapper. Search is restricted to the application directory. No SDK binaries are committed or downloaded by CI.

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

Slice 11 adds a focused facility/local-weather acceptance path. Start MSFS with the user aircraft physically at the airport being checked, then run for example:

```powershell
.\tools\run-live-probe.ps1 -Airport KRME -DurationSeconds 30
```

`KRME` remains a developer fixture only; any suitable test airport identifier may be supplied. The probe waits until live telemetry is flowing, requests the real facility data through the production SimConnect connection, then requests the Slice 10 local-weather observation. The JSONL trace receives an `airportValidation` record containing facility provenance, runway dimensions/surfaces/closure state, density altitude and runway-relative wind components. The probe returns exit code 2 when `-Airport` was requested but both facility and local-weather evidence were not established. This path does not start or validate a FlightSession.

The probe must be run on the user's Windows machine with the real SDK runtime and MSFS 2024. A successful CI build only proves the tool compiles.

Analyze a saved capture with [OpenCareer.TraceAnalysis](../tools/OpenCareer.TraceAnalysis/README.md). It runs without MSFS on Windows/Linux and reports observed snapshot cadence, field ranges, connection/pause/clearing evidence and malformed or incomplete records. A structurally clean report does not replace instrument comparison or the manual acceptance checklist below.

## CI verification

Slice 11 code validation head: `768cb42ebf28c234ed166a0fad4766155441d6c8`.

- Windows x64 Release: run `35535655890` — WinUI and `OpenCareer.LiveProbe` builds succeeded with **0 warnings, 0 errors**; **362/362 xUnit tests passed**.
- Linux: run `35535655870` — build succeeded with **0 warnings, 0 errors**; **362/362 xUnit tests** and **29/29 SimLab scenarios** passed.

The tests inject native-call results and raw SDK-shaped callback buffers. They prove deterministic mapping, source composition behavior and tool compilation, but they do not execute the native simulator facility/weather path. The new `-Airport` probe path remains the focused real-MSFS acceptance step.

The suite also covers pure flight reducers and offline trace analysis. Analyzer fixtures are synthetic, covering cadence boundaries, missing/invalid data, truncated captures and cleanup/count consistency. Local CLI checks verify JSON output, exit codes and preservation of the input file.

**Neither mock tests nor Windows compilation prove live SDK behavior.**

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


### Facility API references

- [AddToFacilityDefinition](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Facilities/SimConnect_AddToFacilityDefinition.htm)
- [RequestFacilityData](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/facilities/simconnect_requestfacilitydata/)
- [SIMCONNECT_RECV_FACILITY_DATA](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/structures-and-enumerations/simconnect_recv_facility_data/)
- [GetLastSentPacketID](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/General/SimConnect_GetLastSentPacketID.htm)
