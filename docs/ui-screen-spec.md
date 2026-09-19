# OpenCareer screen specification

Status: approved target UX for the current 15-item NavigationView shell. This document defines what each destination should become as the underlying systems are implemented.

It is intentionally more detailed than the present Chapter 2 UI. Do not fabricate data to make the app look complete.

Visual rules: see [UI design system](ui-design-system.md).
At-a-glance layouts: see [UI screen atlas](assets/opencareer-ui-screen-atlas.svg).
Conflict reference: see [Conflict Operations reference](assets/opencareer-conflict-operations.svg).

## 1. Dashboard

Purpose: act as the player's **dynamic career operations desk** and answer “what matters right now, what is ready, and where should I go next?”

Detailed accepted behavior: see `home-dashboard.md`.

### Layout

The production Dashboard uses a split hero rather than a single static welcome card:

- left hero: current/relevant aircraft, simulator state, player location, aircraft location, distance to aircraft and readiness for work,
- right hero: career status plus current employer/company status when applicable.

Persistent/major modules:

- header financial strip:
  - current cash,
  - today's net P/L with explicit positive/negative sign,
- dynamic **OpenCareer Recommends** action:
  - chooses the highest-priority actionable next step,
  - links directly to the relevant top-level destination,
- **Top Opportunities**:
  - exactly four highest-ranked **available** jobs,
  - Green **Standard**,
  - Blue **Specialist**,
  - Purple **Elite**,
  - Orange/Gold **Legendary**,
  - tier text/icon must accompany color,
  - eligibility first, then tier, personalized fit, economics/reposition tie-breaks,
- career card:
  - Career Level,
  - XP toward next level,
  - licenses/ratings,
  - total/relevant flight hours,
  - owned-aircraft count,
  - next meaningful milestone,
  - most recent meaningful achievement/activity,
- company card when employed:
  - employer,
  - company rank,
  - company standing,
  - employment state,
  - warnings/probation/suspension/termination information,
- financial-health card:
  - cash,
  - today's net,
  - upcoming obligations,
- aircraft-readiness card:
  - ready/not ready/unknown,
  - blocker/maintenance summary,
  - current aircraft location,
  - player location,
  - distance between player and aircraft,
- recent activity stream,
- condensed World / Map module:
  - player/home/fleet geography,
  - jobs/routes,
  - markets/events,
  - government/military signals when authorized,
- searchable **OpenCareer Network** in-world social feed.

### Dynamic guidance

The Dashboard is not a static summary. It can direct the player to the correct screen.

Examples:

- active operation -> Current Flight,
- accepted job requiring preparation -> Dispatch,
- aircraft blocked by maintenance -> Maintenance,
- no accepted work + strong opportunities -> Jobs,
- company probation/suspension/review -> Company,
- financial issue requiring action -> Finances,
- qualification/career gate -> Career.

Critical operational blockers outrank routine recommendations. The guidance engine is deterministic and testable.

### Opportunity tier rules

The four Dashboard jobs are not merely the highest raw payout.

Only jobs the player is currently eligible to accept enter the Dashboard Top Opportunities list. Ranking is:

1. availability/eligibility,
2. opportunity tier,
3. personalized fit,
4. estimated net value and reposition cost/distance as tie-breakers.

Tier communicates scarcity/prestige/opportunity quality and can reflect requirements, urgency, employer value, world events and career relevance. It does not bypass dispatch feasibility.

Locked jobs can still appear in the full Jobs screen with explanations; they do not displace an actually available Top Opportunity on Home.

### Career Level + XP

Career Level and XP are now accepted as visible meta-progression.

They may summarize broad career growth, unlock presentation/prestige/convenience or help pace content, but **never bypass**:

- licenses/ratings,
- aircraft capability,
- company/employer standing,
- government/military authorization,
- affordability,
- dispatch/safety requirements.

The career must not become XP-grind dependent.

### Company employment and firing

Employment is a real relationship, not permanent access after hiring.

Company employment can include:

- active,
- probation,
- suspended,
- terminated/fired.

Rank and standing can rise or fall. Company-specific deterministic policy may react to repeated failures, no-shows, preventable damage, serious safety/reliability problems and other actual employment-performance signals.

Do not fire or heavily punish the player for one ordinary rough landing, a legitimate go-around/diversion, a simulator disconnect, or another defensible safety decision. Termination must not permanently brick the career; other employers/employee work remain recovery paths.

### OpenCareer Network

