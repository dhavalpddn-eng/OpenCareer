# OpenCareer intro tutorial

Status: accepted onboarding/product tutorial specification.

Purpose: teach a new player what OpenCareer does, how the career loop works, what every major screen is for, and how OpenCareer interacts with Microsoft Flight Simulator 2024 without pretending unfinished or unavailable systems are usable.

The tutorial is a product feature, not a developer walkthrough. It must remain useful with MSFS closed.

## Core behavior

The production tutorial is a versioned first-run tour.

- Show automatically for a brand-new OpenCareer profile after the initial profile/home-base setup flow exists.
- Allow **Back**, **Next**, **Skip tutorial**, and **Finish**.
- Never trap the user in the tutorial.
- Persist completion by tutorial version so major product changes can introduce only the new/revised steps later.
- Provide **Restart intro tutorial** from Settings.
- Allow context help from major screens to reopen the relevant tutorial section later.
- The tutorial may navigate between screens, but it must not create missions, spend money, alter career state, claim aircraft ownership, change qualifications, or settle rewards.
- Tutorial state is UI/application state only. It is never authoritative career state.
- Do not require MSFS to be running.
- If MSFS is connected, the tutorial may point to real connection/telemetry state but must not create a FlightSession from telemetry alone.

## Feature-readiness rule

The tutorial must distinguish three states:

1. **Available** — the function is implemented and usable.
2. **Unavailable right now** — the function exists but a dependency is missing, such as MSFS disconnected, no compatible aircraft data, no active flight, or no qualifying jobs.
3. **Coming later** — the production surface is not implemented in the current build.

Never present a placeholder as a working feature. A tutorial step for a planned feature may explain its role, but the primary action must be disabled or labeled **Coming later** until its backing system exists.

A screen becoming implemented should not require rewriting the tutorial architecture. Each tutorial step therefore declares a feature key/readiness state and optional navigation target.

## Visual behavior

Use the existing OpenCareer visual system.

- Dark navy/slate shell.
- Aviation blue for tutorial focus and normal actions.
- Green only for confirmed/healthy state.
- Amber for dependency/caution.
- Red only for danger/failure/conflict.
- Never rely on color alone.
- Use a dimmed overlay plus a focused tutorial card when highlighting an existing control or region.
- Keep the focused application control readable.
- Do not place the tutorial card over the control it is explaining when another position is available.
- Keyboard focus must stay inside the tutorial card while the overlay is active.
- Escape may close the tour only after a confirmation-free "Skip tutorial" equivalent; it must not perform destructive actions.
- Support text scaling and a vertically scrollable tutorial card.

Reference desktop card width: 480-620 px. On compact windows, use a centered/bottom sheet style rather than forcing a tiny spotlight.

## Tutorial data model

Recommended application/UI model:

```csharp
public enum TutorialFeatureState
{
    Available,
    Unavailable,
    ComingLater
}

public sealed record TutorialStep(
    string Id,
    int Version,
    string Title,
    string Body,
    string? NavigationTag,
    string? FocusElementKey,
    string FeatureKey,
    bool IsOptional = false);
```

The readiness state is resolved separately from the static copy. Do not hard-code "Coming later" into a step whose feature will eventually ship.

Recommended boundary:

```text
Tutorial catalog
    + feature-readiness provider
    + tutorial progress store
    -> tutorial coordinator
    -> tutorial ViewModel
    -> WinUI overlay
```

The tutorial coordinator may request navigation through an application navigation abstraction. It must not contain SimConnect, database, mission, economy, or settlement logic.

## Full first-run sequence

### 1. Welcome to OpenCareer

**Title:** Welcome to OpenCareer

Teach:

- OpenCareer is a single-player career, operations, economy and progression companion for MSFS 2024.
- MSFS flies the aircraft; OpenCareer owns career state, missions, jobs, finances, aircraft access/ownership, company systems and persistent progression.
- Core career gameplay is offline-first.
- A standard career starts from scratch. An installed aircraft is not automatically owned.
- Employer, rented, assigned and owned aircraft are distinct.

Primary action: **Start tour**.

### 2. Simulator connection

Navigation target: Dashboard / shell header.

Teach the persistent connection indicator:

- **Waiting for MSFS** — app is healthy; simulator is not available yet.
- **Connecting/Reconnecting** — OpenCareer is trying to restore the simulator link.
- **Connected** — the SimConnect boundary is active.
- Telemetry can exist without an active career flight.
- A disconnect suspends/recovery logic; it must not automatically erase a valid active session.

