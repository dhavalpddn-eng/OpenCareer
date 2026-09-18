# Runnable integration test

Updated 2026-09-17.

The first combined test branch is `feature/runnable-integration`. It joins the current WinUI/SimConnect telemetry work with the deterministic job market, conflict simulation, persistent world feed and airfield-control foundation.

## One-command Windows validation

From the repo root:

```powershell
./tools/run-runnable-test.ps1
```

That command restores/builds the WinUI app, builds the production-backed SimConnect live probe and runs the integrated xUnit suite.

It does **not** require MSFS to be running. Without a SimConnect DLL it still validates the disconnected build path.

To build/test and launch the app:

```powershell
./tools/run-runnable-test.ps1 -LaunchApp
```

If `MSFS2024_SDK` is configured, the script automatically resolves the SDK x64 `SimConnect.dll`. Otherwise pass it explicitly:

```powershell
./tools/run-runnable-test.ps1 -SimConnectNativePath "C:\path\to\SimConnect.dll" -LaunchApp
```

For the first real simulator validation, start MSFS 2024, load the desired aircraft at KRME, then run:

```powershell
./tools/run-runnable-test.ps1 -SimConnectNativePath "C:\path\to\SimConnect.dll" -LaunchLiveProbe -ProbeOutput "./artifacts/live-probe.jsonl"
```

Use `-ProbeDurationSeconds 300` for a bounded five-minute trace. Omit it to keep the probe running until stopped.

## Manual live checklist

1. Start with MSFS closed: app should remain usable and report waiting/unavailable rather than crash.
2. Launch MSFS and load at KRME: connection should transition to Connected.
3. Observe normalized telemetry at approximately 1 Hz.
4. Exercise menu/pause/resume.
5. Quit and restart MSFS; confirm telemetry clears and reconnect recovers.
6. Test an abrupt simulator exit.
7. Preserve the JSONL probe trace for any discrepancy.

Passing CI and the script proves compilation, deterministic logic and mocked simulator lifecycle. It does **not** prove native SimConnect behavior until the live probe is observed against the installed MSFS 2024 runtime.
