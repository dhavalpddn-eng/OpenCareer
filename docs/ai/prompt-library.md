# OpenCareer AI Development Prompt Library

Canonical reusable prompts for AI-assisted development of `dhavalpddn-eng/OpenCareer`.

Repository code, `AGENTS.md`, `ASTRA.md`, the Master Build List, development checklist, and relevant handoffs remain authoritative. These prompts are workflow controls, not replacements for repository architecture.

## P01 — New Lane Bootstrap

**Use when:** Starting or repurposing a chat for a subsystem.

```text
Operate in Astra-style autonomous development mode for dhavalpddn-eng/OpenCareer.

LANE: <MBL / SYSTEM>

Read and obey current AGENTS.md, ASTRA.md, and only relevant MBL/checklist/handoff sections. Remote code is truth.

STRICT ANTI-HANG MODE.

This chat owns ONLY its assigned lane.

For each development turn:
1. Verify current remote branch/head.
2. Inspect recent directly relevant commits.
3. Determine what is already implemented.
4. Select exactly ONE smallest meaningful unfinished slice.
5. Inspect only directly relevant files.
6. Implement it.
7. Run focused deterministic tests.
8. Fix only failures caused by the slice.
9. Recheck remote before committing.
10. Commit.
11. STOP.

Do not broadly audit the repository every turn, repeatedly read unchanged docs, scan unrelated files, attempt an entire MBL, repeatedly run the full suite, perform unrelated refactoring, redo completed work, cross another active lane, fake missing dependencies, or manufacture work after reaching a boundary.

Consume existing contracts from other systems. If another unfinished subsystem is required, mark that work BLOCKED.

Before coding:
HEAD: <sha> | SLICE: <one sentence>

At completion:
COMMIT: <sha> | TESTS: <result> | NEXT: <smallest meaningful unfinished slice>

Do NOT begin NEXT in the same turn.

If meaningful independent implementation is exhausted:
BOUNDARY REACHED | BLOCKED: <dependencies or NONE> | LOCAL: <verification or NONE>
Then STOP.
```

## P02 — Next Slice

**Use when:** A lane is active and needs one more normal development slice.

```text
NEXT — continue your assigned OpenCareer lane only.

STRICT ANTI-HANG MODE.

Verify current remote head and recent relevant commits first. Complete exactly ONE smallest meaningful unfinished slice from the known remaining work.

Remote code is truth. Do not redo completed work. Inspect only directly relevant files. No broad audit or unrelated refactor. Use focused tests. Do not cross another active lane or fake blocked dependencies. Recheck remote before commit. Commit and STOP.

If the previously identified NEXT slice is already complete remotely, choose the next smallest independent slice in this lane.

If no meaningful independent implementation remains, report BOUNDARY REACHED rather than manufacturing work.

Finish:
COMMIT: <sha> | TESTS: <result> | NEXT: <smallest remaining slice or AUDIT>

Do NOT begin NEXT in the same turn.
```

## P03 — Boundary Audit

**Use when:** About five meaningful commits have landed, work is becoming minor, or the subsystem may be finished.

```text
STOP before starting another implementation slice.

Audit your owned system against CURRENT remote code and relevant MBL/checklist sections. Do NOT implement anything.

Determine exactly what is genuinely complete, what meaningful implementation remains unblocked, what is blocked by another system, what requires local MSFS/Windows/visual verification, and whether continuing would produce only tests, polish, docs, speculative mechanics, or unnecessary abstractions.

Do not manufacture work, expand scope, or audit unrelated systems.

Report:
HEAD: <sha>
BOUNDARY: <NOT REACHED / REACHED>
REMAINING UNBLOCKED: <exact implementation slices or NONE>
BLOCKED: <dependencies or NONE>
LOCAL VERIFICATION: <items or NONE>
RECOMMENDATION: <CONTINUE / PARK THIS CHAT>
```

## P04 — Resume From Audit

**Use when:** P03 says NOT REACHED.

```text
NEXT — continue your assigned OpenCareer lane only.

Use the completed audit as the current boundary map.

AUDITED HEAD: <sha>
REMAINING UNBLOCKED: <paste audit list>

STRICT ANTI-HANG MODE.

Verify current remote head and recent relevant commits. Select exactly ONE smallest meaningful unfinished slice from the audited unblocked work.

Prefer: <desired next slice>

Do not combine later items unless technically inseparable. Inspect only directly relevant files. Run focused deterministic tests. Preserve architecture, persistence, recovery, and idempotency guarantees. Do not cross another active lane or fake blocked dependencies. Recheck remote before commit. Commit and STOP.

If the preferred slice is already complete, choose the next smallest independent audited item. If the audited list is exhausted, report BOUNDARY REACHED.

Finish:
COMMIT: <sha> | TESTS: <result> | NEXT: <smallest audited remaining slice or AUDIT>
```

