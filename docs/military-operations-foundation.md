# Military / Government foundation

Updated: 2026-09-17.

Read `AGENTS.md` and `docs/project-state.md` first. This file is the compact handoff for Military / Government development.

## Branch and verification

- Branch: `feature/military-operations-foundation`
- Parent: `feature/runnable-integration` at `96427ae3eb33278a7c4cf2603e53138472f1eaf2`
- Current implementation head before this documentation update: `91d64533f16ef66acdd162118b306b21d22e94ec`
- Linux CI run 35302265454: build succeeded with 0 warnings/errors, **152/152 xUnit tests passed**, **29/29 deterministic SimLab scenarios passed**.
- Windows CI run 35302265455: WinUI x64 Release build succeeded with 0 warnings/errors, live SimConnect probe build succeeded, **152/152 xUnit tests passed**.
- These results verify compilation and deterministic/mocked behavior only. Live MSFS validation is still separate.

## Checklist status

The original Military / Government checklist items now have these foundation states:

- ✅ Conflict UI design — complete.
- ✅ Simulated combat architecture — implemented/tested as an abstract deterministic engagement resolver.
- ✅ Air support mission flow — implemented/tested as an evidence-driven mission state machine.
- ✅ Threat simulation — implemented/tested with abstract spatial/altitude/time threat zones and bounded exposure.
- ✅ Military operations screen — implemented in WinUI and compiled on Windows CI.

These green checks mean the **foundation item itself exists and is tested**, not that the entire military career loop is finished.

## Architecture

MSFS remains only the aircraft/flight telemetry environment.

```text
MSFS normalized telemetry
    + OpenCareer military authorization
    + persistent conflict/airfield state
    + accepted MilitaryOperationPlan
    -> MilitaryMissionEngine
    -> MilitaryThreatEvaluator
    -> SimulatedCombatEngine when the operation requires a virtual engagement
    -> mission outcome / career / conflict effects
    -> ViewModel
    -> WinUI
```

No native MSFS weapon, target, hit or damage events are assumed.

## Implemented domain

### Authorization and operation requirements

Namespace: `OpenCareer.Domain.Military`.

`MilitaryQualification`:
- ServiceAuthorization
- Mobility
- FastJet
- Surveillance
- Tanker
- Aeromedical
- SearchAndRescue
- Instructor

`MilitaryOperationKind`:
- Training
- Readiness
- Patrol
- Surveillance
- Transport
- AeromedicalEvacuation
- SearchAndRescue
- TankerSupport
- Escort
- Intercept
- AirfieldReinforcement
- AirSupport

`MilitaryOperationAccessPolicy` requires:
- military aircraft access;
- the operation's military qualifications;
- minimum service trust;
- capability-based aircraft eligibility.

No aircraft-name whitelist is used.

### Mission state machine

`MilitaryMissionEngine` uses:
Briefed -> Accepted -> Preflight -> EnRoute -> OnStation -> Objective -> Egress -> Recovery -> Complete.

Terminal alternate states:
- Aborted
- Failed

Important rules:
- eligibility must be verified before acceptance;
- stable origin telemetry is required before Preflight;
- airborne evidence moves the mission into EnRoute;
- objective-area dwell and any required objective action must be satisfied;
- landing by itself does not complete a mission;
- recovery-airfield landing moves to Recovery;
- parked-and-secured evidence is required for Complete.

### Threat simulation

`SimulatedThreatZone` is intentionally abstract:
- category;
- position;
- radius;
- severity;
- confidence;
- active time;
- optional altitude band.

Categories:
- AirspaceHazard
- AirborneOpposition
- GroundBasedOpposition
- ElectronicInterference
- NavigationDisruption

Pressure is spatially attenuated and overlapping threats combine using:

`1 - product(1 - individualPressure)`

so total pressure remains in 0..1.

Mission-risk summary uses duration-weighted mean exposure plus peak exposure. Threat data contains no real missile ranges, seeker models, weapon probabilities or classified/real tactical performance.

