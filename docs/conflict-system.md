# Conflict simulation foundation

Status: active implementation on `feature/military-conflict-system`, draft PR #10. Code head `360e93ea` is green: Linux passed 240/240 xUnit + 29/29 SimLab; Windows built WinUI and the live probe at 0 errors and passed 240/240 xUnit.

## Boundary

Microsoft Flight Simulator 2024 is the flight environment only.

```text
MSFS / SimConnect telemetry
    -> normalized AircraftTelemetrySnapshot
    -> OpenCareer mission telemetry evaluators
    -> OpenCareer conflict action/threat resolvers
    -> deterministic conflict world/campaign state
```

OpenCareer does not assume native MSFS weapons, targets, hit events, enemy AI combat behavior or aircraft combat damage.

## Implemented

- versioned deterministic `ConflictWorldState`,
- friendly/hostile/neutral ground units with strength, readiness and battlefield pressure,
- simulated friendly/hostile air units with deterministic movement,
- geographic sectors with friendly control and intelligence confidence,
- derived front-line snapshot from contested sector state,
- ground air-defense threats and airborne interceptor threats linked to simulated source units,
- deterministic ground attrition/control advancement,
- seeded fictional theater generator for repeatable sectors, ground units, air units and threats,
- battlefield-driven support generation for CAS, suppression, reconnaissance, logistics, patrol, escort and intercept,
- support-request lifecycle: Open / Reserved / Completed / Failed / Cancelled / Expired,
- duplicate-active-assignment protection,
- CAS/suppression mission lifecycle through ingress, on-station, authorized action, egress and completion,
- recon/logistics/patrol area-objective lifecycle,
- escort/intercept air-operation lifecycle against moving simulated air units,
- mission-authored telemetry gates using normalized player state,
- pause/slew/invalid ground-or-air state rejection,
- abstract deterministic precision, suppression, reconnaissance and intercept effects,
- effects fed back into unit strength/readiness, sector control/intelligence and linked threat severity,
- threat exposure detection from player position,
- deterministic simulated threat resolution against separate OpenCareer player-damage state,
- military affiliation, qualification, trust and assigned-aircraft authorization policy,
- explicit rule that installation/ownership of a military-capable aircraft never grants mission authorization by itself,
- application orchestration through `ConflictOperationsService`,
- versioned `ConflictCampaignCheckpoint` covering world state, military career state, simulated player damage and active combat/area/air-operation missions,
- checkpoint validation that rejects orphaned reserved requests, duplicate active mission IDs and request/mission type mismatches,
- SQLite conflict-campaign persistence in the shared `opencareer.db`, including schema migration v2, WAL-compatible storage and optimistic revision checks against stale writers,
- WinUI dependency-injection registration for `IConflictCampaignStore`,
- automatic app-start recovery of the most recently saved military conflict campaign into `ConflictCampaignRuntimeState`, with recovery failure isolated from the rest of app startup,
- database-consistent SQLite backup snapshots using SQLite backup semantics so WAL-backed military campaign and logbook state are captured as one valid database image,
- validated backup-archive restore staging plus restart-time database replacement before SQLite-backed application state is recovered; invalid/corrupt archives are rejected before the live database changes and a rollback snapshot protects replacement failures,
- persistent `ConflictCampaignState` with campaign phase, friendly momentum and deterministic strategic objectives for control, intelligence, readiness and threat reduction,
- deterministic fictional campaign identity with a persistent operation name plus distinct friendly/hostile faction names and short codes; legacy checkpoints without identity are accepted and backfilled deterministically on advance,
- `ConflictCampaignCoordinator` that creates, advances and revision-saves world + strategic state together,
- `MilitaryDispatchService` that exposes eligibility per support request and refuses acceptance when affiliation, qualification, aircraft assignment, capability/access or damage-state rules fail,
- `MilitaryCampaignMissionService` that persists acceptance, mission-stage progress, failure/completion lifecycle and simulated threat outcomes without allowing duplicate replay after recovery,
- `ConflictOperationsSnapshotBuilder` that projects operation/faction identity plus campaign/front/unit/threat/support/strategic-objective/trust/damage/active-operation state for the future Military/Government UI,
- deterministic xUnit tests across ground conflict, air conflict, mission families, request lifecycle, authorization, theater generation, SQLite campaign recovery, strategic evolution, dispatch authorization and the operations snapshot.

## Deliberately abstract

This is a career/game simulation, not a real weapon-performance model.

Mission effects use normalized game-quality inputs and authored mission windows rather than real weapon envelopes, current operational procedures or classified data. Conflict theaters are fictional/abstracted. Current real-world wars are not authoritative gameplay state.

## Still open

- user-facing Settings backup selection/confirmation for staging a restore archive; the validated restore backend and restart-time apply path are implemented,
- richer long-term theater evolution beyond the first phase/momentum/objective director,
- player career onboarding/persistence for military affiliation and qualifications,
- authoritative aircraft assignment issuance through the fleet/dispatch system,
- authoritative job/economy settlement integration,
- production Military/Government/Conflict UI,
- large deterministic balance/stress scenarios,
- live telemetry gameplay verification after the flight-runtime evidence pipeline is ready.

## Invariants

- Same state + seed/time/action/event IDs => same result.
- Processed player actions and threat engagements are idempotent.
- A support request exists because simulated world state creates a need.
- One active support request cannot be accepted by multiple missions.
- A persisted reserved request must have exactly one recoverable active mission, and optimistic checkpoint revisions reject stale concurrent writes.
- Mission objective completion is not career/job/economy settlement.
- Pause/slew cannot advance military objectives.
- Simulated damage remains OpenCareer state unless a separately verified simulator mechanism is explicitly implemented.
- Current real-world wars are not authoritative gameplay state.
- Military aircraft ownership/installation does not imply military authorization.
