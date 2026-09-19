# Flight session core

Updated: 2026-09-19.

Branch: `feature/flight-session-core`
Draft PR: #9 into `feature/m1-simulation-core`.

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

At the time this branch was created, both GitHub Actions workflows were failing before runner steps began, including a run triggered from the unchanged base commit. The connector returned jobs with no steps/logs, so this is not currently attributable to the new flight-session code.

Do not claim green CI for this branch until Actions actually executes the build/tests.

## Next bounded work

1. Add the telemetry-to-`FlightStateEvidence` processor with conservative hysteresis and synthetic tests.
2. Add SQLite active-session checkpoint/recovery.
3. Expose the coordinator snapshot to Current Flight and MBL-03 checklist steps.
4. Calibrate takeoff/landing thresholds against real MSFS traces when the user's PC is available.

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