### Simulated engagement resolver

`SimulatedCombatEngine` is a deterministic gameplay resolver driven by:
- mission execution quality;
- abstract threat exposure;
- aircraft readiness;
- support factor;
- career seed + engagement key.

Outputs:
- NoContact
- ObjectiveAchieved
- ObjectivePartiallyAchieved
- MissionDisrupted
- AbortRecommended

Aircraft stress is bounded to 0..0.10 per resolved engagement. There is no flat catastrophic RNG.

Wolfram sanity checks of the formulas:
- three independent abstract pressures 0.2 / 0.4 / 0.6 combine to 0.808 and remain bounded;
- favorable illustrative inputs produce pre-noise objective effectiveness ~0.84;
- difficult illustrative inputs produce pre-noise effectiveness ~0.073 and disruption ~0.613;
- the current risk index example with mean 0.17 and peak 0.8 equals 0.3905.

These are game-calibration values, not claims about real military effectiveness.

## WinUI

New production page:
- `src/OpenCareer.App/Views/MilitaryGovernmentPage.xaml`
- `MilitaryGovernmentPage.xaml.cs`
- `ViewModels/MilitaryOperationsViewModel.cs`

The NavigationView's Military / Government destination now opens a real page instead of `PlaceholderPage`.

Current screen truthfully shows foundation readiness only:
- authorization not configured;
- no active operation;
- conflict simulation ready;
- threat simulation ready;
- mission flow ready;
- engagement resolver ready;
- mission families;
- explicit MSFS/OpenCareer simulation boundary.

It deliberately does not fabricate current wars, enemy units, missions, authorizations or player progress.

## Figma

File: `OpenCareer Conflict Operations Test UI`.
A new `Military Operations` page was created with:
- authorization card;
- active-operation card;
- four system-readiness cards;
- mission-family chips;
- simulation-boundary panel.

The file's available font list did not expose Segoe UI, so this Figma page currently falls back to Inter. WinUI uses the normal Windows system typography. Treat the Figma page as a product/layout reference rather than a pixel-perfect typography source until that font mismatch is resolved.

## External tooling status

- ForeFlight connector currently requires an active paid ForeFlight license; OpenCareer does not depend on it.
- No Supabase project is connected; do not create cloud persistence for this offline-first subsystem merely to use the connector.
- No Vercel team/project is connected; Vercel is not part of the WinUI runtime.
- Kling CLI 0.2.0 invocation timed out in the current execution environment; no paid generation was submitted.
- Notion may mirror the plan/checklist, but GitHub Markdown remains canonical.

## Next bounded military work

1. Persist a real career `MilitaryAuthorizationProfile` and military qualification history.
2. Convert eligible military `JobMarketOfferDraft` / `JobContract` records into validated `MilitaryOperationPlan` instances.
3. Add an application-level `MilitaryOperationCoordinator` that consumes normalized flight evidence without putting mission logic in the UI.
4. Persist active military mission progress/recovery in SQLite.
5. Connect `AirfieldOperationGuard` and conflict/airfield state to mission destination eligibility.
6. Feed threat/conflict transition summaries into the persistent world feed.
7. Bind real coordinator state into `MilitaryOperationsViewModel` and add the compact Current Flight military strip.
8. Implement the operational map from authoritative in-save mission/threat data.
9. Settle military mission compensation/reputation exactly once through the future authoritative ledger.
10. Live-playtest with MSFS only after the shared SimConnect live-validation gate is passed.

## Product questions still open

- Does the player begin military play already qualified, or complete a training/qualification pipeline?
- Can one save move freely between civilian and military/reserve careers, or does military service require a formal transition?
- Should escort/intercept/air-support scoring include formation/proximity geometry in addition to route/altitude/timing?
- For virtual combat actions, should the player trigger an explicit OpenCareer action at the correct geometry, or should qualifying attack/support runs resolve automatically?
- How severe may simulated military damage/failure consequences become: repair cost/downtime/reputation only, or stronger career consequences?
