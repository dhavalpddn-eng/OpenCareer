# OpenCareer project state

**Ultra-fast resume:** read root `ASTRA.md` first. This file is the detailed handoff.

Updated: 2026-09-28 against validated integration code head `f2acb36773d2ee280a1e83610bfdbbe9f5749b93`. **Read this after `AGENTS.md` when deeper implementation context is needed; do not reread chat history unless a required decision is missing.**

## Resume here

- Repo: `dhavalpddn-eng/OpenCareer`
- Branch: `feature/playable-loop-integration`. Keep `main` stable.
- Validated implementation baseline: `f2acb36773d2ee280a1e83610bfdbbe9f5749b93`.
- Exact-head Linux builds #904/#905 and Windows `winui-build` #836/#837 passed.
- Exact-PC C172/KJFK validation passed the SimConnect/current-aircraft path, production FlightSession takeoff/landing/bounce/shutdown/completion, interrupted-session recovery, exact-once terminal settlement/finalization, facility-request containment with corrected receive IDs, Jobs aircraft-picker stability and shared button-state readability.
- The isolated KJFK Development/Test profile and `tools/run-kjfk-live-test.ps1` protect normal career data while retaining restart/recovery behavior between launches unless explicitly reset.
- Master development tracker: `docs/development-master-checklist.md`; it contains the live chapter/subsystem checkboxes and Mermaid progress graphics. Update it whenever verified implementation status changes.
- UI visual language: `docs/ui-design-language.md` is canonical for palette/material/aesthetic; implementation tokens/layout remain in `docs/ui-design-system.md`, and screen behavior in `docs/ui-screen-spec.md`; visual references: `docs/assets/opencareer-dashboard-concept-v2.svg`, `docs/assets/opencareer-ui-screen-atlas.svg`, and `docs/assets/opencareer-conflict-operations.svg`. `docs/ui-concept.md` remains the short visual-direction entry point.
- Parallel military/conflict implementation: `feature/military-conflict-system`, draft PR #10 targeting `feature/m1-simulation-core`. The campaign-core completion boundary is reached: ground/air conflict, all current military mission families, authorization, fictional theater generation, deterministic operation/faction identity and phase-driven posture evolution, SQLite recovery/startup resume, strategic evolution, logistics recovery/control cycles, finite replacement reserves, terminal outcomes, persistent history, successor planning/decisions, SQLite-consistent backup/restore, production Military/Government UI, and production-wired deterministic operation resolution/consequences are implemented. Operation consequences persist in SQLite schema v5 with restart-safe duplicate protection, failure rollback and reconnect replay safety. Verified head `16cda14981bc6284e4f33833e88aa8289ff46149` is green in exact-head Linux PR CI and exact-head Windows CI. See `docs/conflict-system.md`.
- Verify remote branch head before edits because other chats may change it.

## Fixed direction

Single-player, offline-first MSFS 2024 companion. C#/.NET 10, Windows x64, WinUI 3/Windows App SDK, isolated SimConnect boundary, SQLite, deterministic/testable domain. No aircraft whitelist. AI is optional and never authoritative for money, ownership, mission completion or critical state.
- UI identity is fixed as a retro-modern aviation operations interface: dark blue-gray riveted structure, vivid ivory briefing panels, realistic MSFS 2024 imagery, restrained navy/brass/status accents, and 1950s/60s aviation character without sepia/olive drift. See `docs/ui-design-language.md`.

## Confirmed gameplay

