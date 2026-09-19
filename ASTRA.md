# OpenCareer — Astra Fast Resume

**Read this first.** If coding, `AGENTS.md` is canonical. Verify remote head before writes.

**Repo:** `dhavalpddn-eng/OpenCareer`  
**Branch:** `feature/m1-simulation-core`  
**PR:** #2 draft; keep `main` stable.  
**Master build list:** `docs/master-build-list.md` — active unfinished major systems sorted low-to-high by estimated remaining engineering effort; stable `MBL-xx` IDs remain unchanged; remove items only when fully implemented, integrated, tested and accepted.  
**Detailed tracker:** `docs/development-master-checklist.md` — implementation history and subsystem checkboxes.  
**CI policy:** repo is public; standard Linux/Windows hosted runners are active. Feature pushes do not duplicate PR CI; docs-only updates are excluded from full builds; changed-file gates prevent cumulative PR diffs from forcing irrelevant validation. Relevant WinUI changes get Windows validation during the draft PR.  
**Last implementation commit:** `73bb123b1fbd40b6690e50935d762375d74721c7` (MBL-12 Debrief + Logbook foundation through route/search/UI compile fix). Latest CI configuration: `dbeb67923c61e96ebb79fecb2e8d294e2792266d`.

**Stack:** C# / .NET 10 / WinUI 3 / Windows x64 / SimConnect / SQLite / xUnit. Offline-first. AI/cloud never authoritative for money, ownership, mission completion, flight hours, scoring, or settlement.

**Canonical UI language:** `docs/ui-design-language.md`. All screens must preserve the dark blue-gray riveted shell, vivid ivory paper panels, restrained navy/brass accents and retro-modern 1950s/60s aviation-operations character. Do not drift back to generic SaaS, olive/yellow wash, or sepia-heavy styling.

## Active product build
**Production Dashboard:** MBL-01 is in progress. Dynamic split-hero Home, daily P/L header, deterministic next-action routing, four-tier Top Opportunities, career/company/finance/aircraft/world/activity modules, Active Operation panel, and searchable OpenCareer Network feed are implemented against `IDashboardSnapshotSource`. Home refreshes every 10 seconds while visible with overlap protection/cancellation. Snapshot guidance is centralized/tested for active operations, maintenance blockers, company probation/suspension/termination and jobs. Current source returns honest empty state until Jobs/Career/Company/Aircraft/Economy/World systems become authoritative. Opportunity tiers: Green Standard, Blue Specialist, Purple Elite, Orange/Gold Legendary. Career Level + XP are accepted meta-progression but never bypass licenses/ratings/reputation/authorization/capability/affordability/dispatch. Company employment can include probation/suspension/demotion/firing; termination must be deterministic/fair and leave recovery paths.
**Settings / diagnostics:** MBL-23 code layer is implemented: persistent preferences, Aviation/Metric live telemetry formatting, checklist/tutorial/input-hint/reduced-motion/online-service preferences, live SimConnect diagnostics, local file logging, diagnostic ZIP export, local-data backup and folder actions. MBL-05 still owns actual binding discovery; MBL-07 still owns authoritative SQLite FlightSession/save recovery. MBL-23 remains on the master list only until Windows build + local interactive verification.
**WinUI design system:** MBL-02 code layer is implemented: centralized Jet Age tokens plus reusable typography, paper/metal panels, buttons, form controls, toggles, tabs, lists/tables and semantic status styles; current shell/pages are migrated and design-contract tests protect vivid-ivory/contrast/style keys. Windows CI now builds it; MBL-02 remains open only for local interactive/visual/accessibility acceptance.
**Debrief / Logbook:** MBL-12 is in progress and CI-green at `73bb123` on Linux and Windows. Domain/Application/UI foundations cover frozen debrief history, FlightSession -> FlightLeg hierarchy, decimated route tracks, time/experience, fuel/payload/assistance, landing/event evidence quality, separate safety/mission outcomes, settlement references, committed-log statistics, filters/search, route projection and an idempotent commit coordinator. Production Logbook UI is implemented but `UnavailableLogbookSource` stays honestly empty until MBL-07 supplies SQLite FlightSession/FlightLeg storage. Remaining: real session->debrief mapping, persistent `ILogbookSource`/`ILogbookWriter`, career settlement->commit, free-flight Log/Discard, and local visual acceptance.
**Tutorial engine:** MBL-03 is in progress. The versioned coordinator, persistent progress/resume/skip, first-run WinUI overlay/navigation, Settings replay, first-job walkthrough, and banner/carrier tutorial previews are implemented and CI-green. Current automated baseline is **118/118 xUnit + 29/29 SimLab**, with Windows WinUI/probe build green. MBL-03 remains until local interactive/visual acceptance and automatic future job/mission trigger integration are verified.

