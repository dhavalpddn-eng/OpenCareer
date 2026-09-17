# OpenCareer implementation status and next work

Updated 2026-09-17 after the audit of `99d2dcf`. This is the implementation checklist, not a claim that the complete app exists.

## Corrected in the audit follow-up

- Authoritative hourly UTC world clock. Daily, six-hour, five-minute polling and long catch-up replay the same committed intervals. The sub-hour remainder is retained in the checkpoint.
- Exact event start/end boundaries within committed hours. Dispatch can query `WorldSimulation.GetOperationalEffects` at the requested time without waiting for the next economy tick or consuming RNG.
- Backlog scarcity uses a fixed clearance horizon, not the caller's elapsed interval.
- Permanent capacity investment uses realized service contribution. No service or nonpositive current margin prevents positive growth, even with a previously positive investment signal. A finance freeze also blocks positive expansion.
- Distinct origin/destination airport, region, nation, fleet and global event matching. Global targets are null. The single-market scheduler no longer labels every event DFW.
- Deterministic exponential waiting times per event/scope, checkpointed RNG, and no overlap of the same scheduled event in the same scope. Catalog order and unrelated mission-only events do not alter economic replay.
- Added local charter, regional cargo and repositioning opportunities. Charter and cargo demand changes apply only to their market segments. Frequencies are provisional career-time calibration, not playtested balance.
- Finite-number validation for market/cycle parameters, event effects, aircraft and mission requirements; checkpoint clock and schedule validation.
- Contract acceptance and departure require current eligibility evidence, compatible aircraft/access, valid deadlines, authorization and dispatch feasibility. Airspace/navigation restrictions are rechecked at departure. Lifecycle timestamps prevent completion before departure.
- Completion requires application-supplied flight verification. This is a guard, not a telemetry implementation or a payout ledger.
- Executable regression/stress assertions replace the print-only demonstration. CI fails on scenario failures.

## Remaining work, ordered by dependency

Execution order follows `AGENTS.md` sections 31–32. Complete the FND foundation before expanding jobs, payments or the economy. Retain the corrected simulation and its regression gate. The earlier jobs-first ordering is superseded.