- Current rapid live-test fixture: **C172 at KJFK**, using an explicit zero-progression Development/Test circuit through production acceptance, reservation, dispatch, FlightSession and terminal authorities.
- **F-22/KRME remains a historical developer telemetry fixture, not the required career start.** A standard new career starts from scratch with no owned aircraft and no automatic military access. Early progression may use training, employer, rental or assigned aircraft appropriate to qualifications; ownership and military access are earned separately.
- First owned used light aircraft should become reasonably attainable around **50–80 real flying hours** through actual savings/financing, not an hour unlock.
- Typical sessions 1–3 hours; support up to ~6 and recover interrupted sessions where technically possible.
- Home airport, local route connections, relationships, hangar/storage and additional bases matter; no teleporting the company around the map.
- Bankruptcy is possible but should usually require sustained bad decisions; employee work remains a recovery path.
- Management is mostly automatic. Eligible manual ground procedures may provide modest bounded rewards.
- Military-heavy airports produce much more military/government work while retaining appropriate civilian work.
- Military/rented/employer/assigned aircraft are distinct from owned aircraft.
- **Levels may be added later as meaningful meta-progression.** They may summarize experience or unlock convenience/prestige, but never bypass licenses/ratings, reputation, military authorization, aircraft capability, affordability or dispatch rules.
- Flight-session rules are now specified in `docs/flight-session-design.md`: tracking begins after aircraft load/stable telemetry, real-world-style movement time is distinct from load time, one session may contain multiple legs, bounce recontacts are one landing episode, touch-and-go is recorded distinctly from full-stop, safe diversions/go-arounds are not automatic failures, and conventional jobs default to destination parking + shutdown unless the mission defines another terminal condition.
- Free/practice flights are intended to be loggable for pilot experience; the manual Log/Discard flow remains open. Career-credit hours are distinct from accelerated simulated time so time acceleration remains usable without multiplying license/aircraft-experience progression. Material teleport/slew displacement is rejected; authoritative loaded-aircraft mismatch enforcement during an active FlightSession remains open.
- Cargo requirements are now specified in `docs/cargo-market-requirements.md`: generated cargo work carries named commodity lots (coffee, phones, televisions, food, plants and an extensible catalog) with quantity, physical properties, declared/market value and origin/destination economics rather than anonymous cargo weight.
- Onboarding is layered: a general app tutorial, a first-job tutorial, and versioned first-time mission-family tutorials for specialized procedures. Unique missions such as banner pickup and carrier takeoff/landing receive their own live guided checklists with Controller/Keyboard bindings and telemetry-driven completion only where the state can be verified. See `docs/intro-tutorial.md` and `docs/mission-tutorial-system.md`.
- 2026-09-17 cross-plugin flight review is recorded in `docs/flight-system-research-2026-09-17.md`. It strengthens Chapter 4 with pilot-log dimensions (day/night/cross-country/instrument/event attributes), planned-vs-actual route/diversion evidence, evidence provenance/confidence and bounded adaptive landing telemetry. External cloud/AI services remain optional and non-authoritative.

## Implemented domain foundation

- Deterministic economy/world events, replay/checkpoints, contract lifecycle/dispatch guards.
- Production Chapter 4 path: `FlightTelemetryEvidenceProcessor` converts normalized telemetry into reducer evidence; `FlightSessionRuntime` drains a bounded ordered high-rate critical stream, applies continuity, checkpoints through SQLite and restores processor state on failed persistence. The full-stop C172/KJFK path, interruption recovery and bounce exact-once behavior are live accepted; touch-and-go/go-around production evidence and broader aircraft calibration remain open.
- Location/home-base connection rules and travel-history guards.
- Protected-absence policy, session preferences, bankruptcy stages and bounded manual-ground reward quotes.
- Career-derived credit model, fictional lenders, affordability/debt-service checks.
- Fictional aircraft dealers, seeded offers/discounts, stock validation and cash/finance eligibility.
- Current exact-head Linux and Windows CI are green; historical test totals in older handoffs are no longer the current baseline.

## Validated playable-loop integration

- `FlightTelemetryEvidenceProcessor` and `FlightSessionRuntime` drive one authoritative career session from normalized telemetry through taxi, takeoff, airborne, approach, touchdown/bounce, rollout, taxi-in, parking and shutdown.
- `SqliteFlightSessionCheckpointStore` and `FlightSessionPersistenceService` persist-before-publish, recover active sessions as suspended, require plausible continuity to resume and prevent failed persistence from consuming telemetry evidence.
- Installed-aircraft discovery combines SimConnect catalog/current TITLE and package evidence; canonical registry resolution retains provenance/confidence and a C172 reference profile without treating reference data as installation or ownership.
- Jobs uses persistent board/contracts/Fleet reservations, fail-closed dispatch readiness and the production start/completion/abandon authorities. Current ordinary production scope is Ferry/Reposition; the KJFK circuit is an explicit zero-pay/zero-progression Development/Test offer.
- Completion orders FlightSession and contract completion before exactly-once SQLite settlement and Logbook commit, then experience/location markers, reservation release and terminal checkpoint cleanup. Replay is idempotent; automatic startup reconstruction after a crash midway through those steps remains open.
- Exact-PC validation at `f2acb367` closed the first C172/KJFK full-stop loop, bounce capture, interrupted-session recovery, terminal finalization, facility isolation, Jobs picker stability and button readability gates. This evidence does not generalize to every aircraft, mission or accessibility state.

