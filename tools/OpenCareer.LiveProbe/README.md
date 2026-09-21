# OpenCareer live SimConnect probe

This tool exists only for the live MSFS 2024 acceptance gate. It reuses the production `SimConnectConnection`; it does not implement another simulator adapter or any flight-state logic.

It records connection transitions and each normalized telemetry snapshot as JSON Lines while also printing compact console output. A normal interactive run continues until Ctrl+C.

## Preferred launch

From the repository root in PowerShell:

```powershell
.\tools\run-live-probe.ps1
```

The script uses `$env:MSFS2024_SDK\SimConnect SDK\lib\SimConnect.dll` when `MSFS2024_SDK` is configured. Otherwise pass the native x64 DLL explicitly:

```powershell
.\tools\run-live-probe.ps1 -SimConnectNativePath "C:\MSFS 2024 SDK\SimConnect SDK\lib\SimConnect.dll"
```

Optional arguments:

```powershell
.\tools\run-live-probe.ps1 -DurationSeconds 120
.\tools\run-live-probe.ps1 -Output "C:\Temp\opencareer-live.jsonl"
```

The default trace location is `%LOCALAPPDATA%\OpenCareer\Diagnostics\simconnect-live-<timestamp>.jsonl`.

Use the trace to verify simulator absence, connection acknowledgement, 1 Hz aircraft telemetry, pause/resume, menu/loading transitions, graceful quit, abrupt simulator exit, reconnect and final telemetry clearing. Do not interpret the trace as a FlightSession or takeoff/landing detector.

After capture, use [OpenCareer.TraceAnalysis](../OpenCareer.TraceAnalysis/README.md) to summarize cadence, numeric ranges, connection/pause observations and structural problems on Windows or Linux. The report supports manual review; it does not certify the live acceptance gate or calibrate flight thresholds.
