# OpenCareer project state

**Ultra-fast resume:** read root `ASTRA.md` first. This file is the detailed handoff.

Updated: 2026-09-20.

## Resume here

- Repo: `dhavalpddn-eng/OpenCareer`
- Branch: `feature/m1-simulation-core`; draft PR #2. Keep `main` stable.
- Parent M1 integration head at this slice branch point: `92990f438a3155efc0594f38e39f9199fb41f32a`, containing the completed FlightSession recovery/tracking/live-validation work from PR #14.
- WinUI 3 shell, resilient SimConnect connection/reconnect, normalized telemetry, `FlightTelemetryEvidenceProcessor`, persistent/recoverable SQLite `FlightSession`, continuity protection, Current Flight integration, tutorial evidence and logbook/debrief projection foundations are implemented. Real MSFS 2024 validation completed the full tested lifecycle through clean shutdown; do not repeat it unless new changes affect that area.
- [Windows CI run 35299270135](https://github.com/dhavalpddn-eng/OpenCareer/actions/runs/35299270135): WinUI x64 and live-probe Release builds passed with 0 warnings/errors; **104/104 xUnit tests passed**, including the compiled trace analyzer.
- [Linux CI run 35299270186](https://github.com/dhavalpddn-eng/OpenCareer/actions/runs/35299270186): **104/104 xUnit + 29/29 SimLab** passed. Local Release verification also passed those gates plus analyzer CLI report/exit-code/input-preservation checks.
- Tutorial-engine CI at `f6b1cc7`: **118/118 xUnit + 29/29 SimLab passed**, and the Windows WinUI app/live-probe build passed. Automated UI compilation does not replace local interactive/visual acceptance or live MSFS verification.
- Master development tracker: `docs/development-master-checklist.md`; it contains the live chapter/subsystem checkboxes and Mermaid progress graphics. Update it whenever verified implementation status changes.
- UI visual language: `docs/ui-design-language.md` is canonical for palette/material/aesthetic; implementation tokens/layout remain in `docs/ui-design-system.md`, and screen behavior in `docs/ui-screen-spec.md`; visual references: `docs/assets/opencareer-dashboard-concept-v2.svg`, `docs/assets/opencareer-ui-screen-atlas.svg`, and `docs/assets/opencareer-conflict-operations.svg`. `docs/ui-concept.md` remains the short visual-direction entry point.
- Verify remote branch head before edits because other chats may change it.

## Fixed direction

Single-player, offline-first MSFS 2024 companion. C#/.NET 10, Windows x64, WinUI 3/Windows App SDK, isolated SimConnect boundary, SQLite, deterministic/testable domain. No aircraft whitelist. AI is optional and never authoritative for money, ownership, mission completion or critical state.
- UI identity is fixed as a retro-modern aviation operations interface: dark blue-gray riveted structure, vivid ivory briefing panels, realistic MSFS 2024 imagery, restrained navy/brass/status accents, and 1950s/60s aviation character without sepia/olive drift. See `docs/ui-design-language.md`.

## Confirmed gameplay

- First live test hub: **KRME Griffiss International**; mixed civilian/government/defense/UAS context. Do not invent a resident fighter wing or real-world missions that are not documented.
- **F-22/KRME is only the first developer live-telemetry validation fixture. It is not the required career start.** A standard new career starts from scratch with no owned aircraft and no automatic military access. Early progression may use training, employer, rental or assigned aircraft appropriate to qualifications; ownership and military access are earned separately.
- First owned used light aircraft should become reasonably attainable around **50–80 real flying hours** through actual savings/financing, not an hour unlock.
- Typical sessions 1–3 hours; support up to ~6 and recover interrupted sessions where technically possible.
- Home airport, local route connections, relationships, hangar/storage and additional bases matter; no teleporting the company around the map.
- Bankruptcy is possible but should usually require sustained bad decisions; employee work remains a recovery path.
- Management is mostly automatic. Eligible manual ground procedures may provide modest bounded rewards.
- Military-heavy airports produce much more military/government work while retaining appropriate civilian work.
- Military/rented/employer/assigned aircraft are distinct from owned aircraft.
- **Levels may be added later as meaningful meta-progression.** They may summarize experience or unlock convenience/prestige, but never bypass licenses/ratings, reputation, military authorization, aircraft capability, affordability or dispatch rules.
- Flight-session rules are now specified in `docs/flight-session-design.md`: tracking begins after aircraft load/stable telemetry, real-world-style movement time is distinct from load time, one session may contain multiple legs, bounce recontacts are one landing episode, touch-and-go is recorded distinctly from full-stop, safe diversions/go-arounds are not automatic failures, and conventional jobs default to destination parking + shutdown unless the mission defines another terminal condition.
- Free/practice flights can be logged for pilot experience. Career-credit hours are distinct from accelerated simulated time so time acceleration remains usable without multiplying license/aircraft-experience progression. Material teleport/slew displacement and aircraft changes cannot advance normal career jobs.
- Cargo requirements are now specified in `docs/cargo-market-requirements.md`: generated cargo work carries named commodity lots (coffee, phones, televisions, food, plants and an extensible catalog) with quantity, physical properties, declared/market value and origin/destination economics rather than anonymous cargo weight.
- Onboarding is layered: a general app tutorial, a first-job tutorial, and versioned first-time mission-family tutorials for specialized procedures. Unique missions such as banner pickup and carrier takeoff/landing receive their own live guided checklists with Controller/Keyboard bindings and telemetry-driven completion only where the state can be verified. See `docs/intro-tutorial.md` and `docs/mission-tutorial-system.md`.
- 2026-09-17 cross-plugin flight review is recorded in `docs/flight-system-research-2026-09-17.md`. It strengthens Chapter 4 with pilot-log dimensions (day/night/cross-country/instrument/event attributes), planned-vs-actual route/diversion evidence, evidence provenance/confidence and bounded adaptive landing telemetry. External cloud/AI services remain optional and non-authoritative.

## Implemented domain foundation

- Deterministic economy/world events, replay/checkpoints, contract lifecycle/dispatch guards.
- Pure Chapter 4 flight reducer foundation plus the production `FlightTelemetryEvidenceProcessor` are implemented and tested. The real F-22/KRME validation exercised the complete state path through shutdown/complete with one takeoff and one landing episode and no interruption; thresholds remain configurable because aircraft telemetry behavior can differ.
- Location/home-base connection rules and travel-history guards.
- Protected-absence policy, session preferences, bankruptcy stages and bounded manual-ground reward quotes.
- Career-derived credit model, fictional lenders, affordability/debt-service checks.
- Fictional aircraft dealers, seeded offers/discounts, stock validation and cash/finance eligibility.
- The merged FlightSession head passed the final Release xUnit suite and Windows x64 Release WinUI build. Older test-count snapshots in historical sections are not the current acceptance authority.

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
- MBL-02 is not removed yet: Windows XAML build verification and local interactive visual/accessibility acceptance remain. Current GitHub Actions jobs are failing before any runner step starts and therefore do not provide a compiler/test result.

## Settings and diagnostics implementation

- `OpenCareer.Application/Settings` defines non-authoritative UI/application preferences and the persistence boundary.
- `JsonAppSettingsService` stores a versioned `settings.json` under the OpenCareer local-app-data root using atomic temp-file replacement.
- Measurement units are functional today: Aviation uses ft/kt/lb and Metric uses m/km/h/kg; changing the setting reformats the existing live telemetry snapshot immediately.
- Persisted future-facing preferences include input-hint order, **Show checklist every flight**, automatic tutorial offers, reduced motion and the offline-first optional-online-services permission gate.
- Settings exposes live simulator connection state/issue, simulator and SimConnect versions when reported, telemetry freshness, last sample time and pause/slew/on-ground/airborne state.
- OpenCareer now writes an application log under `Logs/opencareer.log` with bounded rotation.
- Diagnostic export creates a ZIP with environment, connection state, non-coordinate telemetry diagnostics, preferences and local logs/settings. Exact aircraft latitude/longitude are intentionally excluded.
- Current local-data backup creates a ZIP plus manifest while excluding backup/export recursion. It is suitable for currently implemented settings/tutorial/log data.
- FlightSession checkpoint/recovery is now authoritative and SQLite-backed. The existing Settings backup feature still must not be described as a transactionally consistent whole-career database backup until that separate backup integration is implemented.
- **Do not treat input-hint preference as binding discovery.** MBL-05 owns actual controller/keyboard profile resolution.
- MBL-23 is code-complete but remains unremoved until Windows build and local interactive persistence/backup/export verification are available. GitHub Actions currently fails before runner steps are created, so those failures are not compiler/test evidence.

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
- The temporary `UnavailableDashboardSnapshotSource` intentionally returns an empty snapshot. Later authoritative systems replace/wrap it without changing the Home page contract.
- MBL-01 remains active until Jobs/Career/Company/Aircraft/Economy/World integrations and local visual/runtime acceptance are complete. Current public-repo CI passes Windows WinUI/live-probe and Linux/SimLab/unit-test validation.

## Production Logbook / Debrief implementation

- MBL-12 is **IN PROGRESS**.
- `OpenCareer.Domain/Logbook` now defines frozen postflight snapshots rather than recalculating historical flights from future tuning.
- Debriefs preserve FlightSession -> FlightLeg hierarchy, planned/actual route identity, decimated multi-leg route-track points, independent time/experience dimensions, aircraft identity, fuel, payload, assistance/route-integrity flags, landing episodes, incidents/events, evidence quality, separate safety/mission outcomes and authoritative settlement references.
- Unknown landing/runway/fuel/payload evidence remains unavailable; the Logbook never manufactures legal-style credit or mission proof.
- `LogbookStatisticsCalculator` aggregates committed records only.
- `LogbookQuery` / `LogbookQueryMatcher` define search and type/outcome/date filters for the eventual SQLite implementation.
- `LogbookCommitCoordinator` provides an exactly-once application boundary. Career entries derive idempotency from the authoritative settlement key; manual free/practice entries use the debrief id. A career entry cannot be committed while settlement is pending.
- The top-level Logbook navigation destination is now a production WinUI page rather than a placeholder. It has committed-flight history, aggregate totals, search/filter controls, selected debrief details, flight-leg hierarchy, landing/event evidence and a projected multi-leg route-track schematic.
- Production now uses `SqliteLogbookStore` from the new `OpenCareer.Infrastructure` project. `opencareer.db` is created under the existing local-data root, migrated with `PRAGMA user_version`, uses WAL mode, stores immutable versioned debrief JSON plus indexed query fields, and enforces unique idempotency keys.
- SQLite integration tests cover round-trip reload across store instances, indexed search/filter behavior, unknown lookup, concurrent retry collapse and idempotency collision rejection.
- Remaining integration: completed FlightSession -> debrief mapping, career settlement -> commit, free/practice Log Flight/Discard, real persisted route/landing/fuel/payload evidence, database-consistent backup/recovery with MBL-07, and local visual acceptance.
- SQLite persistence head `6319492` is Linux-green: 177/177 xUnit/integration tests + 29/29 SimLab. The SQLite-enabled WinUI app and native dependency restored and built successfully on Windows on the immediately preceding code head; the final assertion-only commit did not require another Windows build.

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
- Current Flight view that displays telemetry while explicitly remaining **No active flight**,
- placeholders for later sections,
- Windows GitHub Actions build workflow.

The shell starts the simulator service, refreshes immutable connection/telemetry snapshots on its dispatcher and awaits asynchronous cleanup on window close. A connection or telemetry snapshot alone never creates or claims an active FlightSession.

## Connection and telemetry boundary implemented

- `src/OpenCareer.Application/Simulator`: `ISimulatorConnection`, `ISimulatorTelemetrySource` and immutable status/identity boundary types. The application layer references normalized Domain telemetry, not SimConnect SDK types.
- `src/OpenCareer.SimConnect`: documented native calls through P/Invoke, all owned by one dedicated worker. Open acknowledgement, data-definition setup, user-aircraft subscription, pause event subscription, dispatch, liveness requests and close remain serialized off the UI thread.
- Correlated `RequestSystemState("Sim")` checks liveness every five seconds, with a 30-second response timeout. Menus are valid responses. Empty dispatch is not proof of a disconnect.
- On `SIMCONNECT_RECV_OPEN`, the worker installs one 1 Hz numeric telemetry definition and subscribes to `Pause_EX1`. Setup failure prevents a false Connected state.
- First normalized snapshot uses the existing `Domain/Telemetry/AircraftTelemetrySnapshot`: position, MSL/AGL altitude, IAS/ground speed, vertical speed, true heading, pitch/bank/G, on-ground, parking brake, engine count/running state, fuel, derived payload, flaps, gear, pause and slew.
- SDK-specific normalization is contained in `OpenCareer.SimConnect`: feet/second vertical speed becomes feet/minute, headings are normalized, SimConnect booleans treat any nonzero value as true, flaps are converted to 0–100%, and payload is conservatively derived as `max(0, total weight - empty weight - fuel weight)`.
- Telemetry is cleared when a connection/session ends so stale aircraft data cannot survive a disconnect.
- Tests inject native-call results/raw callback buffers; they verify mapping, packet validation, worker ownership, pause handling and stale-data clearing. Native DLL execution is **not** tested by these mocks.
- Runtime: supply the installed MSFS 2024 SDK's x64 `SimConnect.dll` through `MSFS2024_SDK` or `-p:SimConnectNativePath=...`. It is copied beside the app, never committed. A build without the DLL launches with connection unavailable; with the DLL and MSFS closed it should show Waiting for MSFS.
- Exact setup, SDK references, telemetry definitions and remaining Windows live checks: `docs/simulator-connection.md`.
- `tools/OpenCareer.LiveProbe` reuses the production `SimConnectConnection` and writes connection/telemetry JSONL traces; `tools/run-live-probe.ps1` resolves the SDK DLL and launches it. This is validation tooling only and does not implement flight state.
- `tools/OpenCareer.TraceAnalysis` reads those captures on Windows/Linux without the SDK. It reports connection/pause/clearing observations, field ranges, snapshot cadence and line-numbered integrity findings. Tests are synthetic; analysis changes no flight state, certifies no live behavior and calibrates no thresholds.

## Material limits

The first installed-aircraft discovery adapter is implemented using the official MSFS 2024 `SimConnect_EnumerateSimObjectsAndLiveries` API on the existing serialized SimConnect worker. It discovers spawnable user-selectable aircraft titles/liveries, collapses livery variants to one exact title-based canonical identity, marks those identities installed, and becomes unavailable on disconnect. It intentionally supplies no fabricated capability/performance values. Airport/runway source adapters, aircraft capability enrichment, full operation-specific dispatch performance, and market-driven Jobs generation are not implemented yet. Registry/runway Slice 1 provides provider-neutral physical records and tri-state compatibility. Slice 2 provides deterministic multi-provider registry resolution using exact canonical IDs, field-level confidence/provenance, installed-vs-known status and explicit unresolved fields; it does not fuzzy-match aircraft names or fabricate missing performance. External aviation APIs may enrich/reference records but are not gameplay authority. F-22/KRME remains a developer fixture only. The live F-22 showed nearly constant fuel quantity, so generic fuel burn must remain non-authoritative until aircraft-specific fuel telemetry is validated.

## Next bounded work

1. Add capability enrichment for discovered aircraft from verified configuration/reference data while preserving explicit unknown fields.
2. Add the first airport/runway adapter with source provenance and caching behind `IAirportDataSource`.
3. Extend dispatch with operation-specific payload/weight/range/weather/safety-margin planning.
4. Keep qualifications, licenses/ratings and government/military authorization as separate gates that cannot override aircraft or runway incompatibility.
5. Start market-driven Jobs generation only after those dispatch inputs can fail closed.

Do **not** expand finance complexity before the playable flight foundation unless explicitly requested.

## Detail only when needed

- `docs/development-master-checklist.md` — live master roadmap/checklist and progress graphics.
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
