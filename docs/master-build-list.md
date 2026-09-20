# OpenCareer Master Build List

Status: **CANONICAL high-level list of unfinished major product systems**.

This file is the active "what is left to build" list.

## Ordering rule

Items are sorted from **lowest to highest estimated remaining direct engineering effort** for a production-quality implementation.

The estimates are rough solo-development effort, not calendar promises. They exclude time waiting on prerequisite systems, user testing, external SDK fixes, store/signing approval, and unknown simulator/API behavior. A short item may therefore be blocked by a larger prerequisite even though its own implementation effort is small.

The `MBL-xx` identifier is stable and does not change when this file is reordered.

## Maintenance rules

- Keep only unfinished major systems here.
- Remove an item only after it is implemented, integrated, tested and accepted.
- Partial foundations do not count as complete.
- Git history and `docs/development-master-checklist.md` preserve completed implementation history.
- New major scope goes here when accepted.
- Technical dependency order in `ASTRA.md` can override this effort-based order.

## Remaining — lowest to highest effort

### MBL-02 — Reusable WinUI design system — CODE COMPLETE / VERIFICATION PENDING
**Estimated remaining effort: ~0.25–0.5 developer day**

The reusable code layer is implemented: centralized Jet Age tokens plus shared typography, paper/metal panels, buttons, form controls, toggles, tabs, lists, ledger/table rows and semantic status styles. Dashboard, Current Flight, Settings, placeholders, shell surfaces and the tutorial overlay use the shared resources. Automated design-contract tests lock the vivid-ivory palette, contrast and required component keys.

Remaining before removal: local interactive/visual/accessibility acceptance. Public-repository Windows CI now builds the WinUI surface successfully, so the remaining gate is local product acceptance rather than compilation.

### MBL-23 — Settings / diagnostics system — CODE COMPLETE / VERIFICATION PENDING
**Estimated remaining effort: ~0.25–0.5 developer day**

Implemented:

- persistent versioned application preferences,
- Aviation/Metric unit selection with immediate live telemetry reformatting,
- input-hint ordering preference without guessed bindings,
- persisted **Show checklist every flight** preference,
- persisted automatic tutorial-offer preference,
- persisted reduced-motion preference,
- live MSFS/SimConnect/telemetry diagnostics,
- local rotating application log,
- diagnostic ZIP export with exact coordinates intentionally excluded,
- current local-data ZIP backup,
- local data/log/backup/export folder actions,
- offline-first optional-online-services permission gate,
- tutorial replay/preview controls,
- safe preference reset that does not touch career state.

Dependency boundaries remain explicit: MBL-05 owns real controller/keyboard binding discovery, and MBL-07 owns authoritative SQLite FlightSession/save recovery. The Settings surface already reports those capabilities as unavailable rather than pretending they exist.

Remaining before removal: local interactive verification of persistence, folders, backup/export and accessibility behavior. Public-repository Windows CI now builds the Settings surface successfully.

### MBL-03 — Full tutorial engine — IN PROGRESS
**Estimated remaining direct effort: ~0.5–1.5 developer days**

Core engine is implemented and CI-green: versioned first-run app tour, persistent resume/skip/completion, shell overlay/navigation, Settings replay, first-job walkthrough, and banner/carrier previews.

Remaining before removal:

- local interactive/visual acceptance,
- automatic first-job trigger when Jobs exists,
- automatic specialized mission-family trigger when those mission families exist,
- mission/checklist evidence integration.

### MBL-01 — Production Home / Dashboard — IN PROGRESS
**Estimated remaining direct effort: ~0.5–1.5 developer days plus downstream integrations**

The production dynamic Home surface and contracts are implemented: split flight/career hero, daily P/L header, deterministic next-action routing, four-tier Top Opportunities foundation, career/company/finance/aircraft/readiness modules, world summary, recent activity, searchable OpenCareer Network feed, and an Active Operation panel for accepted work/checklist/blocker/next-action state.