When disconnected, explicitly state that most management screens remain usable.

### 3. Dashboard

Navigation target: `dashboard`.

Teach:

- High-level career/operation status.
- Current simulator connection.
- Current aircraft identity/access when known.
- Home base/location.
- Active job/flight summary.
- Important alerts and next action.
- The Dashboard answers "What should I do next?" rather than exposing raw telemetry.

Current development note: Dashboard is implemented as a real page, but several career cards remain foundational/placeholder until their backing systems ship.

### 4. The normal OpenCareer loop

This is the most important conceptual step.

Teach the normal loop:

```text
Find suitable work
-> check aircraft / qualifications / route feasibility
-> accept
-> prepare / service / load
-> fly in MSFS
-> OpenCareer tracks evidence
-> land
-> taxi / park / shutdown as required
-> validate objectives
-> settle money / reputation / location once
-> write the logbook
```

Critical rules:

- A landing alone does not complete a mission.
- Conventional jobs normally require destination parking and shutdown.
- Diversions and go-arounds are evaluated by context and are not automatic failures.
- OpenCareer prevents duplicate settlement after recovery/reload.

### 5. Dispatch

Navigation target: `dispatch`.

Purpose: build/validate the operation before accepting or launching it.

Teach:

- Selected aircraft/access type.
- Payload and fuel.
- Origin/destination.
- runway and aircraft feasibility.
- route/weather/environment inputs where authoritative data exists.
- mission constraints.
- explicit reasons when a dispatch is not feasible.

Do not imply an impossible job can be overridden just because it appears attractive.

Feature state today: Coming later; capability/runway dispatch is planned after flight-session recovery.

### 6. Jobs

Navigation target: `jobs`.

Teach:

- Jobs are generated from qualifications, location, aircraft access/capability, market state and mission rules.
- Work can include passenger, cargo, utility, emergency, government and later military families when qualified.
- Employment remains viable without owning a company or aircraft.
- Named cargo has physical/economic identity rather than being anonymous weight.
- Accepting a job creates commitments; browsing does not.

Feature state today: Coming later; contract/economy foundations exist but the end-to-end playable job loop is not wired yet.

### 7. Current Flight

Navigation target: `current-flight`.

Purpose: the in-flight workspace.

Teach four internal areas when implemented:

- **Checklist** — contextual preflight/start/taxi/takeoff/cruise/approach/landing/taxi-in/shutdown requirements. The player can enable **Show checklist every flight** so it appears for every operation.
- **Flight** — current state, route/map and essential telemetry.
- **Mission** — objectives, passenger/cargo status and constraints.
- **Events** — takeoff, landing, go-around, rejected takeoff, abnormal and recovery events.

Every actionable checklist row shows its controller and keyboard instructions inline. There is no separate keybind list.

```text
[ ] Parking brake — Controller: [resolved binding] | Keyboard: [resolved binding]
[ ] Landing lights — Controller: [resolved binding] | Keyboard: [resolved binding]
[ ] Verify fuel — Controller: No input required | Keyboard: No input required
[ ] Set cockpit-only selector — Controller: Cockpit only | Keyboard: Cockpit only
[ ] Unmapped action — Controller: UNBOUND | Keyboard: UNBOUND
```

Controller status presentation:

- **Resolved binding** — normal text plus the actual controller or keyboard binding/chord.
- **UNBOUND** — always visible; never blank.
- **UNBOUND** uses warning/amber/orange treatment by default.
- **UNBOUND — REQUIRED** uses danger/red treatment when the missing mapping blocks a required or safety-critical checklist action.
- **Cockpit only** — neutral informational state when no controller action is appropriate.
- **Binding unavailable** — visible warning/amber state when OpenCareer cannot reliably resolve the player's configured binding.
- Color is never the only signal; the text label/icon remains visible.

Bindings are profile/device/aircraft sensitive. OpenCareer must not invent a controller or keyboard binding. It shows a verified configured binding only when a supported source or explicit user mapping supplies it.

Persistent header teaches:

- flight/operation state,
- aircraft,
- origin/destination,
- fuel/payload,
- connection/recovery status,
- next required action.

Important explanation:

- OpenCareer samples simulator data and converts it into normalized evidence.
- It does not mark a takeoff or landing from one noisy value.
- Telemetry alone does not create an active FlightSession.
- Career-credit time does not multiply when simulation rate is accelerated.

Current development note: the real Current Flight page already displays normalized live telemetry. Authoritative flight-session orchestration is still under development.

### 8. Map / World

Navigation target: `world`.

Teach:

- persistent geographic world,
- airports/bases,
- player/company locations,
- routes/relationships,
- world events,
- market overlays,
- authorized government/military overlays.

Always distinguish real geographic data from OpenCareer simulated market/security/world state.

Feature state today: Coming later.

### 9. Aircraft

Navigation target: `aircraft`.

Teach:

- installed aircraft registry,
- aircraft identity/family/type,
- capability profile,
- performance source/confidence,
- hours/experience,
- current location,
- maintenance/finance/insurance restrictions,
- access type.

Access types:

- **Installed only** — detected in MSFS but not available for career use.
- **Employer** — employer supplies aircraft under job rules.
- **Rented** — temporary paid access.
- **Assigned** — government/military or special-role access.
- **Owned** — player/company asset.

An installed F-22 or other aircraft never becomes owned merely because MSFS can load it.

Feature state today: Coming later; capability direction is designed but registry implementation is pending.

### 10. Hangar / Bases

Navigation target: `bases`.

Teach:

- home base,
- additional bases,
- storage/hangar capacity,
- aircraft physical location,
- local services,
- local relationships,
- relocation and expansion.

Core rule: the company and its aircraft do not teleport around the world. Geography matters.

Feature state today: Coming later.

### 11. Maintenance

Navigation target: `maintenance`.

Teach:

- overall condition,
- gradual wear,
- discrete damage/incidents,
- due/overdue maintenance,
- MRO/parts availability,
- downtime and cost,
- history.

OpenCareer must not invent component precision when a simulator/add-on does not expose trustworthy data.

Feature state today: Coming later.

### 12. Company

Navigation target: `company`.

Teach:

- operating footprint,
- active contracts,
- employees/crew when implemented,
- relationships,
- utilization,
- automated management,
- growth.

The career remains playable as an employee; building a large company is optional.

Feature state today: Coming later.

### 13. Finances

Navigation target: `finances`.

Teach:

- cash/liquidity,
- income and expenses,
- loans/payment schedules,
- aircraft financing,
- insurance,
- maintenance reserve,
- base/hangar costs,
- transaction ledger,
- credit/lender offers.

Credit is derived from career evidence such as reliability, safety, reputation, income/cash flow and debt service. It is not a simple arbitrary level gate.

A quote is not a transaction. Purchases, loans and settlement become authoritative only through atomic persisted operations.

Feature state today: Coming later; deterministic credit/dealer foundations exist but are not production-settled.

### 14. Markets

Navigation target: `markets`.

Teach:

- airport/region demand and supply,
- commodities,
- scarcity/surplus,
- fuel/service pressure,
- route opportunities,
- event modifiers,
- in-game trends.

OpenCareer-generated market numbers are in-game simulation values unless explicitly labeled as external real data.

Feature state today: Coming later; deterministic market-state foundations already exist.

### 15. Military / Government

Navigation target: `military`.

Teach:

- access is earned separately from civilian ownership,
- qualifications/authorization,
- assigned aircraft,
- training/readiness,
- patrol/surveillance/logistics/intercept/escort when valid,
- later simulated conflict operations.

Combat/conflict rule:

- MSFS supplies aircraft flight telemetry.
- OpenCareer simulates mission objectives, threats, effects, damage consequences and conflict state.
- Do not imply native MSFS weapons/targets/damage APIs exist.
- Conflict scenarios remain fictionalized/abstracted rather than turning current real tragedies into gameplay.

Feature state today: Coming later / active design.

### 16. Logbook

Navigation target: `logbook`.

Teach:

- trustworthy FlightSession -> FlightLeg history,
- route track,
- movement/airborne/taxi/simulated/career-credit time,
- takeoffs/landings/touch-and-go/bounce summary,
- fuel,
- landing metrics/confidence,
- incidents,
- mission outcome,
- settlement,
- assistance flags.

Free/practice flights may be loggable while remaining distinct from mission work.

Feature state today: Coming later; flight/time domain foundations exist but durable production persistence is pending.

### 17. Career

Navigation target: `career`.