## P05 — Hung Chat Recovery

**Use when:** A turn runs substantially longer than comparable slices or appears stuck.

```text
STOP the current long-running operation.

STRICT RECOVERY / ANTI-HANG MODE.

Do not restart the same expensive command or broad test/build.

1. Check current working-tree state and remote head.
2. Preserve valid implementation already completed.
3. Identify what caused the stall.
4. If implementation is complete, run ONLY the narrowest focused tests needed.
5. If verification hangs or is disproportionately expensive, stop it and use a narrower technically valid method.
6. Do not run the full repository suite.
7. Do not perform another broad audit.
8. Do not expand or restart the slice.
9. Fix only failures directly caused by this slice.
10. Recheck remote.
11. Commit valid completed work.
12. STOP.

If implementation is incomplete, finish ONLY the smallest remaining portion of the already-active slice. If it cannot be safely recovered, leave the working tree intact and report what remains.

Do NOT use destructive Git operations that could erase current or parallel work.

Final:
HEAD: <sha> | RECOVERED: <work> | TESTS: <focused result> | COMMIT: <sha or NOT COMMITTED> | STALL CAUSE: <cause> | NEXT: <smallest next slice>
```

## P06 — Routing Audit

**Use when:** A lane parks and a development slot becomes available.

```text
Do not implement anything.

Inspect CURRENT OpenCareer remote code and the Master Build List to determine the best independent development lane to activate next.

CURRENT ACTIVE LANES: <list>
CURRENT PARKED/COMPLETED LANES: <list>

Prioritize unfinished prerequisites that unblock multiple systems, have meaningful independent implementation available now, have established requirements/contracts, minimize collision with active chats, and advance the playable core.

Do not select a system already owned by an active chat. Do not reopen a parked system unless a dependency has actually become available. Do not perform an exhaustive repo review.

Report:
BEST NEXT LANE: <MBL/system>
WHY: <what it unblocks>
CURRENT HEAD: <sha>
UNBLOCKED WORK: <specific slices>
BLOCKED WORK: <dependencies>
RECOMMENDED FIRST SLICE: <smallest meaningful slice>
```

## P07 — Verify Only

```text
VERIFY ONLY. Do not implement a new slice.

Verify current completed work using the narrowest technically sufficient strategy. Verify remote head first.

Prioritize focused tests, affected project build, relevant integration tests, then broader suite only when shared-contract/persistence impact justifies it.

Do not modify unrelated code or begin NEXT. If verification exposes a defect directly caused by current work, apply only the smallest reliable correction, rerun focused verification, commit, and STOP.

Report:
HEAD: <sha> | VERIFICATION: <PASS/FAIL> | TESTS/BUILD: <results> | FIX: <sha or NONE>
```

## P08 — Finish Current Slice

```text
FINISH the currently active slice only.

Do not begin another slice, expand scope, or perform additional architecture work unless required to finish the current implementation.

Complete only what remains, run focused verification, recheck remote, commit, and STOP.

Report:
COMMIT: <sha> | TESTS: <result> | NEXT: <one sentence only>
```

## P09 — Parallel Lane Guard

```text
PARALLEL OWNERSHIP GUARD.

Stop before modifying another active lane.

THIS CHAT OWNS: <system>
OTHER ACTIVE LANES: <list>

If the current slice can use existing public/application contracts, do so without changing another lane. If it requires unfinished functionality elsewhere, mark it BLOCKED.

Do not create duplicate infrastructure, modify another lane's internals, copy another system's logic locally, fake missing authority/evidence, or redesign a shared contract solely for convenience.

If no independent work remains, report BOUNDARY REACHED. Otherwise complete exactly ONE independent slice, verify, commit, and STOP.
```

## P10 — Remote Moved / Reconcile

```text
REMOTE MOVED — RECONCILE BEFORE CONTINUING.

Do not overwrite remote work.

1. Fetch/verify current remote branch/head.
2. Compare relevant remote changes with current working changes.
3. Classify them as independent, complementary, overlapping, or conflicting.
4. Preserve valid work from both sides.
5. Reconcile only files/contracts required for the active slice.
6. Do not perform unrelated cleanup/refactoring.
7. Run focused tests.
8. Recheck remote.
9. Commit only when safe.
10. STOP.

Never force-push or destructively discard parallel work unless explicitly authorized.

Report:
REMOTE: <sha> | RELATION: <independent/complementary/overlap/conflict> | RESULT: <reconciled/blocked> | COMMIT: <sha or NONE> | TESTS: <result>
```

