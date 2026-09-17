# OpenCareer project state

Updated: 2026-09-17. **Read this after `AGENTS.md`; do not reread chat history unless a required decision is missing.**

## Resume here

- Repo: `dhavalpddn-eng/OpenCareer`
- Branch: `feature/m1-simulation-core`; draft PR #2. Keep `main` stable.
- Latest implementation: `0b211b3d0fde731fa3e3ae773fcf3a96615e1663` (isolated SimConnect connection/reconnect). This handoff is a later documentation change.
- WinUI 3 shell and connection boundary are implemented; live simulator validation remains open.
- Windows Release build and all **63 tests passed**: [CI run 35275477791](https://github.com/dhavalpddn-eng/OpenCareer/actions/runs/35275477791). Linux tests/SimLab also passed: [CI run 35275477782](https://github.com/dhavalpddn-eng/OpenCareer/actions/runs/35275477782). Both tested implementation `0b211b3`; documentation-only follow-ups do not imply new live verification.
- UI target/spec: `docs/ui-concept.md`; refined lightweight preview: `docs/assets/opencareer-dashboard-concept-v2.svg`.
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

## Implemented domain foundation

- Deterministic economy/world events, replay/checkpoints, contract lifecycle/dispatch guards.
- Location/home-base connection rules and travel-history guards.
- Protected-absence policy, session preferences, bankruptcy stages and bounded manual-ground reward quotes.
- Career-derived credit model, fictional lenders, affordability/debt-service checks.
- Fictional aircraft dealers, seeded offers/discounts, stock validation and cash/finance eligibility.
- Domain baseline remains **35 xUnit tests** plus **29 deterministic SimLab scenarios**. Connection/decoder/ViewModel additions bring the current test total to **63 passing locally in Release**, with all **29 SimLab scenarios passing**.

## Chapter 2 shell verified

`src/OpenCareer.App` contains:

- unpackaged .NET 10 WinUI 3 project pinned to `Microsoft.WindowsAppSDK 2.4.0`,
- application resources and DI/logging startup,
- `NavigationView` shell,
- observable simulator connection-state ViewModel,
- disconnected Dashboard with KRME home-base placeholder,
- empty Current Flight view,
- placeholders for later sections,
- Windows GitHub Actions build workflow.

The shell now starts the connection service, refreshes status on its dispatcher and awaits asynchronous cleanup on window close. A connection alone never claims an aircraft or active flight.

## Connection boundary implemented

- `src/OpenCareer.Application/Simulator`: `ISimulatorConnection` and immutable state/identity snapshots, independent of WinUI/SDK/domain rules.
- `src/OpenCareer.SimConnect`: four documented native calls through P/Invoke, owned by one dedicated worker; open acknowledgement, quit/disconnect, bounded retries, cancellation, timeout, runtime/version failures and clean close.
- Correlated `RequestSystemState("Sim")` checks liveness every five seconds, with a 30-second response timeout. Menus are valid responses. Empty dispatch is not proof of a disconnect.
- Tests inject native-call results/raw callback buffers; native DLL execution is **not** tested by these mocks.
- Runtime: supply the installed MSFS 2024 SDK's x64 `SimConnect.dll` through `MSFS2024_SDK` or `-p:SimConnectNativePath=...`. It is copied beside the app, never committed. A build without the DLL launches with connection unavailable; with the DLL and MSFS closed it should show Waiting for MSFS.
- Exact setup, SDK references and remaining Windows live checks: `docs/simulator-connection.md`.

## Material limits

No live native SimConnect/UI interaction verification, aircraft telemetry, flight detector, durable SQLite flight recovery, installed-aircraft registry, dispatch/runway planner or market-driven job generator yet. Long simulator stalls may cause a safe reconnect; native calls themselves cannot be forcibly interrupted. Loan/dealer/manual-ground outputs remain quotes until authoritative persistence and one-time settlement exist. Ownership balance numbers are provisional until real job income is wired and playtested.

## Next bounded work

1. Normalize first aircraft telemetry fields behind an isolated source interface; inspect existing `Domain/Telemetry` models before extending them. Confirm current official SDK SimVars/units and keep native subscriptions on the connection worker. Display core telemetry without claiming an active FlightSession.
2. Validate the actual SDK runtime on Windows: launch without MSFS, connect, menus/pause, quit/restart, abrupt simulator exit and application shutdown. Use `docs/simulator-connection.md`; no live-test claim until observed. F-22/KRME remains the first flight test.
3. Build robust flight-state detection, then versioned SQLite FlightSession save/recovery.
4. Connect registry/runway feasibility/jobs/economy settlement.

Do **not** expand finance complexity before the playable flight foundation unless explicitly requested.

## Detail only when needed

- `docs/ui-concept.md` — UI direction and level policy.
- `docs/career-foundation-decisions.md` — accepted gameplay/base decisions.
- `docs/credit-and-dealers.md` — finance/dealer model.
- `docs/msfs-sdk-strategy.md` — verified SDK boundaries.
- `docs/simulator-connection.md` — implemented connection boundary, runtime setup and pending live checks.
- `docs/simulation-model.md` — deterministic simulation invariants.
- `docs/development-workflow.md` — chapter order/exit gates.
- `docs/current-review.md` — latest review findings before Chapter 2.
