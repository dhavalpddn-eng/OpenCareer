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

PR #11 is maintained against the active m1 branch. It must be revalidated after each m1 synchronization rather than assuming the base is static.

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

The m1 branch remains active. This branch was reconciled against m1 head `d01e1dc2d2358764895197bbbbb2316da98a415e`; PR CI/merge testing remains the integration guard rather than assuming the base is static.

## Remaining no-PC work for item #1

1. Wire `FlightSessionPersistenceService` into app DI and Current Flight without creating UI business logic.
2. Add automatic checkpoint cadence/debouncing so meaningful transitions persist immediately while steady telemetry does not write SQLite every sample.
3. Add restart recovery UX/state for Active, Suspended and Interrupted sessions.
4. Add route/leg summary fields needed by the later logbook without storing raw telemetry frames.
5. Leave real-aircraft threshold calibration and live SimConnect validation for when the Windows/MSFS PC is available.

## Restart, Current Flight and live tutorial integration

- App startup restores the SQLite checkpoint before the WinUI shell is activated.
- A recovered non-terminal Active checkpoint is immediately converted to `Suspended`; app restart therefore never implies continuity has already been proven.
- App shutdown flushes the current in-memory session even when the normal steady-state checkpoint interval has not elapsed.
- `OpenCareerDataPaths.DatabaseFile` is the canonical shared local `opencareer.db` path. FlightSession checkpoints use their own table in the same SQLite database as current Logbook persistence.
- Current Flight presents the authoritative FlightSession separately from raw/live telemetry: lifecycle status, recovery origin, career/airborne/block summaries, event counts and latest milestone.
- Recovered sessions are visibly labeled **RESTORED FROM LOCAL SAVE**.
- Interrupted sessions explicitly say that the partial flight was preserved and will not auto-complete.

MBL-03 now has a generic `ITutorialStepEvidenceSource`. The FlightSession implementation supplies evidence for first-job prepare/fly/arrive and selected carrier/banner steps. The tutorial overlay displays live evidence state but does not auto-advance merely because evidence was satisfied; the player still controls the instructional walkthrough.

## Runtime continuity guard

`FlightSessionRuntime` is the application-layer pump from immutable normalized simulator snapshots into the FlightSession pipeline. It never creates a career session from telemetry alone.

Recovery uses a persisted `FlightContinuityAnchor`:

- ground checkpoints must remain in a tight local position/altitude envelope;
- a grounded checkpoint cannot resume already airborne;
- airborne checkpoints permit distance proportional to elapsed wall time and a conservative maximum travel speed;
- implausible active-position jumps suspend the session before any new location is accepted;
- stable implausible reconnect evidence converts the record to `Interrupted` instead of carrying mission progress through a teleport;
- slew samples never replace the trustworthy continuity anchor.

The continuity envelope is intentionally configurable and conservative. It is not a substitute for the future aircraft-identity evidence; aircraft identity still needs the verified simulator/registry work.

## Hosted validation

Temporary validation PR #12 targeted `main` solely to run workflows and was closed without merge.

Code head `591ca18b6ef46e272fcf14f031e8f0275014a568` passed:

- Linux: **29/29 deterministic SimLab scenarios**;
- Linux: **186/186 xUnit**;
- Windows: **WinUI x64 build**;
- Windows: **LiveProbe build**;
- Windows: **186/186 xUnit**.

Draft implementation PR remains #11 into `feature/m1-simulation-core`. No merge is requested.

## API-ready next seam

External flight-information/airport/route APIs should feed later planning/reference adapters, not become authoritative FlightSession state. The next route/leg models should store provider-neutral planned/actual locations and evidence provenance so OurAirports, AirLabs or another approved source can enrich them without coupling the domain to one vendor.

## MBL-03 Engine Start tutorial slice

Branch: `feature/mbl03-engine-start`.

The first-job tutorial now separates preparation from engine start:

- `job-prepare` is satisfied by the persisted `AircraftReadyAt` FlightSession milestone rather than waiting for TaxiOut;
- new `job-engine-start` is satisfied only by the persisted `EngineStartAt` milestone;
- the tutorial stays on the current instruction after evidence is confirmed; the player still presses Next;
- interrupted and cancelled sessions cannot satisfy live tutorial evidence;
- the tutorial copy remains aircraft-neutral and does not invent a universal cockpit switch or key binding;
- the first-job tutorial definition is version 2 so older completed progress does not silently skip the added step.

This slice intentionally does not add taxi, takeoff, route coaching, bindings, checklist automation or economy behavior.


## MBL-03 Taxi Out tutorial slice

Branch: `feature/mbl03-taxi-out`.

The first-job tutorial now separates taxi-out from engine start and flight:

- new `job-taxi-out` sits between Engine Start and Fly;
- it is satisfied only by the persisted `TaxiOutAt` FlightSession milestone;
- taxi evidence remains owned by the existing normalized telemetry / FlightSession pipeline;
- tutorial confirmation still does not auto-advance;
- the copy explicitly excludes pushback, slew and unstable telemetry from claimed taxi progress;
- the first-job tutorial definition is version 3 so previously completed version-2 progress does not skip the new step.

