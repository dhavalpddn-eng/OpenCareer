# Conflict simulation foundation

Status: active implementation on `feature/military-conflict-system`, draft PR #10. Code head `4b0472b4` is green: Linux passed 352/352 xUnit + 29/29 SimLab; Windows built WinUI and the live probe at 0 errors and passed 352/352 xUnit.

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
- deterministic faction operational posture persisted with faction identity: Defensive, Aggressive, LogisticsFocused or AirFocused,
- faction posture subtly changes campaign behavior without introducing nondeterministic AI authority: aggressive factions request battlefield support earlier and favor maneuver replacements, logistics-focused factions request resupply earlier and prioritize sustainment roles, air-focused factions widen intercept/escort coverage and favor air-defense support, while defensive posture preserves the baseline behavior,
- both friendly and hostile posture influence replacement priority; the friendly faction posture also shapes the player-facing support-request mix and urgency,
- bounded long-term campaign cycles that recover nearby ground units through same-side logistics support, recover simulated air-unit readiness from theater logistics, consolidate sector control from persistent campaign momentum, and resynchronize linked threat severity after recovery,
- finite friendly/hostile replacement reserves that require operational logistics, reinforce the weakest eligible surviving ground units first, and are consumed rather than providing unlimited regeneration,
- persisted campaign terminal outcomes: Ongoing, Victory, Defeat, Stalemate and Ceasefire; victory/defeat require sustained secured evaluations, ceasefire can result from mutual exhaustion, and prolonged balanced low-momentum campaigns can end in stalemate,
- telemetry-only mission progress synchronization updates campaign timestamps/state without falsely incrementing strategic evaluation counters,
- terminal campaigns refuse new military mission acceptance,
- terminal campaigns can transition into a fresh successor campaign while preserving military career state and simulated player damage rather than silently repairing the player,
- completed operation summaries persist in checkpoint history with operation/faction identity, theater, terminal outcome, final phase/control, evaluation count and end time; history round-trips through SQLite and is exposed to the future Military/Government UI,
- deterministic successor-offer planning selects from eligible fictional theaters, avoids immediately repeating the current theater when alternatives exist, derives a stable successor seed/campaign ID and previews the same operation identity that acceptance will create,
- application-layer `MilitaryCampaignTransitionService` exposes the current successor offer, accept/decline/reconsider decisions, stale-offer protection and atomic runtime replacement after acceptance; ViewModels do not need theater seeds or campaign-generation logic,
- default successor theaters are abstract fictional operation regions and are supplied through an `IConflictTheaterCatalog` boundary so future world/career sources can replace the built-in catalog without changing transition rules,
- `ConflictCampaignCoordinator` that creates, advances and revision-saves world + strategic state together,
- `MilitaryDispatchService` that exposes eligibility per support request and refuses acceptance when affiliation, qualification, aircraft assignment, capability/access or damage-state rules fail,
- `MilitaryCampaignMissionService` that persists acceptance, mission-stage progress, failure/completion lifecycle and simulated threat outcomes without allowing duplicate replay after recovery,
- `ConflictOperationsSnapshotBuilder` that projects operation/faction identity and posture, campaign outcome, replacement reserves, campaign/front/unit/threat/support/strategic-objective/trust/damage/active-operation state for the Military/Government UI,
- first production WinUI Military/Government screen replacing the placeholder: active operation, faction names/postures, phase/outcome, control/momentum, replacement reserves, trust, OpenCareer damage, front/threat summary, support requests and strategic objectives are projected through `MilitaryGovernmentViewModel` without moving campaign logic into the UI,
- successor-operation offer presentation with Accept / Decline for now / Reconsider actions routed through `MilitaryCampaignTransitionService`, including stale-offer protection already enforced by the application layer,
- page-scoped refresh while the Military/Government screen is visible, with the view reading immutable application snapshots instead of simulator/database objects directly,
- shared SQLite schema v3 reconciliation after synchronizing FlightSession and military persistence: databases previously stamped v2 by either parallel branch idempotently gain the missing flight-session or conflict-campaign table before advancing to v3,
- deterministic xUnit tests across ground conflict, air conflict, mission families, request lifecycle, authorization, theater generation, SQLite campaign recovery, strategic evolution, dispatch authorization and the operations snapshot.

## Deliberately abstract

This is a career/game simulation, not a real weapon-performance model.

Mission effects use normalized game-quality inputs and authored mission windows rather than real weapon envelopes, current operational procedures or classified data. Conflict theaters are fictional/abstracted. Current real-world wars are not authoritative gameplay state.

## Still open

- user-facing Settings backup selection/confirmation for staging a restore archive; the validated restore backend and restart-time apply path are implemented,
- dynamic faction-posture evolution across campaign phases/outcomes; the current posture is deterministic and fixed for each operation,
- player career onboarding/persistence for military affiliation and qualifications,
- authoritative aircraft assignment issuance through the fleet/dispatch system,
- authoritative job/economy settlement integration,
- local visual/accessibility acceptance and richer Conflict Operations UI detail beyond the first production screen (operational map/comms/history drill-down),
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
