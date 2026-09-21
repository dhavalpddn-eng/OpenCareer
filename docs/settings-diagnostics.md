# OpenCareer Settings & Diagnostics

Status: implementation reference for MBL-23.

## Authority boundary

Settings and diagnostics are application-support state. They are not authoritative for money, aircraft ownership, mission completion, flight hours, settlement or career progression.

The persistent settings file may influence presentation and optional behavior, but it must never be used as a substitute for authoritative career storage.

## Local data layout

OpenCareer uses the user's local application-data directory:

```text
OpenCareer/
  settings.json
  ui-preferences.json
  opencareer.db
  opencareer.db-wal / opencareer.db-shm (while WAL is active)
  Logs/
    opencareer.log
    opencareer.log.1
  Backups/
    opencareer-backup-*.zip
  Diagnostics/
    opencareer-diagnostics-*.zip
```

`OpenCareerDataPaths` is the single path authority for these locations.

## Persisted preferences

Current schema version: 1.

- Measurement system:
  - Aviation — feet / knots / pounds
  - Metric — meters / km/h / kilograms
- Input-hint preference:
  - Controller + keyboard
  - Controller first
  - Keyboard first
- Show checklist every flight
- Automatically offer tutorials
- Reduce motion
- Allow optional online services

Defaults remain offline-first. Optional online services are disabled unless the player explicitly enables the permission gate.

## Unit behavior

The setting is active, not decorative.

Changing Aviation/Metric units causes `ShellViewModel` to reformat the latest telemetry snapshot immediately without waiting for another simulator sample.

Raw normalized domain telemetry remains in the established canonical units; only presentation changes.

## Simulator diagnostics

Settings reads immutable production boundaries only:

- `ISimulatorConnection.Current`
- `ISimulatorTelemetrySource.Latest`

Displayed diagnostics include:

- connection state,
- connection issue,
- simulator identity/version when reported,
- SimConnect version when reported,
- telemetry live/stale state,
- last telemetry timestamp,
- pause/slew/on-ground/airborne runtime state,
- OpenCareer/runtime version information.

Diagnostics do not create a FlightSession or change simulator state.

## Logging

`OpenCareerFileLoggerProvider` writes local information-or-higher application logs.

- primary file: `Logs/opencareer.log`
- rotation threshold: 4 MiB
- previous file: `Logs/opencareer.log.1`
- logging failures are swallowed so logging cannot crash OpenCareer

Never log API keys, passwords or other credentials.

## Diagnostic export

The Settings page can create a diagnostic ZIP under `Diagnostics/`.

The export may contain:

- application/runtime information,
- simulator connection state,
- non-location telemetry diagnostics,
- current app preferences,
- application log,
- settings/tutorial preference files.

Exact aircraft latitude/longitude are intentionally excluded from the generated diagnostic report.

## Backup

The Settings page can create a current local-data ZIP under `Backups/`.

The backup:

- includes currently implemented OpenCareer local data,
- excludes the `Backups/` and `Diagnostics/` trees to prevent recursion,
- skips temporary files,
- contains a manifest.

Logbook persistence now uses `opencareer.db`, but this ZIP routine is **not yet a database-consistent SQLite backup mechanism**. Copying a live WAL database as ordinary files is not sufficient for authoritative save/recovery guarantees. MBL-07 must add SQLite-consistent backup/recovery and integrate it with this Settings surface before career-save backups are considered reliable.

## Explicit dependencies

### MBL-05 — Controller + keyboard binding system

Settings persists which verified hint should be shown first, but it does not invent or discover bindings. Actual MSFS profile/device binding resolution belongs to MBL-05.

### MBL-07 — Persistent FlightSession / recovery

Settings exposes backup/recovery tools and status, but authoritative interrupted-flight/SQLite recovery belongs to MBL-07.

## Verification still required

Before MBL-23 leaves the master list:

1. Windows WinUI build succeeds on the current branch.
2. Launch locally with MSFS closed.
3. Change units; restart; confirm persistence.
4. Confirm live telemetry changes units when MSFS is connected.
5. Toggle checklist/tutorial/reduced-motion/online-service settings; restart; confirm persistence.
6. Create a backup and inspect its manifest.
7. Export diagnostics and confirm no coordinates are present in the report.
8. Open data/log/backup/export folders successfully.
9. Confirm diagnostics update during connect, disconnect and reconnect.
10. Verify keyboard focus, high-contrast behavior and text scaling on Settings.
