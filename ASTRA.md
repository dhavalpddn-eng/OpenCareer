# OpenCareer — Astra Handoff

**Fast path:** read this first. If modifying code, `AGENTS.md` remains canonical. Verify the remote branch head before every write.

## Resume
- Repo: `dhavalpddn-eng/OpenCareer`
- Branch: `feature/m1-simulation-core`
- Draft PR: #2; keep `main` stable.
- Last feature/code commit before this handoff: `bd86bd33da14eda5c7c2087017d0b4ae76768282`.
- Stack: C# / .NET 10 / WinUI 3 / Windows x64 / SimConnect / SQLite / xUnit.
- Offline-first. AI/cloud are optional and never authoritative for money, ownership, mission completion, flight hours, scoring, or settlement.

## Implemented
- WinUI shell + DI/logging.
- Resilient serialized SimConnect connection/reconnect boundary.
- Normalized ~1 Hz telemetry: position, MSL/AGL, IAS/GS, VS, heading, pitch/bank/G, on-ground, brake, engines, fuel/payload, flaps/gear, pause/slew.
- Windows live-probe tool reusing production SimConnect code.
- Pure `FlightTrackingStateMachine` consuming classified evidence, not guessed raw thresholds.
- `FlightTimeLedger`: wall/simulated/block/flight/airborne/taxi/career-credit/pause/slew/night/instrument time.
- Commercial terminal policy: valid parking + stationary + brake + engines off + servicing.
- CI green after flight-core work: **87/87 xUnit + 29/29 SimLab**; Windows WinUI/probe workflow also green.

## Accepted flight rules
- Normal jobs: gate/parking, cold-and-dark. Mission profiles may allow runway starts for emergency/military work.
- Pushback/block time is separate from pilot flight-time credit.
- Standard passenger/cargo multi-leg turns require park, shutdown, unload/load/refuel before next leg.
- Rejected takeoff is its own event; no takeoff count; fuel/brake/wear still apply.
- Crash/reset preserves partial record. Insurance may grant **1 redo per real calendar day**, no carryover; policy-dependent.
- Disconnect alone never fails/expires a flight. Suspend indefinitely; resume only when continuity is plausible.
- Sim-rate changes allowed in any phase. Career credit is capped by real active time: `wall_delta * min(rate, 1)`.
- Correct go-arounds/diversions/rejects may earn small bounded safety credit; serious safely handled failures may earn more; no farming.
- Landing scoring is aircraft-relative when trusted data exists; conservative confidence-labeled fallback otherwise.
- Use simulator time/weather. Night fixed-wing work needs suitable lighting unless mission/aircraft rules explicitly allow otherwise.
- Actual-instrument-like credit requires supported simulated IMC evidence.
- Initial licensing is realistic-but-simplified FAA-style worldwide; rules remain data-driven for later regions.
- Experience progression is deep: total -> category/class -> propulsion/complexity -> family -> exact model/type -> recency/takeoff/landing.

## EFB
Optional MSFS 2024 EFB companion is accepted:
`EFB TypeScript/JSX -> CommBus JSON -> C# SimConnect adapter -> Domain/SQLite -> projection -> EFB`.
Windows app remains authoritative. See `docs/efb-integration.md`.

## Career/economy constraints
- Progress through licenses/ratings, reputation, relationships, reliability/safety, work access, capital, ownership/company capability; do not use XP grind as the primary gate.
- Cargo later uses named commodity lots (coffee, phones, TVs, food, plants, etc.) with quantity/mass/volume/value/origin-destination market context. Freight pay is separate from cargo value.
- Do not expand finance/dealers before the playable flight foundation unless explicitly requested.

## HARD GATE
**Live MSFS validation is still NOT done.** Do not claim real-runtime correctness or freeze IAS/AGL/hysteresis thresholds before observing the user’s Windows/MSFS trace.

Run at repo root:
`./tools/run-live-probe.ps1`

First live scenario: **F-22 at KRME Griffiss**. Validate MSFS absent, connect/load, ~1 Hz telemetry, pause/resume, taxi/takeoff/landing telemetry, quit/restart, abrupt exit, telemetry clearing/reconnect.

## Next bounded work
1. Run/analyze live probe.
2. Fix only concrete SimVar/unit/runtime discrepancies.
3. Implement configurable `FlightEvidenceProcessor` -> tested reducer evidence; calibrate from live trace.
4. Add versioned SQLite `FlightSession`/`FlightLeg` checkpoint + recovery.
5. Then registry/runway feasibility -> jobs -> authoritative mission validation/atomic settlement.

## Open deeper docs only when needed
- `docs/project-state.md` — detailed current handoff.
- `docs/simulator-connection.md` — SimConnect/runtime/live checks.
- `docs/flight-session-design.md` — flight/session rules.
- `docs/efb-integration.md` — EFB architecture.
- `docs/cargo-market-requirements.md` — named cargo markets.
- `docs/ui-screen-spec.md` + `docs/ui-design-system.md` — UI.
- `docs/development-workflow.md` — chapter gates.

**Never merge PR #2 / `main` unless explicitly requested and exit gates are met.**