## Military/conflict implementation (parallel PR #10)

- `OpenCareer.Domain/Conflict` contains versioned deterministic conflict state with ground units, simulated air units, sectors, derived front snapshots, linked threats and support requests.
- The world engine advances ground pressure/control and simulated air movement deterministically.
- Battlefield state can generate CAS, suppression, reconnaissance, logistics, patrol, escort and intercept requests.
- Support requests have Open/Reserved/Completed/Failed/Cancelled/Expired lifecycle state so one active need cannot be accepted twice.
- `AirSupportMissionEngine` handles CAS/suppression; `AreaSupportMissionEngine` handles recon/logistics/patrol; `AirOperationMissionEngine` handles escort/intercept.
- All player flight evidence comes from normalized `AircraftTelemetrySnapshot`; pause/slew and invalid ground/air states cannot advance objectives.
- Precision, suppression, reconnaissance and intercept effects are abstract deterministic OpenCareer effects; no native MSFS weapons/hits/damage are assumed.
- `ThreatExposureEvaluator` and `ThreatEngagementResolver` model simulated air-defense/interceptor threats and OpenCareer-only player damage.
- `MilitaryAuthorizationPolicy` separates affiliation, qualifications, aircraft assignment, aircraft capability/access and damage state. Installing/owning a military-capable aircraft never grants mission access by itself.
- The new operation-resolution pipeline converts simulator-agnostic `OperationResolutionInput` into deterministic Success / PartialSuccess / Failure / Aborted `OperationOutcome` values, protected by a stable operation+mission resolution key.
- `OperationConsequenceOrchestrator` applies isolated faction-influence, campaign-progress, thresholded territory-pressure, military-reputation and conflict-resource effects and returns one validated immutable result.
- `PersistedOperationConsequenceCoordinator` + `SqliteOperationConsequenceStore` persist that result exactly once in SQLite schema v5; persisted duplicates are rejected before the resolver runs, reconnect/replay is a no-op, and failed saves release in-memory reservations for a clean retry.
- The persisted consequence pipeline is wired into authoritative military mission completion/failure, including latest campaign/sector consequence-state lookup and replacement of the legacy direct trust/reputation update. Integration coverage exercises success, partial success, failure, abort, invalid input, duplicate/already-resolved operation, restart reload and rollback/retry.
- `ConflictTheaterGenerator` creates repeatable fictional theater state from a seed/template; current wars never become authoritative gameplay state.
- Military campaign persistence now auto-recovers the most recently saved campaign at app launch through `ConflictCampaignRuntimeState`; recovery failures are logged without blocking the rest of OpenCareer startup.
- OpenCareer backup now creates a SQLite-consistent snapshot instead of copying the live database/WAL files directly, so persisted military campaign state and logbook state are captured coherently.
- Backup restore now validates the manifest and SQLite image before staging, applies the staged database at the next app launch before military/logbook SQLite state is recovered, removes stale WAL/SHM artifacts, and keeps a rollback snapshot during replacement.
- Windows CI exposed a pooled temporary SQLite handle that prevented immediate backup-file reads; production snapshot connections now disable pooling and the rerun passed.
- Campaigns now persist a deterministic fictional operation identity plus distinct friendly/hostile faction names and short codes; old checkpoints without identity remain valid and gain the same deterministic identity when advanced.
- The Military/Government application snapshot exposes operation/faction identity to the production UI without changing the generic Friendly/Hostile simulation model.
- Long-term campaign advancement now includes bounded logistics-based ground recovery, theater-logistics air readiness recovery, momentum-based sector consolidation and linked-threat severity resynchronization before the strategic director evaluates the next campaign state.
- Campaign logistics now includes finite replacement reserves for each side; replacements require operational logistics and cannot regenerate units indefinitely.
- Campaigns now terminate deterministically as Victory, Defeat, Stalemate or Ceasefire. Secured states must persist across evaluations, mutual exhaustion can produce ceasefire, prolonged balanced low-momentum state can produce stalemate, and terminal campaigns cannot accept new operations.
- Mission telemetry synchronization no longer counts as a strategic campaign evaluation, preventing rapid telemetry saves from manufacturing stalemate/victory progress.
- Completed campaigns now archive operation/faction identity, theater, outcome and final strategic state into checkpoint history. Successor campaigns retain that history plus military career state and simulated player damage, while starting fresh strategic counters/replacement reserves and a new world.
- `ConflictCampaignTransitionPlanner` deterministically proposes the next fictional campaign from candidate theaters and avoids an immediate theater repeat when alternatives exist; accepting the offer reproduces its previewed operation identity.
- Completed-operation history persists through SQLite and is projected to the production Military/Government UI.
- `MilitaryCampaignTransitionService` owns successor offer projection and accept/decline/reconsider behavior, validates offers against the current campaign revision/catalog before acceptance, rejects stale offers and replaces the in-memory runtime record after successor creation. `MilitaryGovernmentViewModel` stays free of seed/theater-generation logic.
- The built-in successor theater set is behind `IConflictTheaterCatalog`; it is replaceable by later authoritative world/career selection without changing transition behavior.
- Fictional factions carry a deterministic operational posture: Defensive, Aggressive, LogisticsFocused or AirFocused. Posture is persisted as part of campaign identity and projected to the production Military/Government UI.
- Posture subtly biases deterministic behavior rather than acting as AI authority: battlefield/resupply request thresholds, request urgency, intercept/escort range and replacement-role priority can differ by posture. Both sides use posture for replacement priority; player-facing request generation follows the friendly faction posture.
- The Military/Government navigation destination now opens a production WinUI page rather than a placeholder. It projects active operation, factions/postures, phase/outcome, control/momentum, replacement reserves, military trust, OpenCareer damage, front/threat summary, support requests and strategic objectives through `MilitaryGovernmentViewModel`.
- Military/Government now includes persisted completed-operation history plus a read-only schematic Operational Map. The map projects snapshot units, threats and support targets into normalized presentation coordinates only; it is labeled not for navigation and keeps conflict authority outside the UI.
- Military/Government now includes a deterministic Communications feed built from the current application snapshot. It emits command status, active-flight status, dispatch support requests and threat advisories with bounded stable ordering; it does not use AI to decide or alter game state and does not invent historical events.
- Completed campaigns expose the deterministic successor offer directly in that page; Accept / Decline for now / Reconsider call `MilitaryCampaignTransitionService` rather than duplicating campaign-generation logic in the ViewModel.
- Successor acceptance handles expected storage failures and cancellation without escaping the UI event handler, preserves user-facing results across polling, and refreshes the full campaign after stale-offer rejection. Eight test cases cover safe retry, duplicate actions, decline/reconsider, cancellation, stale state and SQLite restart recovery. Hosted Linux and Windows CI verified code head `4c63e365` (see `docs/conflict-system.md` for runs). Completed-operation history and drill-down are now implemented; local visual/MSFS acceptance remains open.
- Code head `69337761` is green: Linux 373/373 xUnit + 29/29 SimLab; Windows WinUI/live-probe builds at 0 errors + 373/373 xUnit.
- The military branch's documented integration point included `feature/m1-simulation-core` FlightSession/OpenAI work. Its shared SQLite migration is schema v5: legacy parallel-v2 shapes are reconciled, schema v4 adds the military career profile, and schema v5 adds immutable `military_operation_consequences` persistence.
- Code head `4c63e365` is green: Linux 368/368 xUnit + 29/29 SimLab; Windows WinUI/live-probe builds at 0 errors + 368/368 xUnit.
- Code head `b95e8f2e` is green: Linux 260/260 xUnit + 29/29 SimLab; Windows WinUI/live-probe builds at 0 errors + 260/260 xUnit.
- Completed-operation drill-down is implemented and verified: archived rows are selectable, the detail projection is read-only and persisted-fact-driven, selection survives polling/current-campaign replacement/successor activation, stale history clears safely, native keyboard/focus behavior is preserved, and SQLite restart tests prove identical terminal detail with no database writes caused by viewing.
- Campaign-core completion boundary is reached at `16cda14981bc6284e4f33833e88aa8289ff46149`; the 96-step deterministic stress test is exact-head Linux/Windows CI green. No further standalone campaign mechanics slice is queued. Dependency-driven work remains: authoritative career onboarding/profile integration (MBL-13), aircraft assignment issuance/revocation through registry/dispatch (MBL-08/09), authoritative mission/job/economy settlement (MBL-10/14), and representative live gameplay verification after MBL-06/07 plus local MSFS runtime. Local visual/accessibility acceptance and broader balance/tuning remain later product-validation work.