| Priority / ID | Work | Completion criteria and constraints |
| --- | --- | --- |
| P0 / FND-01 | WinUI 3 shell and test structure | Launch a Windows x64 app before MSFS; display Waiting/Disconnected, connection status and an empty Current Flight screen. Keep Views/ViewModels separate from application services. Add OpenCareer.Tests with xUnit for normal tests; retain SimLab for long deterministic scenarios. Introduce DI/logging only at the application boundary. |
| P0 / FND-02 | SimConnect boundary | Dedicated mockable out-of-process service with serialized calls, connection loss, bounded retry/backoff, cancellation, disposal and graceful shutdown. Reopening MSFS reconnects without duplicating subscriptions. No UI-to-SimConnect calls. Verify installed SDK/runtime compatibility before claiming live integration works. |
| P0 / FND-03 | Normalized live telemetry | Show aircraft name and core position/speed/altitude/heading/on-ground data. Add identity, simulation-rate and explicit missing/unsupported/stale-value semantics; the current non-nullable snapshot cannot yet express these. Unknown on-ground/engine values must not be treated as false/zero. Separate telemetry cadence from UI refresh and use bounded handoff. |
| P0 / FND-04 | Flight detector and FlightSession | Implement state transitions using multiple signals, hysteresis and time thresholds. Handle bounce, duplicate/out-of-order samples, pause, slew, aircraft changes, reconnect and incomplete telemetry. Track takeoff/landing/parking/shutdown without treating a landing alone as mission completion. Use replay fixtures and mock telemetry. |
| P0 / FND-05 | Active-session persistence and recovery | SQLite transaction-backed autosave, explicit save/schema version, migrations, previous-valid-save backup and useful recovery errors. Preserve active FlightSession across app or simulator interruption. Parameterized SQL/EF only. A JSON round trip is not crash-safe storage. |
| P0 / FND-06 | Foundation acceptance and Windows validation | Pass every AGENTS.md section 32 criterion: startup before simulator, live connection/telemetry, reconnect, reliable flight states, saved flight, active-session recovery, mockability and independent simulation. Add Windows build/xUnit gates when those projects exist. Capture real MSFS evidence separately from mock tests and Linux CI. |
| P1 / M2-01 | Demand-driven job generation and reservation | Generate feasible employee cargo, charter and ferry/reposition offers from demand and installed-aircraft capabilities. Stable offer IDs; repeat refresh cannot mint demand. Reserve/release quantity and reconcile player deliveries with NPC service so backlog is not fulfilled twice. Show expected duration, wage, costs covered, requirements and why the job exists. |
| P1 / M2-02 | Persistent ledger and settlement | Distinguish customer revenue, employee wages, reimbursements and company money. Settle a completed contract exactly once using a unique contract/settlement key in a SQLite transaction. Replayed completion, crash/retry and reload must not duplicate payment. Failure and cancellation need explicit financial rules. The new lifecycle guards alone do not provide this guarantee. |
| P1 / M2-03 | Local save infrastructure | Persist world checkpoint, contracts, ledger and settings atomically; schema migrations, backups and restore tests. JSON round-trip testing exists, but no SQLite/file-save application service exists. Pin simulation version/config and provide migrations before loading changed models into real careers. |
| P1 / M2-04 | Aircraft discovery and dispatch evidence | Implement installed/flyable aircraft registry using simulator data, reference data and capability overrides. Validate runway, weather, payload/fuel trade-off, range, qualifications and access before offers. Current profiles use independent maxima and cannot prove simultaneous payload/range feasibility. Dispatch booleans must come from services, not unrestricted UI checkboxes. |
| P1 / M2-05 | WinUI 3 employment slice | Existing agreed stack: WinUI 3, Windows App SDK, C#/.NET 10, x64, MVVM/CommunityToolkit, unpackaged. Build a job board, briefing, active job, results, balance and save/load flow around the headless domain. No forced airline track or XP grind. |
| P1 / M2-06 | SimConnect and flight lifecycle | Serialized out-of-process adapter; reconnect, pause, slew, simulator-rate and crash handling; telemetry-derived lifecycle and completion evidence; ground servicing/loading/parking/shutdown/unloading. Distinguish simulator faults from player mistakes. Preserve high-rate landing samples through touchdown, bounded buffers and batched persistence. Measure CPU, memory, callback latency and MSFS frame impact on Windows. Current telemetry types/policy are not a working adapter. |
| P2 / M3-01 | Aircraft ownership economy | New/used inventory, condition/history, acquisition and resale spread, wear, component maintenance, insurance, hangar costs, lease/loan schedules, default/recovery and actual fleet delivery limits. Civilian ownership must stay distinct from government/military access. Aggregate capacity investment is still a proxy, not an aircraft order book or company cash constraint. |
| P2 / M3-02 | Persistent inflation | Shared nominal price index plus separate real sector scarcity. Connect wages, fuel, maintenance and aircraft values; explicitly decide fixed versus indexed contract/loan terms. Avoid applying inflation twice or suppressing demand merely because both wages and prices rose. Existing mean-reverting cost cycles are not inflation. |
| P2 / M3-03 | World coordinator and event consequences | One career-wide event schedule and regional-cycle registry, shared across all markets. Global/national events must not be independently rolled per route. Support multi-region/multi-nation routes and mixed fleets. Add geographic/seasonal eligibility and market-specific disaster responses. Connect service queues, maintenance hazards, financing, aircraft groundings and emergency mission generation; currently only market factors and supplied dispatch effects are consumed. |
| P2 / M3-04 | Qualifications, reputation and relationships | Explicit licenses/ratings and employer trust unlock work; relationships and repeat customers, not XP. Define what experience is credited and when qualifications can be earned; do not substitute `QualificationsVerified` for the progression system. |
| P2 / M4-01 | Competition and company management | Named employers/competitors, fleet and cash constraints, market share, hiring and management choices. Capital/public-company systems only after underlying operating cash flows are validated. |
| P2 / BAL-01 | Engagement and progression calibration | Run employee-to-owner career simulations and playtests across short and long sessions. Measure time to first meaningful purchase, job diversity, dominant money/hour strategies, downside/recovery, empty boards, event visibility and recurring costs during absence. No claim that numerical stability proves fun. |
| P2 / PERF-01 | Scale and background scheduling | Current tests are single-market paths. Benchmark thousands of markets, define active-region aggregation, resumable catch-up/cancellation budgets and immutable UI handoff. Do not run heavy catch-up on a UI or SimConnect callback thread. |

