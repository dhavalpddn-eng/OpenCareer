# Flight session core

Updated: 2026-09-19.

Branch: `feature/flight-session-core-current`
Draft PR: #11 into `feature/m1-simulation-core`.

## Purpose

This is the first bounded implementation slice for the real FlightSession backbone that MBL-03 tutorials and later jobs will consume.

It builds on the existing `FlightTrackingStateMachine`, `FlightTimeLedger`, normalized telemetry boundary, and tutorial system rather than creating a parallel flight lifecycle.

## Implemented in this slice

- `FlightSession` aggregate with stable session/contract identity;
- explicit `FlightSessionStatus` separate from the detailed tracking state;
- `FlightSessionMilestones` for aircraft-ready, engine start, taxi-out, takeoff roll, takeoff, approach, touchdown, final landing/rollout, taxi-in, parking, shutdown, completion and interruption;
- `FlightSessionEngine` that composes the existing `FlightTrackingStateMachine` and `FlightTimeLedger`;
- disconnect -> suspended behavior without erasing the operational phase;
- continuity-validated reconnect -> resume behavior;
- failed continuity/crash -> interrupted terminal session while preserving partial flight evidence;
- touch-and-go does not become a final landing;
- rejected takeoff returns to taxi and does not record a takeoff milestone;
- simulation acceleration contributes simulated operational time but does not multiply career-credit time;
- cancellation is terminal and cannot be overwritten by later telemetry;
- `FlightSessionCoordinator` in the Application layer as the seam for Current Flight, MBL-03 tutorial state, and the next SQLite recovery slice.

## Current state flow represented

`Accepted -> ReadyForStart -> EngineStart -> TaxiOut -> DepartureReady -> Airborne -> Landed -> TaxiIn -> Parked -> Shutdown -> Complete`

The detailed detector still retains `Approach`, `LandingEpisode`, `Suspended`, `Interrupted`, bounce, touch-and-go and rejected-takeoff semantics underneath that user-facing operational flow.

Preflight servicing/loading phases remain mission/workflow-driven rather than guessed from simulator telemetry.

## Tests added

`FlightSessionEngineTests` covers:

- complete cold-and-dark lifecycle;
- milestone timestamps;
- disconnect/reconnect continuity;
- interrupted partial-flight preservation;
- touch-and-go;
- rejected takeoff;
- time-ledger integration;
- cancellation;
- invalid session IDs.

`FlightSessionCoordinatorTests` covers:

- single active-session enforcement;
- state-change publication;
- terminal clear/restart;
- restore seam for persistence;
- protection against clearing an active session.

## CI note

The repository's current workflows automatically validate pull requests targeting `main`; this nested draft PR targets `feature/m1-simulation-core`, so it does not automatically receive the normal Linux/Windows validation runs.

PR #11 is currently reported mergeable against the active m1 branch. Do not claim this branch is CI-green until it is exercised by a manual/workflow validation path or folded into the parent m1 PR validation.

## Next bounded work

1. Wire the persistence service into app DI/Current Flight without moving business logic into the UI.
2. Add restart/recovery presentation for active, suspended and interrupted sessions.
3. Add route/leg summary persistence and duplicate-effect guards needed by jobs/logbook.
4. Keep takeoff/landing thresholds configurable and defer real-aircraft calibration until Windows/MSFS access is available.

No merge is requested yet.
## Telemetry evidence processor added

The branch now also contains `FlightTelemetryEvidenceProcessor`, `FlightEvidenceObservation` and `FlightEvidenceProcessorOptions`.

It converts normalized telemetry into conservative high-confidence evidence without exposing SimConnect types to flight logic.

Current derived evidence includes:

- stable telemetry sample streak;
- valid loaded-aircraft readiness supplied explicitly by the application boundary;
- engine-start transition;
- self-powered taxi movement;
- takeoff candidate;
- rejected takeoff after a prior takeoff candidate;
- airborne confirmation using multiple samples;
- approach evidence from low descending airborne telemetry;
- touchdown confirmation using multiple grounded samples after confirmed airborne flight;
- parking confirmation from low speed + parking brake;
- operation completion only when the application says terminal conditions are satisfied and telemetry confirms parked + engines off.

Pause and slew suppress operational transitions. Invalid numeric telemetry cannot become stable evidence. Backward timestamps are rejected.

Bounce, touch-and-go and go-around are intentionally still owned by the more detailed landing-event layer; this first processor does not pretend that 1 Hz generic telemetry is enough to classify every landing subtype.

All thresholds are explicit options and remain provisional until broader real-aircraft MSFS calibration is available.

`FlightTelemetryEvidenceProcessorTests` adds synthetic coverage for stable-sample arming, engine start/taxi, airborne hysteresis, touchdown hysteresis, rejected takeoff, pause/slew suppression, parking/completion, invalid telemetry, backward timestamps and disconnect streak reset.
## SQLite checkpoint/recovery added

The current branch now includes the first durable active-flight recovery path:

- `IFlightSessionCheckpointStore` in Application;
- `OpenCareer.Infrastructure` with `Microsoft.Data.Sqlite` 10.0.12, matching the existing economy workstream project/package choice;
- `SqliteFlightSessionCheckpointStore` using one authoritative current-session slot;
- schema-version and metadata/payload consistency validation;
- atomic single-statement upsert for the latest checkpoint;
- reopen/load recovery;
- clear after terminal acknowledgement;
- serialized concurrent writes;
- `FlightSessionPersistenceService` that writes the next state before publishing it to the in-memory coordinator.

The write-before-publish ordering is intentional: a failed SQLite checkpoint must not advance the app's authoritative in-memory FlightSession beyond what can survive an app crash.

Added tests cover SQLite reopen, newer-checkpoint replacement, clear, unsupported schema rejection, concurrent saves, persistence-failure behavior, restore, and terminal cleanup.

## Branch synchronization note

The original draft PR #9 was based on an older m1 head and was closed without merge. This work was forward-ported onto the then-current `feature/m1-simulation-core` and continues in draft PR #11.

The m1 branch remains active in other project chats, so PR CI/merge testing is the integration guard rather than assuming the base is static.

## Remaining no-PC work for item #1

1. Wire `FlightSessionPersistenceService` into app DI and Current Flight without creating UI business logic.
2. Add automatic checkpoint cadence/debouncing so meaningful transitions persist immediately while steady telemetry does not write SQLite every sample.
3. Add restart recovery UX/state for Active, Suspended and Interrupted sessions.
4. Add route/leg summary fields needed by the later logbook without storing raw telemetry frames.
5. Leave real-aircraft threshold calibration and live SimConnect validation for when the Windows/MSFS PC is available.