## Tutorial engine implementation

- `OpenCareer.Application/Tutorials` now contains generic versioned tutorial models, catalog/readiness/progress boundaries and a coordinator.
- The application auto-launches the app-intro tutorial for a new/current tutorial version and persists resume, completed and skipped state outside authoritative career state.
- The WinUI shell renders Back / Next / Finish / Skip and navigates to the relevant top-level destination while preserving explicit **Coming Later** readiness for unfinished features.
- Settings can replay the app intro and preview the first-job, banner-tow, carrier-takeoff and carrier-landing walkthroughs.
- Specialized previews are instructional only today; automatic first-use mission triggering and telemetry-driven mission-step completion wait on Jobs, specialized mission logic, live checklist and input-binding systems.
- Local interactive/visual acceptance of the tutorial overlay is still open, so MBL-03 remains active.

## Reusable WinUI design system

- `src/OpenCareer.App/Styles/DesignTokens.xaml` is the production Jet Age palette/brush authority used by WinUI.
- `src/OpenCareer.App/Styles/ComponentStyles.xaml` provides reusable typography, paper/metal panels, buttons, form controls, toggles, tabs, lists, grid/ledger rows and semantic status styles.
- Dashboard, Current Flight, Settings, placeholders, shell status surfaces and the tutorial overlay consume the shared resources.
- Standard WinUI control templates remain intact so native focus, keyboard and high-contrast behavior are preserved rather than replaced by custom templates.
- `UiDesignSystemContractTests` verifies the vivid ivory/cool shell palette, WCAG-style text contrast, required component keys and duplicate-resource protection.
- Variant-scoped Primary/Secondary/Danger state resources preserve the stock WinUI Button template. Windows build and exact-PC normal/hover readability are accepted. MBL-02 remains open for keyboard-focus, high-contrast, text-scaling and broader visual/accessibility acceptance.

