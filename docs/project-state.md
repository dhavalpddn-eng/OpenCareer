# OpenCareer project state

Updated: 2026-09-17. **Read this after `AGENTS.md`; do not reread chat history unless a required decision is missing.**

## Resume here

- Repo: `dhavalpddn-eng/OpenCareer`
- Branch: `feature/m1-simulation-core`; draft PR #2. Keep `main` stable.
- Latest tested implementation: `2f63e56a8da585c7cbab4eb2d53d4a6b19a1b404` (`fix: compile live SimConnect probe`). The production telemetry implementation remains `7fddbe1cc5d30fbe17411eef341f8f22dbbba92f`.
- WinUI 3 shell, resilient SimConnect connection/reconnect and first normalized aircraft telemetry are implemented. **Live simulator/runtime validation remains open.**
- [Windows CI run 35283864087](https://github.com/dhavalpddn-eng/OpenCareer/actions/runs/35283864087): WinUI x64 Release build and the live SimConnect probe both succeeded with 0 warnings/errors; **74/74 xUnit tests passed**.
- [Linux CI run 35283863937](https://github.com/dhavalpddn-eng/OpenCareer/actions/runs/35283863937): **74/74 xUnit tests** and **29/29 deterministic SimLab scenarios** passed.
- Both CI runs tested `2f63e56`; compilation of the probe still does **not** imply live MSFS verification.
- UI target/spec: `docs/ui-design-system.md` + `docs/ui-screen-spec.md`; visual references: `docs/assets/opencareer-dashboard-concept-v2.svg`, `docs/assets/opencareer-ui-screen-atlas.svg`, and `docs/assets/opencareer-conflict-operations.svg`. `docs/ui-concept.md` remains the short visual-direction entry point.
- Verify remote branch head before edits because other chats may change it.

## Fixed direction

Single-player, offline-first MSFS 2024 companion. C#/.NET 10, Windows x64, WinUI 3/Windows App SDK, isolated SimConnect boundary, SQLite, deterministic/testable domain. No aircraft whitelist. AI is optional and never authoritative for money, ownership, mission completion or critical state.

## Confirmed gameplay

- First live test hub: **KRME Griffiss International**; mixed civilian/government/defense/UAS context. Do not invent a resident fighter wing or real-world missions that are not documented.
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

## Implemented domain foundation

- Deterministic economy/world events, replay/checkpoints, contract lifecycle/dispatch guards.
- Location/home-base connection rules and travel-history guards.
- Protected-absence policy, session preferences, bankruptcy stages and bounded manual-ground reward quotes.
- Career-derived credit model, fictional lenders, affordability/debt-service checks.
- Fictional aircraft dealers, seeded offers/discounts, stock validation and cash/finance eligibility.
- Domain baseline remains **35 xUnit tests** plus **29 deterministic SimLab scenarios**. Connection/telemetry/decoder/ViewModel additions bring the current xUnit total to **74 passing**.

## Chapter 2 shell and simulator boundary

`src/OpenCareer.App` contains:

- unpackaged .NET 10 WinUI 3 project pinned to `Microsoft.WindowsAppSDK 2.4.0`,
- application resources and DI/logging startup,
- `NavigationView` shell,
- observable simulator connection and telemetry ViewModel,
- disconnected Dashboard with KRME home-base placeholder,
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

## Material limits

No live native SimConnect/UI interaction verification, robust flight-state detector, durable SQLite flight recovery, installed-aircraft registry, dispatch/runway planner or market-driven job generator yet. The first telemetry path and live-probe executable are CI-built/tested, but neither has been observed against the installed MSFS 2024 runtime or a real aircraft. Long simulator stalls may cause a safe reconnect; native calls themselves cannot be forcibly interrupted. Loan/dealer/manual-ground outputs remain quotes until authoritative persistence and one-time settlement exist. Ownership balance numbers are provisional until real job income is wired and playtested.

## Next bounded work

1. **Run the live probe on the user's Windows/MSFS machine before building flight-state logic.** From the repo root use `./tools/run-live-probe.ps1` (or pass `-SimConnectNativePath`). Verify simulator absence, 1 Hz telemetry, menus/pause/resume, quit/restart, abrupt simulator exit and final telemetry clearing. Use `docs/simulator-connection.md`; no live-test claim until observed. F-22/KRME remains the first flight test.
2. Correct any SimVar/unit/runtime discrepancy found by that live test without broadening scope.
3. Build robust flight-state detection and time accounting from `docs/flight-session-design.md`, then versioned SQLite FlightSession/FlightLeg save/recovery. Keep load observation, movement/flight time, airborne time and anti-exploit career-credit time separate.
4. Connect registry/runway feasibility/jobs/economy settlement.

Do **not** expand finance complexity before the playable flight foundation unless explicitly requested.

## Detail only when needed

- `docs/ui-concept.md` — short UI direction and level policy.
- `docs/ui-design-system.md` — canonical shell, tokens, layout/adaptive/accessibility rules.
- `docs/ui-screen-spec.md` — target UX for all 15 current navigation destinations, including Conflict Operations.
- `docs/career-foundation-decisions.md` — accepted gameplay/base decisions.
- `docs/credit-and-dealers.md` — finance/dealer model.
- `docs/msfs-sdk-strategy.md` — verified SDK boundaries.
- `docs/simulator-connection.md` — implemented connection/telemetry boundary, runtime setup and pending live checks.
- `docs/flight-session-design.md` — accepted load/flight/leg/time/landing/resume/free-flight/postflight rules for Chapter 4.
- `docs/cargo-market-requirements.md` — accepted named-commodity/market-value requirements for later cargo generation.
- `docs/simulation-model.md` — deterministic simulation invariants.
- `docs/development-workflow.md` — chapter order/exit gates.
- `docs/current-review.md` — latest review findings before Chapter 2.
