# OpenCareer — Astra Fast Resume

**Read this first.** If coding, `AGENTS.md` is canonical. Verify remote head before writes.

**Repo:** `dhavalpddn-eng/OpenCareer`  
**Branch:** `feature/m1-simulation-core`  
**PR:** #2 draft; keep `main` stable.  
**Last implementation commit:** `540029c378a507f729dfe451db642eca4065ae65` (offline probe analysis).

**Stack:** C# / .NET 10 / WinUI 3 / Windows x64 / SimConnect / SQLite / xUnit. Offline-first. AI/cloud never authoritative for money, ownership, mission completion, flight hours, scoring, or settlement.

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
**Live MSFS runtime validation is NOT done.** Do not freeze IAS/AGL/hysteresis thresholds or claim live correctness before the trace.

Run: `./tools/run-live-probe.ps1`  
First live validation fixture: **F-22 at KRME** (test only; not a career starting aircraft). Validate absent/connect/load/~1 Hz/pause/taxi/takeoff/landing/quit/restart/abrupt exit/telemetry clear/reconnect.

## Next
1. Run/analyze live probe. Offline command: `dotnet run --project tools/OpenCareer.TraceAnalysis -c Release -- <trace.jsonl>`. See its README for findings/exit codes; a clean report is not live acceptance.
2. Fix only concrete SimVar/unit/runtime issues.
3. Build configurable `FlightEvidenceProcessor` -> tested reducer evidence.
4. Add versioned SQLite `FlightSession`/`FlightLeg` checkpoint/recovery.
5. Then registry/runway feasibility -> jobs -> mission validation + atomic settlement.

**Do not:** merge PR/main without explicit approval; expand finance/dealers before playable flight core; let AI/cloud become gameplay authority.

Deep docs only if needed: `docs/project-state.md`, `docs/simulator-connection.md`, `docs/flight-session-design.md`, `docs/efb-integration.md`, `docs/cargo-market-requirements.md`, `docs/ui-screen-spec.md`, `docs/development-workflow.md`.