Teach progression through:

- licenses/ratings,
- total/recent experience,
- category/family/type experience,
- reputation,
- employer/customer relationships,
- safety/reliability,
- government/military qualifications,
- milestones.

Do not teach XP grinding as the primary progression system. Any future meta-level never bypasses licenses, authorization, aircraft capability, affordability, dispatch or relationship requirements.

Feature state today: Coming later.

### 18. Settings

Navigation target: `settings`.

Teach:

- SimConnect/runtime status,
- reconnect/diagnostics,
- UI preferences,
- gameplay/units/notifications,
- save/backup/recovery status,
- logs/build information,
- optional online/AI services,
- **Show checklist every flight**,
- **Show controller/keyboard bindings on each checklist step**,
- **Restart intro tutorial**.

Dangerous reset/destructive actions require separate confirmation and are never bundled into the tutorial.

Feature state today: placeholder; settings implementation remains pending.

### 19. Your first operation

This step teaches behavior without generating a fake mission.

When the playable loop exists, show the checklist:

1. Confirm OpenCareer is connected or management-only operation is available.
2. Select a feasible job.
3. Review aircraft access and qualifications.
4. Review dispatch, runway, payload and fuel feasibility.
5. Accept the job.
6. Load the assigned/rented/owned aircraft in MSFS as required.
7. Follow preparation/loading/start requirements.
8. Taxi and fly normally.
9. Follow mission objectives.
10. After landing, taxi to the required destination parking.
11. Complete unloading/servicing/shutdown requirements.
12. Review the debrief.
13. Confirm the logbook and one-time settlement.

Until the playable loop ships, this step is instructional only and must say **Career mission flow is not enabled in this build**.

### 20. Finish

**Title:** You know where everything lives

Show a concise summary:

- Dashboard: next action.
- Dispatch/Jobs: find and validate work.
- Current Flight: fly and satisfy the operation.
- Aircraft/Bases/Maintenance: manage physical capability.
- Company/Finances/Markets: manage the business.
- Military/Government: authorization and special operations.
- Logbook/Career: history and progression.
- Settings: integration, recovery and preferences.

Finish returns to Dashboard.

## Contextual mini-tutorials

The full first-run tour should later be supplemented with short one-time contextual tutorials:

- First simulator connection.
- First accepted job.
- First dispatch failure explanation.
- First active flight.
- First disconnect/recovery event.
- First landing/debrief.
- First aircraft purchase/loan quote.
- First maintenance event.
- First company base expansion.
- First government/military authorization.
- First conflict operation.

Each contextual tutorial is independently versioned and dismissible.

## Persistence

Until authoritative profile persistence is available, do not create a second ad-hoc gameplay database just for onboarding.

When UI preference persistence is implemented, store at minimum:

- latest completed full-tour version,
- current/last step when resumable,
- skipped version,
- completed contextual tutorial IDs/versions.

Resetting tutorial progress must not reset career state.

## Analytics/privacy

The tutorial must work fully offline. No telemetry/analytics service is required to determine completion. If product analytics are ever added, tutorial usage must not become a gameplay dependency.

## Implementation order

1. Keep this document as the canonical tutorial copy/behavior specification.
2. Add tutorial catalog and readiness interfaces.
3. Add WinUI tutorial overlay/card with Back/Next/Skip.
4. Add navigation coordination without putting navigation logic in tutorial copy.
5. Add preference persistence/versioning.
6. Add Settings "Restart intro tutorial".
7. Bind currently implemented Dashboard/Current Flight focus targets.
8. Keep unfinished destinations explicitly Coming later.
9. As each subsystem ships, change readiness mapping and add real focus targets; do not duplicate feature logic inside the tutorial.
10. Add xUnit tests for ordering/versioning/readiness and UI-level smoke tests for navigation/skip/restart.

## Acceptance criteria

- A first-time user can explain the normal OpenCareer career loop after completing the tour.
- All 15 canonical navigation destinations are explained.
- The tutorial never fabricates a balance, job, aircraft ownership, mission, qualification, market value, conflict state or completed feature.
- MSFS may be closed for the entire tutorial.
- The user can skip and later restart the tutorial.
- Tutorial completion survives normal application restart after preference persistence is implemented.
- A future shipped feature can move from ComingLater to Available through readiness configuration without rewriting the tutorial system.
- The tutorial never creates or settles authoritative career state.
