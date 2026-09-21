# Instruction alignment review — 2026-09-17

## Scope and governing decisions

Read the complete user-supplied `Pasted markdown.md` (1,646 lines) and the current repository `AGENTS.md`. Reviewed branch `feature/m1-simulation-core` at `f6d2e1a944c76334a814a8307571557172f316f9`. That commit only adds AGENTS.md on top of the tested corrections at `0659456`; it does not change application code.

AGENTS.md already adapts the generic instructions to OpenCareer. Retain the specific WinUI 3/.NET 10/x64 choice over the generic WPF default, and qualification/reputation/capital progression over the optional XP/level examples. The generic list of possible features is not a requirement to implement all of them now. Keep optional AI and public-company systems out of the critical path.

The main divergence was the previous development backlog: it prioritized jobs and settlement before the simulator/flight-session foundation. The backlog and README now follow AGENTS.md sections 31–32. Existing simulation code is retained and tested; further gameplay expansion follows the connected foundation.

## Traceability

“Aligned” means the implemented portion follows the instruction. “Pending” means an implementation or runtime acceptance gate does not yet exist; an architecture note is not proof it works.

| AGENTS sections | Requirement | Evidence / current state | Action |
| --- | --- | --- | --- |
| 1–5, 35 | Product identity, priorities and explicit choices | C# net10.0 domain, OpenCareer namespaces, independent single-player simulation. WinUI 3 is selected but not built. | Retain WinUI 3, SQLite, offline core, no XP grind; reliability/integration/data integrity lead feature expansion. |
| 6 | Separate UI/application/domain/infrastructure/simulator layers | Only Domain and SimLab projects exist. No UI or raw SimConnect coupling has been introduced. | Create layers as the foundation needs them; do not add empty projects or redundant interfaces for appearance. |
| 7–8 | Isolated serialized SimConnect; responsive UI; bounded handoff | SDK strategy is aligned. Connection, reconnect, callback ownership, cancellation, disposal and UI dispatch are pending. | FND-01/02; establish single-owner client, safe shutdown and mockable source. |
| 9 | Normalized aircraft identity, sim rate and missing-value handling | Existing AircraftTelemetrySnapshot uses required numeric/bool values and lacks aircraft identity and simulator rate. | FND-03 must represent unknown/stale/unsupported values explicitly before live data drives game rules. Missing is not zero/false. |
| 10–11 | Robust state machines and recoverable FlightSession | FlightOperationState is an enum, not a detector. No persisted active FlightSession exists. | FND-04/05; hysteresis, multiple signals, bounce/noise, pause/slew, duplicate samples and restart recovery. |
| 12 | Meaningful progression; employment remains viable | No implemented XP gates. Reputation/career fields and service tracks exist. | Preserve viable long-term employment; first-aircraft pacing is optional calibration, not a compulsory ownership path. |
| 13 | Deterministic economy outside UI; temporary/structural separation | WorldSimulation/MarketTickEngine and regression assertions cover replay, closure and investment guards. | Retain; actual money ledger, company finance, inflation and fleet acquisition remain pending. |
| 14 | Structured contracts and validated completion | JobContract has eligibility/evidence/deadline/chronology guards. Evidence is supplied by callers. | Telemetry/session and dispatch services must establish evidence. Landing alone or a user checkbox cannot authorize completion/payment. |
| 15–16 | Installed aircraft capabilities and feasible dispatch | Capability matching exists; discovery, scenery facilities, performance provenance and joint fuel/payload feasibility do not. | Registry and route/runway/performance validation precede offered jobs. Test C208 is a fixture, not a whitelist. |
| 17 | Scoped events, meaningful consequences and rare extremes | Scope, timing, sector targeting and provisional ordinary opportunities exist. Shared global coordination, cascades and most operational consequences remain pending. | Preserve fictional event framing; validate geography, seasonality and total-world rarity before scaling. |
| 18 | Gradual wear separate from discrete damage; evidence-based hazards | No wear/damage engine exists. Hazard fields alone are not such an engine. | Add component history, maintenance and multi-signal damage after integration; avoid flat catastrophic RNG or FPM-only damage. |
| 19 | SQLite, migrations, atomic updates, backups | Serializable versioned simulation checkpoint and replay tests exist; actual persistent storage does not. | FND-05 owns crash-safe flight/session saves; later ledger transactions enforce exactly-once settlement. |
| 20 | Polished, understandable UI | No application UI exists. | FND-01 shows connection and current flight clearly; UI refresh is independent of telemetry cadence. |
| 21 | Optional AI; application controls authoritative state | Current core has no LLM/network dependency. | Preserve local fallbacks; validate optional AI output, never let it set balances or completion. |
| 22 | Graceful errors and structured logging | Domain validation rejects invalid inputs; no host exists to translate failures into recoverable UX. | Application handles expected connection/save/unsupported-data failures, structured logs, no per-frame logging. |
| 23–26 | Focused code, complete implementations, review and clear compatibility | Implemented correction methods are complete; incomplete systems are documented instead of hidden stubs. Public contract APIs changed in the prior correction. | Explain compatibility below; keep subsequent integration, persistence and UI commits focused. |
| 27 | Deterministic tests and mocks | SimLab has 29 passing regression scenarios. OpenCareer.Tests/xUnit and simulator replay fixtures are absent. | Keep SimLab for stress paths; add xUnit with the foundation and test detector, reconnect, missing telemetry and save recovery. |
| 28–29 | Official docs; external process; minimal optional in-sim bridge | Official SDK confirms external managed clients and non-thread-safe SimConnect. No EFB integration is implemented. | Verify selected SDK/.NET/WinUI deployment on Windows. EFB work stays later with .PLN fallback and Package Tool ownership. |
| 30 | Feature branch, focused changes, honest CI evidence | Draft PR #2 and Linux core CI exist. Prior correction build had zero warnings/errors and 29 passing scenarios. | Add Windows build/runtime evidence for Windows projects; Linux green status does not prove WinUI or SimConnect operation. |
| 31–32 | Connected, recoverable flight foundation before further gameplay | Previous jobs-first backlog conflicted with this order. No real-simulator MVP gate has passed. | Corrected to FND-01 through FND-06, then registry/dispatch/jobs/settlement. |
| 33–36 | Feature acceptance, continuity, incremental maintainability | AGENTS.md defines the feature checklist and conversational commands; existing project retained. | For each feature document player flow, inputs, failure/restart behavior, tests and done criteria before a substantial implementation. |

