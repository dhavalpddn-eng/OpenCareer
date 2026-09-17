# OpenCareer project state

Updated: 2026-09-17. **Read this after `AGENTS.md`; do not reread chat history unless a required decision is missing.**

## Resume here

- Repo: `dhavalpddn-eng/OpenCareer`
- Branch: `feature/m1-simulation-core`; draft PR #2. Keep `main` stable.
- Chapter 2 is in progress: real WinUI 3 shell now exists under `src/OpenCareer.App`.
- Windows UI build workflow: `.github/workflows/winui-build.yml`.
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
- Last verified domain baseline: **35 xUnit tests** plus **29 deterministic SimLab scenarios** after dealer-validation correction.

## Chapter 2 shell now present

`src/OpenCareer.App` contains:

- unpackaged .NET 10 WinUI 3 project pinned to `Microsoft.WindowsAppSDK 2.4.0`,
- application resources and DI/logging startup,
- `NavigationView` shell,
- observable simulator connection-state ViewModel,
- disconnected Dashboard with KRME home-base placeholder,
- empty Current Flight view,
- placeholders for later sections,
- Windows GitHub Actions build workflow.

The shell currently has **no SimConnect implementation**; `Waiting for MSFS 2024` is intentional. A passing Windows CI build proves compilation only, not live MSFS behavior.

## Material limits

No live SimConnect verification, telemetry detector, durable SQLite flight recovery, installed-aircraft registry, dispatch/runway planner or market-driven job generator yet. Loan/dealer/manual-ground outputs remain quotes until authoritative persistence and one-time settlement exist. Ownership balance numbers are provisional until real job income is wired and playtested.

## Next bounded work

1. Make Windows CI green and fix any WinUI compile/runtime-shell issues.
2. Add isolated `ISimulatorConnection` / SimConnect connection + safe reconnect.
3. Normalize first telemetry fields.
4. Build flight-state detection and recoverable FlightSession persistence.
5. Then connect registry/runway feasibility/jobs/economy settlement.

Do **not** expand finance complexity before the playable flight foundation unless explicitly requested.

## Detail only when needed

- `docs/ui-concept.md` — UI direction and level policy.
- `docs/career-foundation-decisions.md` — accepted gameplay/base decisions.
- `docs/credit-and-dealers.md` — finance/dealer model.
- `docs/msfs-sdk-strategy.md` — verified SDK boundaries.
- `docs/simulation-model.md` — deterministic simulation invariants.
- `docs/development-workflow.md` — chapter order/exit gates.
- `docs/current-review.md` — latest review findings before Chapter 2.
