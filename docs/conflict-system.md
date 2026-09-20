# Conflict simulation foundation

Status: active implementation on `feature/military-conflict-system`, draft PR #10. The branch is being kept synchronized with `feature/m1-simulation-core`. The latest persistence/recovery slice is at `aec8fdfe`; it has not yet received a fresh CI run because the repository workflows currently target `main` PR/push events rather than this feature-to-feature PR.

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
- deterministic xUnit tests across ground conflict, air conflict, mission families, request lifecycle, authorization, theater generation and SQLite campaign recovery.

## Deliberately abstract

This is a career/game simulation, not a real weapon-performance model.

Mission effects use normalized game-quality inputs and authored mission windows rather than real weapon envelopes, current operational procedures or classified data. Conflict theaters are fictional/abstracted. Current real-world wars are not authoritative gameplay state.

## Still open

- automatic runtime bootstrap that loads the active campaign checkpoint when the Military/Government feature becomes a production application flow,
- database-consistent backup/restore coverage for conflict checkpoints,
- richer named faction/campaign state beyond Friendly/Hostile/Neutral,
- long-term theater objectives and campaign evolution,
- player career onboarding/persistence for military affiliation and qualifications,
- authoritative aircraft assignment issuance through the fleet/dispatch system,
- authoritative job/economy settlement integration,
- production Military/Government/Conflict UI,
- large deterministic balance/stress scenarios,
- post-sync Linux/Windows verification,
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