This slice intentionally does not add takeoff, route coaching, taxi routing/guidance, controller bindings, checklist automation, jobs or economy behavior.


## MBL-03 Takeoff tutorial slice

Branch: `feature/mbl03-takeoff`.

The first-job tutorial now separates takeoff from the general mission-flight step:

- new `job-takeoff` sits between Taxi Out and Fly;
- it is satisfied only by the persisted `TakeoffAt` FlightSession milestone;
- entering takeoff roll alone does not satisfy the tutorial;
- the existing `job-fly` no longer treats one recorded takeoff as proof that mission flying is complete; route/climb evidence is deferred to its own slice;
- tutorial confirmation still does not auto-advance;
- the first-job tutorial definition is version 4 so previously completed version-3 progress does not skip the new step.

This slice intentionally does not add climb, route coaching, approach, landing, controller bindings, checklist automation, jobs or economy behavior.


## MBL-03 Initial Climb tutorial slice

Branch: `feature/mbl03-initial-climb`.

The first-job tutorial now separates initial climb from liftoff and later mission flying:

- new `job-initial-climb` sits between Takeoff and Fly;
- `FlightTelemetryEvidenceProcessor` derives climb evidence only after airborne flight has already been confirmed;
- default climb evidence requires 2 consecutive operational samples at at least 200 ft AGL and at least +100 ft/min vertical speed;
- pause, slew, descent, low climb rate or dropping below the AGL threshold resets the climb streak;
- confirmed climb is persisted as `InitialClimbAt` on `FlightSessionMilestones`;
- `job-fly` remains intentionally unmapped until route/mission-flight evidence is implemented in its own slice;
- tutorial confirmation still does not auto-advance;
- the first-job tutorial definition is version 5.

Thresholds are configurable and remain provisional until broader real-aircraft MSFS calibration is available.

This slice intentionally does not add route tracking, cruise, approach, landing, controller bindings, checklist automation, jobs or economy behavior.


## MBL-03 Mission Flight Progress tutorial slice

Branch: `feature/mbl03-route-progress`.

The first-job tutorial now gives `job-fly` a real live-evidence definition without pretending that OpenCareer already knows a mission route:

- mission-flight progress can begin only after `InitialClimbAt` has been established;
- the telemetry processor requires repeated operational airborne samples and at least 0.5 NM of accumulated great-circle path distance;
- paused, slewed or non-airborne samples cannot add path distance and reset the consecutive-sample streak;
- disconnects clear the segment anchor so reconnect movement cannot create a false distance jump;
- successful evidence is persisted as `MissionFlightProgressAt`;
- `job-fly` is satisfied only by that persisted milestone;
- this is evidence that the flight phase is genuinely underway, not proof of waypoint, airway or assigned-route compliance;
- the first-job tutorial definition is version 6.

Actual route geometry, waypoint sequencing and mission-objective compliance remain separate future mission-system work.

## MBL-03 Approach tutorial slice

Branch: `feature/mbl03-approach`.

The first-job tutorial adds `job-approach` after `job-fly`. The step reads the persisted `ApproachAt` milestone only when it was recorded after `MissionFlightProgressAt`. The tutorial remains on this step until the player selects Next.

The telemetry processor now requires two consecutive stable, operational, airborne descending samples at or below 2,000 ft AGL, following confirmed mission-flight progress. Pause, slew, disconnect, ground contact, or an insufficient descent resets the approach streak. Restored sessions use the persisted mission-flight milestone to resume detection without carrying an old approach streak across the interruption.

This confirms an airborne approach phase. It does not validate the destination, runway, clearance, route, landing or mission completion. The first-job tutorial definition is version 7.

## MBL-03 Landing tutorial slice

Branch: `feature/mbl03-landing`.

The first-job tutorial adds `job-land` after approach. Initial touchdown records `FirstTouchdownAt` but does not satisfy the step. The telemetry processor confirms landing rollout only after continued operational ground contact beyond touchdown confirmation and ground speed at or below 20 knots. The existing flight state engine then records `LandingAt` on transition to TaxiIn. The tutorial reads that persisted milestone after mission flight progress and approach; it still requires a manual Next. The first-job tutorial definition is version 8.

This confirms the aircraft landed and slowed down. It does not validate destination, parking, shutdown, unloading, or job completion.

## MBL-03 Taxi In tutorial slice

Branch: `feature/mbl03-taxi-in`.

The first-job tutorial adds `job-taxi-in` after landing. `TaxiInAt` is recorded on the same transition as landing rollout, so the tutorial uses a separate `TaxiInProgressAt` milestone recorded only on a later TaxiIn sample with stable, operational, self-powered ground movement between 3 and 15 knots. It remains on the step until the player selects Next. The first-job tutorial definition is version 9.

This confirms movement after landing. It does not verify a specific taxi route, parking stand, or arrival completion.