## Settings and diagnostics implementation

- `OpenCareer.Application/Settings` defines non-authoritative UI/application preferences and the persistence boundary.
- `JsonAppSettingsService` stores a versioned `settings.json` under the OpenCareer local-app-data root using atomic temp-file replacement.
- Measurement units are functional today: Aviation uses ft/kt/lb and Metric uses m/km/h/kg; changing the setting reformats the existing live telemetry snapshot immediately.
- Persisted future-facing preferences include input-hint order, **Show checklist every flight**, automatic tutorial offers, reduced motion and the offline-first optional-online-services permission gate.
- Settings exposes live simulator connection state/issue, simulator and SimConnect versions when reported, telemetry freshness, last sample time and pause/slew/on-ground/airborne state.
- OpenCareer now writes an application log under `Logs/opencareer.log` with bounded rotation.
- Diagnostic export creates a ZIP with environment, connection state, non-coordinate telemetry diagnostics, preferences and local logs/settings. Exact aircraft latitude/longitude are intentionally excluded.
- Current local-data backup creates a ZIP plus manifest while excluding backup/export recursion. The shared SQLite database is captured through SQLite backup semantics, so current Logbook and military-campaign data are internally consistent even with WAL enabled.
- A validated SQLite restore backend stages a backup archive first and applies it on the next app startup with rollback protection; Settings still needs the user-facing archive picker/confirmation.
- MBL-07 now provides authoritative SQLite FlightSession checkpoint/recovery for the supported single-session path. True multi-leg/FlightLeg runtime persistence and previous-valid corruption fallback remain open.
- **Do not treat input-hint preference as binding discovery.** MBL-05 owns actual controller/keyboard profile resolution.
- MBL-23 is code-complete and Windows-built but remains unremoved until local interactive persistence/backup/export/accessibility verification. Its recovery-status copy still needs reconciliation with the now-implemented FlightSession checkpoint path.