## P11 — Test/Build Hang Recovery

```text
STOP the expensive verification command. Do not rerun the same broad command.

Implementation scope is frozen.

Use the smallest technically valid verification target: specific test/filter, affected test project, affected build project, or narrow integration test.

Do not weaken/delete legitimate tests merely to get green. If focused verification fails, fix only defects caused by this slice.

Recheck remote, commit the validated slice if appropriate, and STOP.

Report:
STALL: <command/category> | FOCUSED VERIFICATION: <result> | COMMIT: <sha or NONE> | BROADER VERIFICATION: <deferred/not required>
```

## P12 — Completion / Park

```text
The owned system has reached its current implementation boundary.

Do NOT start another feature.

Check whether tracker/handoff docs require synchronization. If already accurate, make no change. If needed, update only relevant tracker/handoff state, commit the documentation sync, and STOP.

Do not manufacture tests, polish, refactors, mechanics, or speculative work.

Report:
FINAL HEAD: <sha> | SYSTEM: <system> | STATUS: PARKED | BLOCKED: <dependencies> | LOCAL: <verification>
```

## P13 — Recheck Parked Lane

```text
RECHECK ONLY — do not implement yet.

This lane was parked because of:
<BLOCKERS>

A potentially relevant dependency has advanced:
<DEPENDENCY + HEAD/CHANGE>

Verify current remote code and determine whether that change genuinely removes any blocker.

Report:
HEAD: <sha>
UNBLOCKED NOW: <specific work or NONE>
STILL BLOCKED: <items>
RECOMMENDATION: <REACTIVATE / KEEP PARKED>
FIRST SLICE IF REACTIVATED: <one smallest slice or NONE>
```

## P14 — Integration Boundary Check

```text
INTEGRATION BOUNDARY CHECK.

Do not immediately modify either subsystem.

SYSTEM A: <system>
SYSTEM B: <system>

Inspect CURRENT production contracts and relevant MBL requirements.

Determine authoritative ownership, data crossing the boundary, whether an existing contract suffices, whether an adapter/application coordinator is enough, whether either subsystem needs modification, required persistence/idempotency semantics, and focused integration tests.

Prefer composition/application-layer integration. Do not duplicate authority, recompute authoritative state in consumers, or create circular dependencies.

Report:
PRODUCER: <system/contract>
CONSUMER: <system>
BOUNDARY: <existing/new adapter/minimal contract extension>
FIRST INTEGRATION SLICE: <smallest implementation>
RISKS: <brief>

Do not implement until the boundary is established.
```

## P15 — Live MSFS Validation Gate

```text
LIVE MSFS VALIDATION GATE.

Do not add speculative code to replace runtime validation.

Identify exactly what requires real MSFS verification and what has already been proven deterministically.

Produce the minimum validation procedure: simulator starting state, aircraft/start configuration, telemetry/evidence to observe, expected OpenCareer transitions, failure indicators, and minimum representative scenarios.

Do not expand or redesign the feature merely because local validation is pending.

Report:
CODE BOUNDARY: COMPLETE
LOCAL VALIDATION REQUIRED: <behavior>
MINIMUM SCENARIOS: <scenarios>
PASS CONDITION: <condition>
FAILURE EVIDENCE TO CAPTURE: <data/logs>

Then STOP.
```

## Quick Index

- **P01** — New lane bootstrap
- **P02** — Normal next slice
- **P03** — Boundary audit
- **P04** — Resume from audit
- **P05** — Hung chat recovery
- **P06** — Routing audit
- **P07** — Verify only
- **P08** — Finish current slice
- **P09** — Parallel lane guard
- **P10** — Remote moved/reconcile
- **P11** — Test/build hang recovery
- **P12** — Completion/park
- **P13** — Recheck parked lane
- **P14** — Integration boundary check
- **P15** — Live MSFS validation gate

## Operating Rhythm

Normal active lane:

`P01 → P02 × 5 → P03`

If P03 says CONTINUE:

`P04 → P02 as needed → P03`

If P03 says PARK:

`P12`

When a slot becomes available:

`P06`

When a dependency may unblock a parked lane:

`P13`

If a turn appears hung:

`P05`

If only tests/builds are hung:

`P11`

When separately developed systems need to connect:

`P14`

When only real MSFS verification remains:

`P15`

The goal is not to keep every chat busy. The goal is to keep every active chat doing meaningful, independently mergeable work.