Dashboard snapshot guidance is centralized and domain-tested for active operations, maintenance blockers, probation/suspension/termination and job recommendations. Home refreshes its non-telemetry snapshot every 10 seconds while visible with overlap protection and cancellation on navigation. Windows WinUI, live-probe, Linux, SimLab and unit-test CI all passed after the first converter namespace defect was fixed.

The UI intentionally shows honest empty/unavailable states until authoritative systems provide data. Final completion depends on Jobs, Career/XP, Company employment, Aircraft Registry/location, Economy/Finances and Map / World sources wiring into `IDashboardSnapshotSource`, plus local visual/runtime acceptance.

### MBL-22 — AI dispatcher / copilot / passengers
**Estimated effort: ~2–5 developer days for the first production version**

Optional narrative/dialogue layer that never owns authoritative game state.

This estimate covers the first safe production integration, not an unlimited library of personalities/content.

### MBL-12 — Debrief + Logbook — IN PROGRESS
**Estimated remaining direct effort: ~1–3 developer days plus MBL-07 / settlement integration**

The production Logbook/Debrief foundation is implemented: immutable historical debrief snapshots, FlightSession -> FlightLeg hierarchy, decimated multi-leg route tracks, independent time/experience dimensions, fuel/payload/assistance facts, landing episode metrics with evidence quality, incidents/events, mission/safety outcome separation, frozen settlement references, committed-logbook statistics, search/type/outcome filters, and a real Jet Age WinUI Logbook screen.

An application-level idempotent commit coordinator prevents retry-created duplicate logbook entries and derives career-log idempotency from the authoritative settlement key. Free/practice flights use a separate manual-log path. Unknown evidence remains unknown rather than being inferred.

SQLite persistence is now implemented in `OpenCareer.Infrastructure`: a versioned `opencareer.db` schema, persistent `ILogbookSource` / `ILogbookWriter`, indexed history/search fields, frozen JSON debrief payloads, WAL mode, startup migration, and idempotent duplicate protection. The WinUI app is wired to the SQLite store instead of the unavailable placeholder.

Remaining before removal:
- map real completed FlightSession evidence into `FlightDebrief`,
- wire career settlement -> exactly-once Logbook commit,
- wire free/practice **Log Flight / Discard from pilot logbook** postflight flow,
- feed persisted route/landing/fuel/payload evidence from live sessions,
- add database-consistent backup/recovery with MBL-07,
- complete local interactive/visual acceptance.

MBL-12 does not own mission success or economy settlement; it records their authoritative final results.

### MBL-04 — Live per-flight checklist
**Estimated effort: ~3–6 developer days**

Phase-aware preflight through shutdown checklist with trustworthy live auto-verification.

### MBL-06 — FlightEvidenceProcessor
**Estimated effort: ~4–7 developer days**

Normalized telemetry to stable taxi/takeoff/airborne/approach/landing/go-around/parking evidence.

Includes configuration, hysteresis, edge cases and regression coverage. Live calibration may extend elapsed calendar time.

### MBL-24 — Installer / update / release pipeline
**Estimated effort: ~4–8 developer days**

Packaging, migrations, backups, production logging, performance validation, signing and update strategy.

Final release acceptance naturally waits until the app is much closer to feature-complete.

### MBL-05 — Controller + keyboard binding system
**Estimated effort: ~4–8 developer days**

Per-step verified bindings, UNBOUND states and aircraft/device-sensitive mapping.

This is more uncertain than its size suggests because MSFS binding/profile accessibility must be proven rather than guessed.

### MBL-15 — Aircraft purchasing + financing
**Estimated effort: ~5–8 developer days**

Persistent dealers/lenders, used/new inventory, loans, insurance and atomic ownership transfer.

Credit/dealer domain foundations already exist, reducing remaining effort.

### MBL-07 — Persistent FlightSession / recovery
**Estimated effort: ~5–9 developer days**

SQLite checkpoints, multi-leg sessions, route/event persistence, interruption recovery and duplicate-effect prevention.

### MBL-16 — Hangars + Bases
**Estimated effort: ~5–10 developer days**