## Built
WinUI shell; resilient serialized SimConnect connect/reconnect; ~1 Hz normalized telemetry; Windows live probe; pure `FlightTrackingStateMachine`; `FlightTimeLedger`; conventional park/shutdown/servicing terminal policy; offline JSONL trace analyzer. CI at `540029c`: **104/104 xUnit + 29/29 SimLab; Windows WinUI/probe green**. Analyzer fixtures are synthetic; live validation remains open.

## Career start
**F-22/KRME is a developer validation fixture only.** A standard new career starts from scratch: no owned F-22, no automatic military access and no required aircraft ownership. The player builds from entry training/employer/rental/assigned-aircraft work appropriate to qualifications, then earns access, financing and ownership through progression.

## Flight rules
Normal jobs cold-and-dark at parking; mission profiles may authorize runway starts. Block/pushback time != pilot flight time. Commercial multi-leg turns require park/shutdown/service. Rejected takeoff is distinct; no takeoff count. Crash preserves partial record; insurance may allow **1 redo/day, no carryover**. Disconnect only suspends; no short timeout; resume on plausible continuity. Sim-rate allowed anytime, but career credit = `wall_delta * min(rate,1)`. Legit safety decisions may earn small bounded credit; no farming. Landing scoring aircraft-relative when trusted data exists. Use simulator time/weather; normal night fixed-wing jobs need suitable lighting. Actual-instrument credit needs simulated IMC evidence. Initial progression is simplified FAA-style; aircraft experience is layered: total -> category/class -> propulsion/complexity -> family -> model/type -> recency/TO/L.

## EFB
Accepted future thin UI:
`MSFS EFB TS/JSX -> CommBus JSON -> C# SimConnect -> Domain/SQLite -> EFB projection`.
Windows app stays authoritative. See `docs/efb-integration.md`.

## HARD GATE
**Live MSFS runtime validation is PARTIAL, not accepted.** The 2026-09-18 trace proved connection, telemetry, pause observations, clearing and reconnect, and exposed a gear-scale defect fixed in `2accedb6`. The capture had no clean session end and the gear correction still needs a short live rerun. Do not freeze IAS/AGL/hysteresis thresholds or claim full live correctness.

Run: `./tools/run-live-probe.ps1`  
First live validation fixture: **F-22 at KRME** (test only; not a career starting aircraft). Validate absent/connect/load/~1 Hz/pause/taxi/takeoff/landing/quit/restart/abrupt exit/telemetry clear/reconnect.

## Next
1. Rerun the live probe briefly and verify `gearDown` changes correctly on the ground and in flight; end the probe cleanly so `sessionEnd` is recorded.
2. Analyze the rerun and fix only additional concrete SimVar/unit/runtime issues.
3. Build configurable `FlightEvidenceProcessor` -> tested reducer evidence.
4. Add versioned SQLite `FlightSession`/`FlightLeg` checkpoint/recovery.
5. Then registry/runway feasibility -> jobs -> mission validation + atomic settlement.

**Do not:** merge PR/main without explicit approval; expand finance/dealers before playable flight core; let AI/cloud become gameplay authority.

Deep docs only if needed: `docs/ui-design-language.md`, `docs/development-master-checklist.md`, `docs/project-state.md`, `docs/simulator-connection.md`, `docs/flight-session-design.md`, `docs/efb-integration.md`, `docs/cargo-market-requirements.md`, `docs/ui-screen-spec.md`, `docs/development-workflow.md`.
