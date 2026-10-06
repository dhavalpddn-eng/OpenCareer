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

Primary/Secondary/Danger button readability now has exact-PC normal/hover acceptance and variant-scoped state resources retain the stock WinUI template. Remaining before removal: interactive keyboard-focus, high-contrast, text-scaling and broader visual/accessibility acceptance. Public-repository Windows CI builds the WinUI surface successfully.

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

Dependency boundaries remain explicit: MBL-05 owns real controller/keyboard binding discovery, and MBL-07 owns FlightSession persistence/recovery. Settings now projects the existing SQLite-backed FlightSession recovery state from the authoritative runtime without mutating it.

Remaining before removal: local interactive verification of persistence, folders, backup/export and accessibility behavior. Public-repository Windows CI now builds the Settings surface successfully.

### MBL-03 — Full tutorial engine — IN PROGRESS
**Estimated remaining direct effort: ~0.5–1.5 developer days**

Core engine is implemented and CI-green: versioned first-run app tour, persistent resume/skip/completion, shell overlay/navigation, Settings replay, first-job walkthrough, automatic first-job triggering from authoritative career/Jobs state, and banner/carrier previews.

Remaining before removal:

- local interactive/visual acceptance,
- automatic specialized mission-family trigger when those mission families exist,
- mission/checklist evidence integration.

### MBL-01 — Production Home / Dashboard — CURRENT-AUTHORITY CODE COMPLETE / VERIFICATION PENDING
**Estimated remaining direct effort: ~0.25–0.5 developer day plus downstream integrations**

The production dynamic Home surface and contracts are implemented: split flight/career hero, daily P/L header, deterministic next-action routing, four-tier Top Opportunities foundation, career/company/finance/aircraft/readiness modules, world summary, recent activity, searchable OpenCareer Network feed, and an Active Operation panel for accepted work/checklist/blocker/next-action state.

Dashboard snapshot guidance is centralized and domain-tested for active operations, maintenance blockers, probation/suspension/termination and job recommendations. Home refreshes its non-telemetry snapshot every 10 seconds while visible with overlap protection and cancellation on navigation. Windows WinUI, live-probe, Linux, SimLab and unit-test CI all passed after the first converter namespace defect was fixed.

Production registers `PlayerCareerDashboardSnapshotSource`. It projects authoritative career qualifications/experience and player/home location, cash/today net, recent committed flights, one deterministic active operation with existing readiness/blockers, and its contract-reserved aircraft identity when reservation/registry evidence exists. `CareerJobOfferEligibilitySource` reads the persisted local board and delegates aircraft readiness to the existing start action; Dashboard projects up to four proven-startable offers in deterministic source order with route, duration/distance and qualifying-aircraft evidence. It also exposes the authoritative eligible-local count and source status.

Tier, fit, gross/net offer pay and quality ranking remain explicitly unknown because no current authority provides them. Company/employment, ownership, maintenance readiness, physical aircraft location, obligations, social activity and broader World signals remain null/empty. Final removal depends on local adaptive-layout/keyboard/visual acceptance and projecting later upstream authorities without fabrication; current Dashboard work must not block those systems.

### MBL-22 — AI dispatcher / copilot / passengers
**Estimated effort: ~2–5 developer days for the first production version**

Optional narrative/dialogue layer that never owns authoritative game state.

This estimate covers the first safe production integration, not an unlimited library of personalities/content.

### MBL-12 — Debrief + Logbook — IN PROGRESS
**Estimated remaining direct effort: ~1–3 developer days**

The production Logbook/Debrief foundation is implemented: immutable historical debrief snapshots, FlightSession -> FlightLeg hierarchy, decimated multi-leg route tracks, independent time/experience dimensions, fuel/payload/assistance facts, landing episode metrics with evidence quality, incidents/events, mission/safety outcome separation, frozen settlement references, committed-logbook statistics, search/type/outcome filters, and a real Jet Age WinUI Logbook screen.

An application-level idempotent commit coordinator prevents retry-created duplicate logbook entries and derives career-log idempotency from the authoritative settlement key. Free/practice flights use a separate manual-log path. Unknown evidence remains unknown rather than being inferred.

SQLite persistence is implemented in `OpenCareer.Infrastructure`: a versioned `opencareer.db` schema, persistent `ILogbookSource` / `ILogbookWriter`, indexed history/search fields, frozen JSON debrief payloads, WAL mode, startup migration, and idempotent duplicate protection. Supported terminal career flights are projected from the authoritative FlightSession and committed after settlement through the production terminal workflow. Replay uses the settlement-derived key and cannot duplicate the ledger or Logbook entry.

