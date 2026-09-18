# OpenCareer — Astra Fast Resume

**Read this first.** If coding, `AGENTS.md` is canonical. Verify remote head before writes.

**Repo:** `dhavalpddn-eng/OpenCareer`  
**Branch:** `feature/m1-simulation-core`  
**PR:** #2 draft; keep `main` stable.  
**Last feature commit:** `bd86bd33da14eda5c7c2087017d0b4ae76768282`.

**Stack:** C# / .NET 10 / WinUI 3 / Windows x64 / SimConnect / SQLite / xUnit. Offline-first. AI/cloud never authoritative for money, ownership, mission completion, flight hours, scoring, or settlement.

## Built
WinUI shell; resilient serialized SimConnect connect/reconnect; ~1 Hz normalized telemetry; Windows live probe; pure `FlightTrackingStateMachine`; `FlightTimeLedger`; conventional park/shutdown/servicing terminal policy. CI after flight-core work: **87/87 xUnit + 29/29 SimLab; Windows WinUI/probe green**.

## Flight rules
Normal jobs cold-and-dark at parking; mission profiles may authorize runway starts. Block/pushback time != pilot flight time. Commercial multi-leg turns require park/shutdown/service. Rejected takeoff is distinct; no takeoff count. Crash preserves partial record; insurance may allow **1 redo/day, no carryover**. Disconnect only suspends; no short timeout; resume on plausible continuity. Sim-rate allowed anytime, but career credit = `wall_delta * min(rate,1)`. Legit safety decisions may earn small bounded credit; no farming. Landing scoring aircraft-relative when trusted data exists. Use simulator time/weather; normal night fixed-wing jobs need suitable lighting. Actual-instrument credit needs simulated IMC evidence. Initial progression is simplified FAA-style; aircraft experience is layered: total -> category/class -> propulsion/complexity -> family -> model/type -> recency/TO/L.

## EFB
Accepted future thin UI:
`MSFS EFB TS/JSX -> CommBus JSON -> C# SimConnect -> Domain/SQLite -> EFB projection`.
Windows app stays authoritative. See `docs/efb-integration.md`.

## HARD GATE
**Live MSFS runtime validation is NOT done.** Do not freeze IAS/AGL/hysteresis thresholds or claim live correctness before the trace.

Run: `./tools/run-live-probe.ps1`  
First live scenario: **F-22 at KRME**. Validate absent/connect/load/~1 Hz/pause/taxi/takeoff/landing/quit/restart/abrupt exit/telemetry clear/reconnect.

## Next
1. Run/analyze live probe.
2. Fix only concrete SimVar/unit/runtime issues.
3. Build configurable `FlightEvidenceProcessor` -> tested reducer evidence.
4. Add versioned SQLite `FlightSession`/`FlightLeg` checkpoint/recovery.
5. Then registry/runway feasibility -> jobs -> mission validation + atomic settlement.

**Do not:** merge PR/main without explicit approval; expand finance/dealers before playable flight core; let AI/cloud become gameplay authority.

Deep docs only if needed: `docs/project-state.md`, `docs/simulator-connection.md`, `docs/flight-session-design.md`, `docs/efb-integration.md`, `docs/cargo-market-requirements.md`, `docs/ui-screen-spec.md`, `docs/development-workflow.md`.