The social feed is primarily a **live in-world simulated network**, not scraped real social media.

It can contain posts/reactions from:

- passengers,
- employers/companies,
- airports/bases,
- local organizations,
- customers,
- simulated aviation/world entities.

It is searchable by airport, city/region, company and text/topic. Structured world/player events remain authoritative; optional AI may render flavor text but cannot invent or modify money, mission outcomes, reputation changes or other critical state.

If optional real-world public content is added later, it must be opt-in where appropriate and visually/source-labeled separately from OpenCareer simulated posts.

### Rules

- Telemetry alone must never create an active FlightSession.
- No hard-coded developer fixture may be presented as the player's production home base.
- Home remains useful with MSFS closed.
- Employer/rented/assigned/owned aircraft remain distinct.
- Do not fabricate career, company, aircraft, job or economy values while their authoritative systems are unavailable.
- Future layout customization is allowed as an extension point, but the initial production Dashboard uses one curated fixed layout.
- The full Jobs, Map / World, Finances, Company, Aircraft and Career screens remain the detailed workspaces; Home summarizes and routes into them.

## 2. Dispatch

Purpose: build a feasible flight before departure.

### Layout

- Main route/map workspace.
- Origin/destination/alternates.
- Aircraft selector and access type.
- Payload/passenger/cargo manifest.
- Fuel/planned endurance.
- Runway/performance feasibility.
- Weather/environment summary when an authoritative source exists.
- Cost/revenue summary.
- Validation checklist.
- Primary action: Dispatch / Prepare Operation.

### Rules

- Never offer “ready” when aircraft/runway/payload/range checks fail.
- Unknown performance confidence must be explicit.
- Dispatch uses installed-aircraft/capability data, not a small whitelist.
- A mission can be economically attractive and still be operationally invalid.

## 3. Jobs

Purpose: find and evaluate work.

### Layout

- Filter/search rail: region, duration, job family, aircraft/access, employer, government/military authorization.
- Sortable job list.
- Selected job detail.
- Eligibility block with explicit reasons.
- Pay/cost/risk.
- Route summary.
- Named cargo/passenger manifest where applicable.
- Relationship/reputation impact.
- Accept -> Dispatch flow.

### Rules

- Show why a job is locked rather than only disabling it.
- Cargo jobs show named commodity lots, not anonymous “cargo weight.”
- Pay is distinct from cargo market value.
- Military/government work must show authorization requirements.

## 4. Current Flight

Purpose: lightweight operational awareness while MSFS renders the flight.

### Normal flight layout

Treat Current Flight as the **In-Flight workspace**. It may use compact internal tabs/pivots without adding another top-level NavigationView destination:

- **Checklist** — default before departure; contextual cold-and-dark/preflight/start/taxi/takeoff/cruise/approach/landing/taxi-in/shutdown items. A user setting can keep this checklist enabled for every flight.
- **Flight** — current flight state, compact route/map and core telemetry only: position, altitude, speed, heading, vertical speed, configuration.
- **Mission** — current objectives, passenger/cargo status and mission-specific constraints.
- **Events** — recent takeoff/landing/go-around/rejected-takeoff/abnormal/recovery events.

Each checklist row includes both the controller and keyboard instruction for that exact action. Do not move input bindings into a separate reference list.

Input-binding states apply independently to **Controller** and **Keyboard**:

- resolved binding: show the actual button/key/chord inline,
- no direct input required: show **No input required**,
- cockpit-only action: show **Cockpit only**,
- mapping cannot be reliably resolved: show **Binding unavailable** in amber/orange,
- no configured mapping: show **UNBOUND** in amber/orange,
- required or safety-critical action with no mapping: show **UNBOUND — REQUIRED** in red.

Example row:

```text
Parking brake — Controller: LB + X | Keyboard: Ctrl + Num Del
```

One input device can be bound while the other is unbound. A missing controller or keyboard mapping must never render as empty whitespace. Never invent a default binding when the player's actual mapping is unknown. Text/iconography must accompany color for accessibility.

The header remains persistent across those views:

- flight state / operation lifecycle,
- aircraft and origin/destination,
- fuel/payload summary,
- connection/recovery status,
- next required action.

As phases change, emphasis may move automatically from Checklist -> Flight -> Arrival requirements, but the user can always switch views.