Physical fleet geography, storage, local services, relationships, expansion and reposition logistics.

### MBL-13 — Career progression
**Estimated effort: ~6–10 developer days**

Licenses, ratings, experience, recency, reputation, relationships, qualifications and milestones.

### MBL-20 — World Map
**Estimated effort: ~6–12 developer days**

Airports, bases, fleet/company geography, routes, opportunities, events, markets and authorized government/military overlays.

### MBL-08 — Installed-aircraft registry
**Estimated effort: ~7–12 developer days**

Installed aircraft identity, capability/confidence, location/experience and installed/employer/rented/assigned/owned access.

Aircraft discovery and reliable capability inference are the main uncertainty.

### MBL-09 — Dispatch system
**Estimated effort: ~7–14 developer days**

Payload, fuel/range, runway, weather/environment, qualification and mission feasibility with explicit blocking reasons.

### MBL-17 — Maintenance
**Estimated effort: ~8–14 developer days**

Wear, service, repairs, parts/MRO, downtime, history and supported incident consequences.

### MBL-14 — Economy + Markets
**Estimated effort: ~8–15 developer days**

Authoritative persistent economy, named cargo, market pressure/events, long absence and bankruptcy/recovery.

A substantial deterministic foundation already exists, which keeps this below a from-scratch economy implementation.

### MBL-10 — Playable Jobs system
**Estimated effort: ~10–18 developer days**

Market/location/career-driven work with employee viability and accept-to-active-operation flow.

This is where many earlier foundations become one playable product loop.

### MBL-21 — MSFS EFB companion
**Estimated effort: ~10–20 developer days**

Thin in-sim projection for Current Flight, checklist, objectives, route and next action while Windows remains authoritative.

MSFS SDK/version-sensitive behavior is the largest uncertainty.

### MBL-18 — Company system
**Estimated effort: ~12–25 developer days**

Optional business ownership, contracts, routes, utilization, staff, margins and expansion while preserving employee-only play. Employer careers must include rank/standing plus deterministic probation, suspension, demotion and firing/termination behavior with fair recovery paths.

### MBL-19 — Military / Government career system — PERSISTENT CAMPAIGN FOUNDATION IN PROGRESS
**Estimated remaining effort: ~6–14 developer days**

Draft PR #10 now provides deterministic ground/air conflict state, sector/front representation, linked air-defense/interceptor threats, battlefield-driven CAS/suppression/recon/logistics/patrol/escort/intercept lifecycles, OpenCareer-only effects/damage, military qualification and assigned-aircraft authorization, seeded fictional theater generation, deterministic operation/faction identity, SQLite campaign/active-mission recovery, persisted threat idempotency, strategic phase/momentum/objectives, bounded logistics/recovery/control cycles, finite logistics-gated replacement reserves, terminal Victory/Defeat/Stalemate/Ceasefire outcomes, persistent completed-operation history, deterministic successor-operation planning/transition, an authorization-enforcing dispatch boundary, and a UI-ready operations projection. MSFS remains flight/telemetry only.

Automatic startup/resume, SQLite-consistent backup snapshots, and validated staged restore with restart-time apply/rollback are now implemented. Remaining major work is a user-facing restore selection/confirmation flow, successor-operation offer/acceptance UI and richer faction behavior after terminal outcomes, main-career onboarding/persistence integration, fleet assignment issuance/revocation, authoritative job/economy settlement integration, production Military/Government UI, large deterministic balance/playtesting, and live telemetry gameplay verification.
### MBL-11 — Specialized mission framework
**Estimated effort: ~20–40+ developer days for the full planned family set**

Banner tow, carrier operations, glider tow, skydiving, firefighting, SAR, survey, medevac, special government work and other unique mission families.

This is the largest single feature family because each mission type needs:

- deterministic objectives,
- simulator evidence,
- failure/recovery handling,
- aircraft/capability constraints,
- UI/checklist behavior,
- tutorial integration,
- tests,
- and mission-specific balancing.