## Foundation completion gate

The next milestone passes only with evidence that the application:

1. Starts before MSFS and shows a waiting/disconnected state without crashing.
2. Connects when MSFS opens and shows aircraft identity plus valid normalized live telemetry.
3. Tolerates unsupported/missing values without manufacturing takeoff, landing, engine or crash events.
4. Detects flight/ground transitions using multiple signals and tracks one FlightSession.
5. Saves a flight locally and restores an active session after application interruption.
6. Handles simulator shutdown and reconnect without duplicate subscriptions or losing the active operation.
7. Keeps UI responsive, callbacks serialized and telemetry queues bounded, with clean cancellation/disposal.
8. Has xUnit/mock/replay tests plus Windows build and actual simulator acceptance evidence; the existing economy suite remains green.

The currently available Linux simulation tests prove none of the live simulator/UI/storage items by themselves.

## Compatibility notes from the previous correction

- JobContract.Accept/Start now require ContractDispatchContext; Complete requires explicit time and flight verification; Expire requires time.
- Contract lifecycle now records acceptance/departure/completion timestamps. Constructing an in-progress contract without consistent timestamps is rejected.
- Production career time advances through WorldSimulation.Advance. Calling a low-level market integrator on UI refresh is not supported as an authoritative clock.
- Simulation checkpoint schema is explicitly versioned, but there is no production save migration service yet. Older prototype structures must not be silently treated as current saves.
- No database migration or package change was made by the correction because no database implementation existed.
- This alignment change edits documentation only: no new runtime dependency, API change or data migration.

## Evidence and limits

The prior correction's [CI run](https://github.com/dhavalpddn-eng/OpenCareer/actions/runs/35188606036) passed all 29 scenarios. The intervening AGENTS.md commit changes instructions only. This review does not relabel unimplemented systems as complete or claim fresh MSFS testing.

Official [SimConnect SDK guidance](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/SimConnect_SDK.htm), rechecked through Context7, supports the external managed-client boundary and serialized access. Wolfram remains useful for bounded mathematical checks when equations change; it is not evidence of live SDK behavior. No equations were changed in this alignment review.

The six outstanding gameplay questions in the backlog remain useful for later balance. They do not block the immediate integration foundation.