## Production Dashboard implementation

- `OpenCareer.Application/Dashboard` defines stable Dashboard snapshot contracts, opportunity tiers, company-employment status, guidance targets, a deterministic primary-guidance selector and Top Opportunities ranking.
- `DashboardOpportunitySelector` returns at most four **available** jobs, ordering by tier -> fit -> estimated net -> reposition distance -> stable ID.
- Tiers are fixed as Green **Standard**, Blue **Specialist**, Purple **Elite**, Orange/Gold **Legendary**. The UI renders the tier name as well as color.
- `DashboardViewModel` owns presentation/routing logic and consumes `IDashboardSnapshotSource`; no Jobs/Career/Company/Economy values are fabricated while those systems are unavailable.
- The Dashboard now includes a first-class Active Operation panel for accepted work, stage, checklist requirement, contract economics, blockers and the next routed action.
- Snapshot guidance is centralized in `DashboardGuidanceEngine`; active-operation, maintenance, company probation/suspension/termination and jobs guidance are domain-tested.
- Home refreshes its snapshot every 10 seconds while visible, skips overlapping refreshes and cancels cleanly when navigating away.
- The production Home layout contains split flight/career hero, daily P/L header, dynamic recommendation, Top Opportunities, career Level/XP/licenses/hours/owned count/next/recent, company rank/standing/employment state, aircraft readiness/location/distance, finance summary, recent activity, condensed world activity and searchable OpenCareer Network feed.
- KRME was removed as a hard-coded production Home base. KRME/F-22 remains a developer validation fixture only.
- Career Level + XP are now accepted as meta-progression but cannot bypass licenses/ratings, employer standing, military authorization, capability, affordability or dispatch/safety requirements.
- Company employment is required to support deterministic rank/standing plus probation, demotion, suspension and firing/termination. Normal safety choices and one routine rough landing are not arbitrary termination triggers; other-employer/independent recovery paths remain available.
- OpenCareer Network is an in-world simulated feed generated from structured career/world events. It is searchable by area/airport/company/text. Optional AI may phrase posts but cannot authoritatively create money, mission outcomes or reputation changes.
- The temporary `UnavailableDashboardSnapshotSource` still returns an empty snapshot even though Jobs/Career/Fleet/Economy/Logbook authorities now exist. A later bounded MBL-01 slice must project those available sources without fabricating Company/World data.
- MBL-01 remains active until Jobs/Career/Company/Aircraft/Economy/World integrations and local visual/runtime acceptance are complete. Current public-repo CI passes Windows WinUI/live-probe and Linux/SimLab/unit-test validation.

## Production Logbook / Debrief implementation

- MBL-12 is **IN PROGRESS**.
- `OpenCareer.Domain/Logbook` now defines frozen postflight snapshots rather than recalculating historical flights from future tuning.
- Debriefs preserve FlightSession -> FlightLeg hierarchy, planned/actual route identity, decimated multi-leg route-track points, independent time/experience dimensions, aircraft identity, fuel, payload, assistance/route-integrity flags, landing episodes, incidents/events, evidence quality, separate safety/mission outcomes and authoritative settlement references.
- Unknown landing/runway/fuel/payload evidence remains unavailable; the Logbook never manufactures legal-style credit or mission proof.
- `LogbookStatisticsCalculator` aggregates committed records only.
- `LogbookQuery` / `LogbookQueryMatcher` define search and type/outcome/date filters used by the production SQLite implementation.
- `LogbookCommitCoordinator` provides an exactly-once application boundary. Career entries derive idempotency from the authoritative settlement key; manual free/practice entries use the debrief id. A career entry cannot be committed while settlement is pending.
- The top-level Logbook navigation destination is now a production WinUI page rather than a placeholder. It has committed-flight history, aggregate totals, search/filter controls, selected debrief details, flight-leg hierarchy, landing/event evidence and a projected multi-leg route-track schematic.
- Production now uses `SqliteLogbookStore` from the new `OpenCareer.Infrastructure` project. `opencareer.db` is created under the existing local-data root, migrated with `PRAGMA user_version`, uses WAL mode, stores immutable versioned debrief JSON plus indexed query fields, and enforces unique idempotency keys.
- SQLite integration tests cover round-trip reload across store instances, indexed search/filter behavior, unknown lookup, concurrent retry collapse and idempotency collision rejection.
- Supported career completion now projects the terminal FlightSession into a debrief, commits it after authoritative settlement, applies experience and travel/location markers, releases the Fleet reservation and clears the terminal checkpoint. Settlement-derived idempotency prevents duplicate ledger/Logbook effects across replay and service reconstruction.
- Remaining integration: manual free/practice **Log Flight / Discard**, true multi-leg projection, richer quantitative landing/event evidence, automatic restart resumption after a process exit midway through terminal processing, and local visual acceptance.

