# OpenCareer — Astra Fast Resume

**Read this first.** If coding, `AGENTS.md` is canonical. Verify remote head before writes.

**Repo:** `dhavalpddn-eng/OpenCareer`  
**Branch:** `feature/m1-simulation-core`  
**PR:** #2 draft; keep `main` stable.  
**Master build list:** `docs/master-build-list.md` — active unfinished major systems sorted low-to-high by estimated remaining engineering effort; stable `MBL-xx` IDs remain unchanged; remove items only when fully implemented, integrated, tested and accepted.  
**Detailed tracker:** `docs/development-master-checklist.md` — implementation history and subsystem checkboxes.  
**CI policy:** repo is public; standard Linux/Windows hosted runners are active. Feature pushes do not duplicate PR CI; docs-only updates are excluded from full builds; changed-file gates prevent cumulative PR diffs from forcing irrelevant validation. Relevant WinUI changes get Windows validation during the draft PR.  
**Parent M1 integration head at this slice branch point:** `92990f438a3155efc0594f38e39f9199fb41f32a` (FlightSession recovery/tracking/live-validation merge from PR #14).

**Stack:** C# / .NET 10 / WinUI 3 / Windows x64 / SimConnect / SQLite / xUnit. Offline-first. AI/cloud never authoritative for money, ownership, mission completion, flight hours, scoring, or settlement.

**Canonical UI language:** `docs/ui-design-language.md`. All screens must preserve the dark blue-gray riveted shell, vivid ivory paper panels, restrained navy/brass accents and retro-modern 1950s/60s aviation-operations character. Do not drift back to generic SaaS, olive/yellow wash, or sepia-heavy styling.

## Active product build
**Production Dashboard:** MBL-01 is in progress. Dynamic split-hero Home, daily P/L header, deterministic next-action routing, four-tier Top Opportunities, career/company/finance/aircraft/world/activity modules, Active Operation panel, and searchable OpenCareer Network feed are implemented against `IDashboardSnapshotSource`. Home refreshes every 10 seconds while visible with overlap protection/cancellation. Snapshot guidance is centralized/tested for active operations, maintenance blockers, company probation/suspension/termination and jobs. Current source returns honest empty state until Jobs/Career/Company/Aircraft/Economy/World systems become authoritative. Opportunity tiers: Green Standard, Blue Specialist, Purple Elite, Orange/Gold Legendary. Career Level + XP are accepted meta-progression but never bypass licenses/ratings/reputation/authorization/capability/affordability/dispatch. Company employment can include probation/suspension/demotion/firing; termination must be deterministic/fair and leave recovery paths.
**Settings / diagnostics:** MBL-23 code layer is implemented: persistent preferences, Aviation/Metric live telemetry formatting, checklist/tutorial/input-hint/reduced-motion/online-service preferences, live SimConnect diagnostics, local file logging, diagnostic ZIP export, local-data backup and folder actions. MBL-05 still owns actual binding discovery. FlightSession SQLite checkpoint/recovery from MBL-07 is implemented and merged; database-consistent whole-career backup remains a separate integration concern. MBL-23 remains on the master list only until Windows build + local interactive verification.
**WinUI design system:** MBL-02 code layer is implemented: centralized Jet Age tokens plus reusable typography, paper/metal panels, buttons, form controls, toggles, tabs, lists/tables and semantic status styles; current shell/pages are migrated and design-contract tests protect vivid-ivory/contrast/style keys. Windows CI now builds it; MBL-02 remains open only for local interactive/visual/accessibility acceptance.
**Debrief / Logbook:** MBL-12 is in progress. Domain/Application/UI foundations cover frozen debrief history, FlightSession -> FlightLeg hierarchy, decimated route tracks, time/experience, fuel/payload/assistance, landing/event evidence quality, separate safety/mission outcomes, settlement references, committed-log statistics, filters/search, route projection and an idempotent commit coordinator. Production now uses `SqliteLogbookStore` in the new Infrastructure layer: versioned `opencareer.db`, WAL, indexed filtering/search, immutable JSON payloads and unique idempotency keys. Linux at `6319492` is green with 177/177 tests + 29/29 SimLab; the SQLite-enabled WinUI app built successfully on Windows on the preceding code head. Remaining: real session->debrief mapping, career settlement->commit, free-flight Log/Discard, database-consistent backup/recovery, and local visual acceptance.
**Tutorial engine:** MBL-03 is in progress. The versioned coordinator, persistent progress/resume/skip, first-run WinUI overlay/navigation, Settings replay, first-job walkthrough, and banner/carrier tutorial previews are implemented and CI-green. Current automated baseline is **118/118 xUnit + 29/29 SimLab**, with Windows WinUI/probe build green. MBL-03 remains until local interactive/visual acceptance and automatic future job/mission trigger integration are verified.

## Built
WinUI shell; resilient serialized SimConnect connect/reconnect; normalized telemetry; production `FlightTelemetryEvidenceProcessor`; persistent/recoverable SQLite `FlightSession`; continuity/reconnect protection; route/statistics tracking; landing episodes; completion boundary; Current Flight, tutorial and logbook/debrief projection foundations; Windows live probe and offline trace analysis. Real MSFS 2024 validation after the final takeoff-continuity fix completed `Observing -> Preflight -> EngineStart -> TaxiOut -> TakeoffRoll -> Airborne -> Approach -> LandingEpisode -> TaxiIn -> Parked -> Shutdown -> Complete` with 1 takeoff, 1 landing episode, 0 bounces, 0 touch-and-goes, no FlightSession interruption and clean telemetry shutdown. Final Release xUnit tests and Windows x64 Release WinUI build passed at parent head `92990f4`. The tested F-22 reported nearly constant fuel quantity, so fuel burn remains non-authoritative until aircraft-specific telemetry is validated.

## Career start
**F-22/KRME is a developer validation fixture only.** A standard new career starts from scratch: no owned F-22, no automatic military access and no required aircraft ownership. The player builds from entry training/employer/rental/assigned-aircraft work appropriate to qualifications, then earns access, financing and ownership through progression.

## Flight rules
Normal jobs cold-and-dark at parking; mission profiles may authorize runway starts. Block/pushback time != pilot flight time. Commercial multi-leg turns require park/shutdown/service. Rejected takeoff is distinct; no takeoff count. Crash preserves partial record; insurance may allow **1 redo/day, no carryover**. Disconnect only suspends; no short timeout; resume on plausible continuity. Sim-rate allowed anytime, but career credit = `wall_delta * min(rate,1)`. Legit safety decisions may earn small bounded credit; no farming. Landing scoring aircraft-relative when trusted data exists. Use simulator time/weather; normal night fixed-wing jobs need suitable lighting. Actual-instrument credit needs simulated IMC evidence. Initial progression is simplified FAA-style; aircraft experience is layered: total -> category/class -> propulsion/complexity -> family -> model/type -> recency/TO/L.

## EFB
Accepted future thin UI:
`MSFS EFB TS/JSX -> CommBus JSON -> C# SimConnect -> Domain/SQLite -> EFB projection`.
Windows app stays authoritative. See `docs/efb-integration.md`.

## Live validation
The FlightSession/live SimConnect gate for the tested F-22/KRME developer fixture is accepted. Do not rerun the full live flight unless a new code change affects that area. This acceptance does not imply every aircraft exposes every telemetry field consistently; the F-22 fuel-quantity behavior remains an explicit follow-up.

## Next
1. Build the provider-neutral installed/known aircraft registry foundation without an aircraft whitelist.
2. Add airport/runway records and deterministic tri-state physical feasibility with explicit failure reasons.
3. Add later MSFS installed-aircraft and airport-data adapters behind Application interfaces; external aviation APIs remain enrichment/reference inputs.
4. Then extend dispatch with operation-specific payload/weight/range/weather/safety-margin checks before market-driven Jobs generation.

**Do not:** merge PR/main without explicit approval; expand finance/dealers before playable flight core; let AI/cloud become gameplay authority.

Deep docs only if needed: `docs/ui-design-language.md`, `docs/development-master-checklist.md`, `docs/project-state.md`, `docs/simulator-connection.md`, `docs/flight-session-design.md`, `docs/efb-integration.md`, `docs/cargo-market-requirements.md`, `docs/ui-screen-spec.md`, `docs/development-workflow.md`.