## Next milestone

**The immediate milestone is the connected flight-tracking foundation, FND-01 through FND-06.** This corrects the previous jobs-first recommendation to follow the project instructions. It does not restart or discard the simulation work.

Build a WinUI 3 shell, isolated SimConnect service, normalized telemetry, flight-state detector, active FlightSession and safe SQLite save/recovery as a small working vertical slice. MVP acceptance is a recorded and recoverable real flight, including simulator disconnect/reconnect. A mocked adapter is a development tool, not proof of a live connection.

**After that foundation passes, M2 is** one complete employee-career loop: load a career, discover available aircraft, create demand-backed feasible offers, accept one, verify a flight, settle wages once, save, reload and continue. Extend the tested simulator/persistence foundation with reservation and settlement tests, then connect job screens to its real flight evidence. The shipping workflow must never present a test/manual completion flag as simulator verification.

Build the ledger and reservation logic together before connecting player jobs to aggregate demand. Aircraft purchasing follows this loop; otherwise neither affordability nor meaningful progression can be calibrated.

## Later gameplay decisions (not blockers for the foundation)

1. **Progression pace:** how many real flying hours should it take to afford the first used light aircraft: roughly 10–20, 30–50, or 80+? This sets wages, purchase prices, financing availability and operating-cost pressure.
2. **Absence policy:** the world will advance while not flying as already agreed. During a week away, should personal loan/lease/hangar obligations advance fully, be capped, or wait for active career play? Demand/world time and player liabilities need not share the same policy.
3. **Typical session:** 20–40 minutes, 45–90 minutes, or 2+ hours? Can a job be safely suspended and resumed? This controls the useful job board, deadlines and ground-work pacing.
4. **Setback policy:** should a serious loss permit bankruptcy and a return to employee work, or include a more forgiving recovery system? Simulator faults must be handled separately from genuine operational mistakes.
5. **First supported experience:** which installed aircraft and home area should anchor the first real flight test, and which jobs should lead: cargo, charter, bush/utility, or a mixture? The registry remains extensible; this only selects the first end-to-end test.
6. **Management and procedures:** should maintenance/financing/ground tasks be mostly automatic with exceptions, or require detailed hands-on decisions? This sets how much management surrounds each flight.

These answers guide later gameplay calibration. They are not prerequisites for the immediate WinUI 3/SimConnect/flight-session foundation. Previously agreed WinUI 3, single-player, offline-first and no-XP-grind choices remain in force.


## Answered career questions follow-through — 2026-09-17

See [career foundation decisions](career-foundation-decisions.md) for the current rules, implementation boundaries, provisional assumptions and remaining questions. Protected absence supersedes the earlier grace/cap default. Home/current location, connection eligibility, session duration preferences and bankruptcy eligibility are now domain-tested; database, dispatch and UI integration remain open. Restore and retain the original 29 SimLab regressions in addition to xUnit career tests. The next implementation milestone remains the WinUI/SimConnect/session foundation specified by AGENTS.md.


## Credit and dealer implementation — 2026-09-17

Added career-derived credit score, lender-specific APR/loan limits, amortized affordability checks, new/used dealer profiles, per-listing seeded daily discounts, cash-purchase eligibility and dealer-to-loan quote composition. Updated the first-ownership target to cash-small / financed-larger paths and confirmed F-22 at KRME for first integration testing. See [credit and dealer design](credit-and-dealers.md). Still open: settled-history aggregation, persisted inventory/issued offers, atomic loan origination/purchase, repayment scheduler on protected career time, UI and live aircraft verification. These remain domain quotes, not funded loans or completed purchases.