## CI cost controls

- Feature-branch pushes no longer run the same CI that an open pull request already runs.
- Documentation-only changes do not trigger build workflows.
- Feature-branch pushes do not duplicate open-PR validation.
- Documentation-only updates are excluded from full code builds; a lightweight changed-files gate also prevents an open PR's cumulative diff from forcing irrelevant full validation.
- Linux validates relevant domain/application/test changes; Windows validates WinUI/Application/Domain/SimConnect/live-probe changes.
- Public-repository standard hosted runners are active, so the former private-repository minute gate no longer blocks jobs before step 1.
- Relevant Windows and Linux validation run throughout the draft PR; no merge to main occurs without explicit user approval.

## Chapter 2 shell and simulator boundary

`src/OpenCareer.App` contains:

- unpackaged .NET 10 WinUI 3 project pinned to `Microsoft.WindowsAppSDK 2.4.0`,
- application resources and DI/logging startup,
- `NavigationView` shell,
- observable simulator connection and telemetry ViewModel,
- dynamic production Dashboard shell with honest empty states and no hard-coded production home base,
- Current Flight view that projects the authoritative active/recovered career FlightSession and exposes completion or confirmed abandon when eligible,
- placeholders for later sections,
- Windows GitHub Actions build workflow.

The shell starts the simulator service, refreshes immutable connection/telemetry snapshots on its dispatcher and awaits asynchronous cleanup on window close. A connection or telemetry snapshot alone never creates or claims an active FlightSession.

## Connection and telemetry boundary implemented

- `src/OpenCareer.Application/Simulator`: `ISimulatorConnection`, `ISimulatorTelemetrySource` and immutable status/identity boundary types. The application layer references normalized Domain telemetry, not SimConnect SDK types.
- `src/OpenCareer.SimConnect`: documented native calls through P/Invoke, all owned by one dedicated worker. Open acknowledgement, data-definition setup, user-aircraft subscription, pause event subscription, dispatch, liveness requests and close remain serialized off the UI thread.
- Correlated `RequestSystemState("Sim")` checks liveness every five seconds, with a 30-second response timeout. Menus are valid responses. Empty dispatch is not proof of a disconnect.
- On `SIMCONNECT_RECV_OPEN`, the worker installs the normalized telemetry definition for both the existing 1 Hz `Latest` stream and a bounded ordered SimFrame flight-critical stream, then subscribes to `Pause_EX1`. Setup failure prevents a false Connected state.
- First normalized snapshot uses the existing `Domain/Telemetry/AircraftTelemetrySnapshot`: position, MSL/AGL altitude, IAS/ground speed, vertical speed, true heading, pitch/bank/G, on-ground, parking brake, engine count/running state, fuel, derived payload, flaps, gear, pause and slew.
- SDK-specific normalization is contained in `OpenCareer.SimConnect`: feet/second vertical speed becomes feet/minute, headings are normalized, SimConnect booleans treat any nonzero value as true, flaps are converted to 0–100%, and payload is conservatively derived as `max(0, total weight - empty weight - fuel weight)`.
- Telemetry is cleared when a connection/session ends so stale aircraft data cannot survive a disconnect.
- Tests inject native-call results/raw callback buffers; they verify mapping, packet validation, worker ownership, pause handling, monotonic bounded critical buffering, stale-data clearing, facility-request isolation and cooldown/coalescing. Exact-PC validation supplies the native C172/KJFK evidence; tests do not generalize that result to other aircraft/add-ons.
- Runtime: supply the installed MSFS 2024 SDK's x64 `SimConnect.dll` through `MSFS2024_SDK` or `-p:SimConnectNativePath=...`. It is copied beside the app, never committed. A build without the DLL launches with connection unavailable; with the DLL and MSFS closed it should show Waiting for MSFS.
- Exact setup, SDK references, telemetry definitions and remaining Windows live checks: `docs/simulator-connection.md`.
- `tools/OpenCareer.LiveProbe` reuses the production `SimConnectConnection` and writes connection/telemetry JSONL traces; `tools/run-live-probe.ps1` resolves the SDK DLL and launches it. This is validation tooling only and does not implement flight state.
- `tools/OpenCareer.TraceAnalysis` reads those captures on Windows/Linux without the SDK. It reports connection/pause/clearing observations, field ranges, snapshot cadence and line-numbered integrity findings. Tests are synthetic; analysis changes no flight state, certifies no live behavior and calibrates no thresholds.

