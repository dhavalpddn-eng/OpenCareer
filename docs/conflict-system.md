# Conflict simulation foundation

Status: first deterministic implementation on `feature/military-conflict-system`, draft PR #10. The PR is mergeable. GitHub Actions currently fails before runner steps begin, so compilation/test execution remains pending rather than reported as a code failure.

## Boundary

Microsoft Flight Simulator 2024 is only the flight environment.

```text
MSFS / SimConnect telemetry
    -> normalized AircraftTelemetrySnapshot
    -> OpenCareer mission telemetry evaluator
    -> OpenCareer conflict action/threat resolvers
    -> deterministic conflict world state
```

OpenCareer does not assume native MSFS weapons, targets, hit events or aircraft combat damage.

## Implemented in this slice

- versioned deterministic `ConflictWorldState`,
- friendly/hostile/neutral sides,
- ground units with role, strength, readiness and pressure,
- geographic sectors with friendly control and intelligence confidence,
- simulated air-threat envelopes linked to ground air-defense units,
- deterministic ground-pressure/control advancement,
- automatic support-request generation when friendly units are under pressure,
- close-air-support/suppression request selection,
- urgency and expiration,
- CAS mission accept/ingress/on-station/action-authorized/egress/objective-complete lifecycle,
- mission-authored action windows evaluated from normalized player telemetry,
- pause/slew/on-ground rejection,
- virtual precision/suppression/recon effects,
- action replay/idempotency protection,
- sector-control/intelligence feedback from player actions,
- linked air-defense threat degradation after suppression,
- deterministic simulated threat resolution against a separate OpenCareer player-damage state,
- no mutation of MSFS aircraft state,
- deterministic xUnit tests added for support generation, idempotency, CAS lifecycle, recon, suppression, threat exposure/damage and sector movement.

## Deliberately abstract

This is a game simulation, not a weapon-performance model.

The action resolver uses normalized game-quality inputs rather than real weapon envelopes or classified/operational parameters. Mission profiles own their action windows. Later aircraft/mission authoring may provide different profiles, but the simulator telemetry adapter does not invent combat capability.

## Still open

- persistent conflict checkpoints,
- theater/faction campaign generation,
- friendly/enemy simulated air units,
- recon mission lifecycle beyond the action resolver,
- logistics/transport support lifecycle,
- patrol/intercept/escort lifecycle,
- dedicated suppression/SEAD mission lifecycle,
- conflict qualification/onboarding,
- production Military/Government UI wiring,
- career/economy consequences and settlement integration,
- balancing with large deterministic scenario batches.

## Invariants

- Same state + same time/action/event IDs => same result.
- Processed player actions and threat engagements are idempotent.
- A support request exists because simulated battlefield state creates a need.
- Mission objective completion is not career/job settlement.
- Pause/slew cannot advance an attack objective.
- Simulated damage remains OpenCareer state unless a separately verified simulator mechanism is explicitly implemented.
- Current real-world wars are not authoritative gameplay state.