For the first use of a specialized mission family, Current Flight also hosts the mission-family guided tutorial. It uses the same live checklist surface but adds mission-specific instruction, recovery guidance, Controller/Keyboard bindings and auto-completion only for steps supported by trustworthy telemetry or authoritative OpenCareer mission state. Examples include banner pickup, carrier takeoff/landing, glider tow, skydiving, firefighting, survey and SAR. See `mission-tutorial-system.md`.

### Rules

- No raw telemetry flood.
- UI updates are coalesced and slower than high-rate detector sampling.
- Flight state comes from the application state machine, not a single SimVar.
- Disconnect suspends/recovery state; it does not instantly discard a session.
- Full mission completion remains mission-specific.
- The optional MSFS 2024 EFB companion mirrors a small subset of this workspace; it does not replace or become authoritative over the Windows app. See `efb-integration.md`.

### Conflict flight state

When an accepted Military/Government conflict mission is active, the page may show a compact conflict strip: current objective, threat proximity, simulated damage/effects and return-to-base status. The full tactical picture remains on Military / Government.

## 5. Map / World

Purpose: understand the persistent geographic world.

### Layout

- Large map.
- Layer controls.
- Airports/bases.
- player/company locations.
- route/network relationships.
- world events.
- market pressure overlays.
- government/military activity overlays when authorized.
- selected location detail pane.

### Rules

- Clearly distinguish real/geographic data from OpenCareer simulated state.
- Do not imply generated market or security data is current real-world intelligence.
- Layer density must be filterable.

## 6. Aircraft

Purpose: understand installed aircraft, access, capability and history.

### Layout

- Aircraft registry/list.
- Selected aircraft identity.
- Access type: Owned / Employer / Rented / Assigned / Installed only.
- Capability profile.
- performance source/confidence.
- hours/experience in type/family.
- current location.
- maintenance state.
- finance/insurance/access restrictions when applicable.

### Rules

- No static supported-aircraft whitelist.
- Unknown fields stay unknown.
- Military aircraft installation does not imply civilian ownership.
- Ownership and authorization are separate.

## 7. Hangar / Bases

Purpose: manage physical presence and geographic capability.

### Layout

- Home base overview.
- Additional bases.
- hangar/storage capacity.
- aircraft parked at each location.
- local relationships/connections.
- facility/services availability.
- relocation/expansion actions.
- map of operating footprint.

### Rules

- Company cannot teleport between bases.
- Aircraft location is persistent.
- Base benefits are local unless a system explicitly says otherwise.

## 8. Maintenance

Purpose: show airworthiness/reliability consequences and required work.

### Layout

- Fleet/aircraft selector.
- overall condition.
- component/wear list.
- discrete damage/incidents.
- due/overdue maintenance.
- next inspection/service.
- parts/MRO availability.
- estimated downtime/cost.
- maintenance history.

### Rules

- Separate gradual wear from discrete damage.
- Use MSFS-native state only where verified and supported; OpenCareer persistence/fallback remains authoritative where needed.
- Do not invent component precision unsupported by simulator/add-on data.

## 9. Company

Purpose: manage the business without turning the app into a spreadsheet simulator.

### Layout

- company summary and operating footprint.
- active contracts.
- automated management status.
- employees/crew when implemented.
- service/facility capability.
- customer/employer relationships.
- utilization / operational performance.
- growth actions.

### Rules

- Employment remains viable even if the player never builds a large company.
- A player can be demoted, placed on probation, suspended or fired according to deterministic company policy and performance history.
- Legitimate safety decisions, one ordinary rough landing or simulator disconnect must not be treated as arbitrary firing triggers.
- Termination reduces/removes access to that employer but must leave recovery paths through other employers or independent work.
- Management defaults to automation.
- Manual actions provide bounded advantages only where designed.

## 10. Finances

Purpose: make cash, obligations and risk understandable.

### Layout

- cash and near-term liquidity.
- income/expense trend.
- loans and payment schedule.
- aircraft financing.
- insurance.
- fixed operating costs.
- maintenance reserve.
- hangar/base costs.
- transaction ledger.
- credit availability / lender offers when implemented.

### Rules

- Deterministic economy state is authoritative.
- Career-derived credit can use reliability, safety, reputation, income/cash flow and debt service.
- Do not use arbitrary level gates for financing.
- Quotes are not settled transactions.

## 11. Markets

Purpose: expose the world simulation behind jobs and business opportunity.

### Layout

- region/airport selector.
- demand/supply indicators.
- commodity list.
- trend and scarcity/surplus.
- fuel/service cost pressure.
- route opportunity comparison.
- active event modifiers.
- historical in-game trend.