## Material limits

The accepted live result is deliberately narrow: stock C172, KJFK local Development/Test, full-stop landing with bounce, and zero configured progression. It does not prove arbitrary aircraft/add-on calibration, telemetry-derived touch-and-go/go-around, active-session aircraft-change rejection, simulation-rate/night/instrument credit, long-duration replay, ordinary paid-job economics, broad registry coverage or every airport facility payload. Current production job authority is Ferry/Reposition; generation still uses a small fixed destination set and baseline demand. Civilian reputation/relationship application and automatic startup resumption from a partially completed terminal chain remain unimplemented. Loan/dealer/manual-ground outputs remain quotes until their own authoritative persistence and settlement exist.

## Next bounded work

1. Bind the authoritative current canonical aircraft identity to the active FlightSession runtime and fail closed on a loaded-aircraft mismatch without deriving ownership or Airframe identity from TITLE.
2. Add telemetry-derived touch-and-go/go-around/new-approach evidence with focused replay and later live validation.
3. Persist/reconstruct terminal workflow intent so restart can resume safely between the existing exactly-once settlement, Logbook, experience/location, reservation-release and checkpoint-clear steps.
4. Live-certify one ordinary compensated market-generated Ferry/Reposition route through the existing production authorities.
5. Expand registry/capability/job breadth; do not rebuild the validated evidence, checkpoint, dispatch or settlement foundations.

Do **not** expand finance complexity before the playable flight foundation unless explicitly requested.

## Detail only when needed

- `docs/development-master-checklist.md` — live master roadmap/checklist and progress graphics.
- `docs/conflict-system.md` — deterministic military/conflict implementation boundary and current remaining work.
- `docs/ui-concept.md` — short UI direction and level policy.
- `docs/ui-design-system.md` — canonical shell, tokens, layout/adaptive/accessibility rules.
- `docs/ui-screen-spec.md` — target UX for all 15 current navigation destinations, including Conflict Operations.
- `docs/career-foundation-decisions.md` — accepted gameplay/base decisions.
- `docs/credit-and-dealers.md` — finance/dealer model.
- `docs/msfs-sdk-strategy.md` — verified SDK boundaries.
- `docs/simulator-connection.md` — implemented connection/telemetry boundary, runtime setup and pending live checks.
- `tools/OpenCareer.TraceAnalysis/README.md` — offline trace report, invocation, exit codes and limits.
- `docs/flight-session-design.md` — accepted load/flight/leg/time/landing/resume/free-flight/postflight rules for Chapter 4, including pilot-log dimensions, route/diversion evidence and adaptive telemetry.
- `docs/flight-system-research-2026-09-17.md` — cross-plugin/real-world design review and cloud/AI boundary decisions.
- `docs/efb-integration.md` — optional in-simulator EFB companion architecture using the official MSFS 2024 EFB + CommBus APIs.
- `docs/cargo-market-requirements.md` — accepted named-commodity/market-value requirements for later cargo generation.
- `docs/simulation-model.md` — deterministic simulation invariants.
- `docs/development-workflow.md` — chapter order/exit gates.
- `docs/current-review.md` — latest review findings before Chapter 2.