Remaining before removal:
- add broader event/scoring evidence only where trustworthy aircraft-relative inputs exist,
- complete local interactive/visual acceptance.

Completed since the prior reconciliation: explicit free/practice **Log Flight / Discard** orchestration and Current Flight actions, persisted multi-leg time/statistics/route/landing ownership with multi-leg debrief projection, and persisted first-touchdown VS/G/IAS/pitch/bank metrics. Retry/restart paths remain idempotent.

MBL-12 does not own mission success or economy settlement; it records their authoritative final results.

### MBL-04 — Live per-flight checklist
**Estimated effort: ~3–6 developer days**

Phase-aware preflight through shutdown checklist with trustworthy live auto-verification.

### MBL-06 — FlightEvidenceProcessor — CORE FULL-STOP PATH IMPLEMENTED / EDGE CALIBRATION PENDING
**Estimated remaining effort: ~4–7 developer days plus live validation**

Production converts normalized telemetry into stable load, engine-start, taxi, takeoff/reject, airborne, approach, touchdown, bounded bounce/recontact, rollout, parking and shutdown evidence. The runtime drains a bounded ordered high-rate flight-critical stream, preserves the 1 Hz `Latest` contract, rejects pause/slew/stale evidence and transactionally restores processor state when persistence fails. The C172/KJFK full-stop and bounce path is exact-PC accepted.

Telemetry-derived touch-and-go/go-around/new-approach classification, active-session aircraft-identity enforcement, simulation-rate/night/actual-instrument accounting, persisted first-touchdown quantitative metrics and bounded 1/3/6-hour replay are implemented and CI-covered.

Remaining: broader representative-aircraft/edge calibration, aircraft-relative landing scoring/thresholds, richer event windows where trustworthy evidence exists, and local/live acceptance of the newer evidence paths. The exact-PC live pass remains limited to the established C172/KJFK full-stop/bounce path.

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

### MBL-07 — Persistent FlightSession / recovery — CORE CHECKPOINT/RECOVERY IMPLEMENTED / HARDENING PENDING
**Estimated remaining effort: ~1–3 developer days plus broader live validation**

The production runtime owns one authoritative FlightSession, persists versioned SQLite checkpoints before publication, checkpoints transitions and bounded steady-state intervals, stores decimated route/landing summaries, suspends on disconnect, recovers at startup, resumes only on plausible continuity and prevents duplicate terminal effects. It falls back to the previous valid checkpoint when the newest generation is corrupt, passes bounded 1/3/6-hour replay, persists ordered multi-leg FlightLeg boundaries/evidence, and resumes interrupted terminal workflows across restart. Interrupted-session recovery and the complete C172/KJFK lifecycle are live accepted.

Remaining: richer event windows where justified, broader corrupt/restart/live edge validation, and local acceptance beyond the established C172/KJFK path. Multi-leg production creation remains explicit; automatic turnaround detection is intentionally not implemented.

### MBL-16 — Hangars + Bases
**Estimated effort: ~5–10 developer days**

Physical fleet geography, storage, local services, relationships, expansion and reposition logistics.

### MBL-13 — Career progression — IN PROGRESS
**Estimated remaining effort: ~6–10 developer days**

Persistent career profile, onboarding/home location, qualifications foundation and exactly-once experience/travel application from committed career Logbook entries are implemented. Development/Test flights remain progression-neutral.

Remaining: the licensing/training ladder, ratings, recency, civilian reputation/relationships, category/model experience, milestones and their job/employer gates.

### MBL-20 — World Map
**Estimated effort: ~6–12 developer days**

Airports, bases, fleet/company geography, routes, opportunities, events, markets and authorized government/military overlays.

### MBL-08 — Installed-aircraft registry — IN PROGRESS
**Estimated remaining effort: ~7–12 developer days**

Implemented foundation: SimConnect catalog/current TITLE plus package discovery, canonical identity resolution, persistent installed observations, per-field provenance/confidence, configuration enrichment and one complete C172 reference profile. Current-aircraft identity and picker stability are live accepted.

Remaining: broad family/type normalization and capability coverage, aircraft location/experience, and authoritative owned/rented/employer/assigned access. Reference data never implies installation or ownership.

### MBL-09 — Dispatch system — IN PROGRESS
**Estimated remaining effort: ~7–14 developer days**

Implemented foundation: persistent Fleet availability/reservations, contract-owned acquire/recovery/release, airport/runway sources with simulator/reference fallback, and fail-closed payload/range/weight/fuel/runway/weather/safety-margin evaluators. The supported civilian ferry/reposition path checks installation, access, qualifications and physical feasibility before start.

