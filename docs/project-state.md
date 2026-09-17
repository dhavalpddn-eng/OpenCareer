# OpenCareer project state

Updated: 2026-09-17. Compact handoff for a fresh chat; detailed rules remain in AGENTS.md.

## Resume here

- Repository: `dhavalpddn-eng/OpenCareer`.
- Working branch: `feature/m1-simulation-core`; draft PR #2. Keep main stable.
- Latest implementation commit: `79f1072d6a9b7a111f03899ea104e2086c9b7f2d` (dealer stock and offer-consistency validation). This handoff is a later documentation-only change.
- Read AGENTS.md and this file first, then only source/docs needed for the current task. Retrieve earlier conversations only if a necessary decision is missing.
- Verify current remote branch head before editing; other chats may change it. Do not overwrite their work.
- Update this handoff after substantive work: implementation commit, verification, limitations and next task. Keep it short; replace stale status instead of appending a transcript.

## Fixed direction

Single-player MSFS 2024 companion; offline core; C#/.NET 10; Windows x64; WinUI 3/Windows App SDK; isolated SimConnect adapter; SQLite. Domain rules stay independent of simulator/UI. No aircraft whitelist or XP grind: career standing comes from qualifications, reliability, reputation, relationships and finances. User permits focused implementation/fixes on the feature branch; no main merge is implied.

## Confirmed gameplay decisions

- First live test: F-22 at Griffiss International (KRME). Exact installed add-on identity/performance still unverified. Military assignment is separate from civilian ownership.
- First ownership target: roughly 50–80 real flying hours, either buy a small aircraft outright or finance a larger one according to career standing and affordability. No forced hour unlock.
- Sessions: mostly 1–3 hours, supported up to six; interruption recovery required.
- Home airport and geographic connections matter. Jobs/travel can expose new markets; storage and expansion must be local economic resources, with no teleporting.
- Bankruptcy should follow sustained bad decisions; employee work remains a recovery path.
- Mostly automatic management, optional modest manual-ground rewards.
- Military-heavy airports should have substantially more military work while public airports retain civilian jobs. KRME has civil/government/UAS and defense-support context; do not invent a resident fighter wing or actual advertised military sorties.

## Implemented and verified

Projects: `src/OpenCareer.Domain`, `src/OpenCareer.SimLab`, `tests/OpenCareer.Tests`.

- Deterministic economy/world events, scope handling, checkpoint replay, contract transitions and dispatch guards.
- Home/current location separation, local job guards, completed-travel connection history and replay protection.
- Protected absence default, session-duration preferences, bankruptcy eligibility stages, bounded manual-reward quotes.
- Career-derived credit score; three fictional lenders with rate, collateral, income, debt-service, deposit and reserve checks.
- Three fictional dealer profiles with new/used filters, seeded daily discounts, cash eligibility and financing quote composition.
- Verification baseline: 34 xUnit tests and 29 SimLab scenarios passed locally. A subsequent dealer-validation correction passes 35 xUnit tests. [GitHub CI passed](https://github.com/dhavalpddn-eng/OpenCareer/actions/runs/35244805482).

Commands from repository root:
```sh
dotnet test tests/OpenCareer.Tests/OpenCareer.Tests.csproj --configuration Release
dotnet run --project src/OpenCareer.SimLab/OpenCareer.SimLab.csproj --configuration Release
```

## Provisional rules and material limits

- Protected absence was chosen provisionally because the user was undecided: world advances; personal bills pause. Staff alone must not enable uncontrolled offline debt. Passive income and personal deadline integration remain unfinished.
- Ownership calibration: fictional $60,000 small aircraft + $4,000 reserve, or $250,000 larger aircraft with 20% deposit + $14,000 reserve; $1,000 net savings/hour gives 64 hours for either. Actual wages/markets are not yet balanced to this target.
- Loan/dealer outputs are quotes, not funded loans or purchases. Career input aggregation, inventory/issued-offer persistence, atomic settlement, repayment scheduling and UI remain open. Preserve issued discounts; never trust client-supplied scores/prices/ownership flags.
- Manual rewards are quotes only; independent evidence and once-per-flight ledger settlement remain open.
- Location-aware wrappers exist; future orchestration must use them. Aircraft location, employer transfers, storage inventory/purchase and relocation are unfinished.
- Serialization tests are not crash-safe SQLite saves or MSFS in-flight restoration.
- No playable Windows application, SimConnect integration, flight detector, persistent flight session, installed-aircraft registry, dispatch planner or market job generator yet. No live MSFS verification.

## Chapter workflow

Follow [development workflow](development-workflow.md) for chapter order and exit gates. Chapter 1 handoff is established; Chapter 2 (Windows application shell) is next. Existing later-chapter domain rules do not imply those chapters are playable or complete.

## Next bounded task

Build the WinUI 3 application shell with connection-status and empty Current Flight views. It must launch without MSFS and show Waiting/Disconnected. Keep domain isolated; introduce application-level DI/logging only where needed. Verify build on Windows; do not claim a Linux domain build validates WinUI or live SimConnect.

Then: isolated SimConnect connection/reconnect boundary → normalized telemetry → robust flight detection → versioned SQLite session save/recovery → registry and runway feasibility → jobs and atomic mission/economy settlement. Do not expand finance complexity before this playable foundation works unless explicitly requested.

## Detail references

- [Backlog](development-backlog.md): implementation order and outstanding work.
- [Career decisions](career-foundation-decisions.md): accepted answers, provisional policies and airport sources.
- [Credit and dealers](credit-and-dealers.md): scoring, lender/dealer tuning, transaction boundaries.
- [SDK strategy](msfs-sdk-strategy.md): verified SDK direction and integration limitations.
- [Simulation model](simulation-model.md): deterministic invariants.
- [Instruction alignment](instruction-alignment.md): earlier architecture review; historical status, not current implementation inventory.

## Visual reference

[UI concept and preview](ui-concept.md) — optional reading for UI/design tasks only. Keep the SVG/image out of routine context. Concept uses sample data; no implemented-screen claim.

## Latest review

[Current review](current-review.md): fresh source/test verification, dealer consistency fix, image-authentication blocker and seven unanswered design questions. Chapter 2 remains next. Read the review only when those details are relevant.