### Rules

- Generated values are in-game market values unless a real external data source is explicitly connected and labeled.
- Temporary shocks do not permanently rewrite structural capacity without a structural event.
- Commodity UI shows physical and economic identity.

## 12. Military / Government

Purpose: manage authorization, government work and simulated conflict operations.

### Normal layout

- authorization/qualification status.
- available government/military employers/contracts.
- assigned/employer military aircraft.
- training/readiness missions.
- patrol/surveillance/logistics/intercept/escort opportunities.
- active theaters/events.
- relationship/reputation effects.

### Active conflict layout

Use the approved Conflict Operations composition:

- left: Conflict Overview
  - theater/operation name
  - risk/threat level
  - contested/control percentages when the deterministic simulation produces them
  - active simulated threats
  - recommended operational action
- center: Operational Map
  - friendly/hostile control
  - front line
  - friendly bases
  - mission target
  - simulated SAM/air threats
  - planned route
  - player aircraft
- lower center: Flight Status + Communications
- right: Active Mission + Mission Progress

#### Air support/combat rule

MSFS 2024 is only the flight/telemetry environment. OpenCareer simulates the entire conflict layer.

```text
MSFS telemetry
    -> player position/altitude/speed/heading/configuration
    -> deterministic conflict mission evaluator
    -> simulated threats / attack-run validation / mission effects
    -> world-state update
```

There are no assumed native MSFS weapon, target, hit or damage events.

A close-air-support request can be generated because a simulated ground battle needs help. The player physically flies to the area in MSFS; OpenCareer evaluates arrival, attack geometry and mission actions, resolves virtual effects, then updates the simulated battle/front.

Any simulated aircraft damage belongs to OpenCareer career/mission state unless a separately verified simulator integration is added later.

### Political/real-world content rule

World conflict gameplay should remain fictionalized or safely abstracted. Do not turn current real wars or real tragedies into entertainment scenarios.

## 13. Logbook

Purpose: provide trustworthy flight history and operational evidence.

### Layout

- session list with filters.
- FlightSession -> FlightLeg hierarchy.
- route map/track.
- times: movement, airborne, taxi, simulated, career-credit.
- takeoff/landing/touch-and-go/bounce summary.
- fuel.
- landing metrics/confidence.
- incidents/violations.
- passenger/cargo outcome.
- mission/economy settlement.
- assistance flags: pause/slew/teleport/time acceleration where relevant.

### Rules

- Free/practice flights can be logged or discarded from pilot logbook.
- Owned-aircraft physical consequences remain even if a free flight is not logged.
- Career-credit time does not multiply with accelerated simulation time.

## 14. Career

Purpose: show meaningful progression without MMO-style XP dependence.

### Layout

- licenses/ratings.
- total and recent experience.
- aircraft category/family/type experience.
- segmented reputation.
- employer/customer relationships.
- government/military qualifications.
- safety/reliability record.
- meaningful milestones/achievements.
- Career Level + XP meta-progression summary.

### Rules

Career Level + XP are accepted meta-progression, but they never bypass:

- license/rating,
- military authorization,
- aircraft capability,
- affordability,
- dispatch/safety rule,
- relationship/reputation requirement.

## 15. Settings

Purpose: configure OpenCareer and diagnose integration safely.

### Sections

- Simulator connection
  - SimConnect DLL/runtime status
  - reconnect behavior/status
  - live diagnostics entry point
- UI
  - density
  - text scaling/system behavior
  - reduced motion where relevant
- Gameplay
  - assistance preferences
  - notification preferences
  - unit display
- Persistence
  - save/database location display
  - backup/recovery status
- Diagnostics
  - logging level
  - export diagnostics
  - build/version information
- Optional online/AI services
  - clearly non-required
  - explicit enabled/disabled status

### Rules

- Never expose secrets in normal diagnostics.
- Core gameplay remains offline-first.
- Settings do not directly mutate domain balances/progression.
- Dangerous reset/destructive actions require confirmation.

## Shared state patterns

Every screen must support these cross-cutting states:

- MSFS unavailable.
- Reconnecting.
- Connected but no stable telemetry.
- Stable telemetry but no active FlightSession.
- Active FlightSession.
- stale data.
- partial/low-confidence aircraft data.
- database failure/recovery.
- optional network/AI service unavailable.

The visual shell remains useful even when MSFS is closed.