Remaining: production use of the complete composite fuel/weather planning path, broader aircraft/airport data, board-generation feasibility filtering, military assignment issuance/revocation and the dedicated Dispatch UI.

### MBL-17 — Maintenance
**Estimated effort: ~8–14 developer days**

Wear, service, repairs, parts/MRO, downtime, history and supported incident consequences.

### MBL-14 — Economy + Markets — IN PROGRESS
**Estimated effort: ~8–15 developer days**

Authoritative persistent economy, named cargo, market pressure/events, long absence and bankruptcy/recovery.

Persistent SQLite contract terms and ledger settlement now complete supported jobs exactly once; the terminal workflow also applies experience/location markers before cleanup. Civilian reputation/relationship application, broader operating-cost authorities, named cargo, persistent market-demand integration, long absence and bankruptcy/recovery execution remain open.

### MBL-10 — Playable Jobs system — FIRST LOOP IMPLEMENTED / BREADTH PENDING
**Estimated remaining effort: ~10–18 developer days**

The production Jobs surface now provides persisted deterministic board refill, installed-aircraft selection/readiness, contract acceptance, Fleet reservation, dispatch, FlightSession start/recovery, mission completion, settlement, Logbook, experience/location finalization, reservation release, abandon cleanup and exact-once replay. The zero-progression C172/KJFK Development/Test circuit runs through those same authorities and is live accepted.

Remaining: authoritative market-demand-driven generation, generation-time aircraft feasibility filtering, job kinds beyond Ferry/Reposition, representative passenger/cargo/utility work, richer briefing, civilian reputation/relationship consequences and live validation of an ordinary compensated route.

### MBL-21 — MSFS EFB companion
**Estimated effort: ~10–20 developer days**

Thin in-sim projection for Current Flight, checklist, objectives, route and next action while Windows remains authoritative.

MSFS SDK/version-sensitive behavior is the largest uncertainty.

### MBL-18 — Company system
**Estimated effort: ~12–25 developer days**

Optional business ownership, contracts, routes, utilization, staff, margins and expansion while preserving employee-only play. Employer careers must include rank/standing plus deterministic probation, suspension, demotion and firing/termination behavior with fair recovery paths.

### MBL-19 — Military / Government career system — CAMPAIGN CORE COMPLETE / INTEGRATIONS PENDING
**Estimated remaining effort: ~6–14 developer days**

Draft PR #10 now provides deterministic ground/air conflict state, sector/front representation, linked air-defense/interceptor threats, battlefield-driven CAS/suppression/recon/logistics/patrol/escort/intercept lifecycles, OpenCareer-only effects/damage, military qualification and assigned-aircraft authorization, seeded fictional theater generation, deterministic operation/faction identity plus Defensive/Aggressive/LogisticsFocused/AirFocused operational posture, posture-biased replacement/support-request behavior, SQLite campaign/active-mission recovery, strategic phase/momentum/objectives, bounded campaign cycles, finite replacement reserves, terminal Victory/Defeat/Stalemate/Ceasefire outcomes, persistent completed-operation history, deterministic successor planning/actions with stale-offer protection, an authorization-enforcing dispatch boundary, the production Military/Government WinUI screen using the application snapshot/transition boundaries, persisted completed-operation history, a read-only schematic Operational Map projecting units/threats/support targets, a deterministic current-state Communications feed for command/flight/dispatch/intelligence messages, and an interactive read-only completed-operation drill-down with persisted terminal facts, polling-stable selection, keyboard/focus UX and SQLite restart/no-write verification. MSFS remains flight/telemetry only.

Campaign-core completion boundary reached at `16cda14981bc6284e4f33833e88aa8289ff46149`: production mission completion/failure now settles through the persisted operation-consequence pipeline without the legacy duplicate trust/reputation path, faction posture evolves deterministically with campaign phase changes, and a bounded 96-step deterministic campaign evolution stress test verifies replay equality and state bounds across campaign state, reserves, sectors, units, air units, threats, support requests, identity and postures. Exact-head Linux PR CI for `16cda14981bc6284e4f33833e88aa8289ff46149` is green; exact-head Windows CI is green as well.

MBL-19 remains open only for dependency-driven integration and product acceptance: authoritative career onboarding/profile integration (MBL-13), aircraft assignment issuance/revocation through registry/dispatch (MBL-08/09), authoritative mission/job/economy settlement (MBL-10/14), representative live gameplay verification after flight-evidence/session-recovery gates (MBL-06/07), and local Military/Government visual/accessibility acceptance. No further standalone campaign-core mechanics slice is queued unless a concrete defect is found; broader balance/playtesting remains later tuning work.
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
