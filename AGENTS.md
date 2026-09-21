# OpenCareer AI Agent Instructions

## Compact prompt commands

For `P01`-`P15` commands, use these definitions directly. Do NOT read `docs/ai/prompt-library.md` unless the user explicitly asks for the full prompt/reference.

Global rules for all development commands: current remote code is truth; preserve current lane ownership; inspect only directly relevant files; do not redo completed work; prefer one smallest meaningful slice; use focused verification; avoid broad audits/full suites unless required; recheck remote before commit; never manufacture work when blocked or at a boundary.

- **P01 — Bootstrap:** Establish the named/current lane, verify remote/relevant commits, read only relevant repo instructions, choose one smallest unfinished slice, implement, focused-test, commit, STOP.
- **P02 — Next:** Verify remote, complete exactly one smallest meaningful unfinished slice in the current lane, focused-test, commit, STOP. If none remains, report boundary reached.
- **P03 — Audit:** NO implementation. Audit current lane against current remote + relevant MBL/checklist. Report HEAD, complete state, meaningful unblocked work, blockers, local verification, BOUNDARY REACHED/NOT REACHED, and CONTINUE/PARK.
- **P04 — Resume audit:** Verify remote, use the latest audit as boundary map, complete exactly one smallest audited unblocked slice, focused-test, commit, STOP.
- **P05 — Hung recovery:** Stop/recover the current long-running slice; preserve valid work; do not repeat expensive commands; use narrow verification; finish only the active slice if safe; commit and STOP. Never destructively discard parallel/current work.
- **P06 — Route:** NO implementation. Determine the best currently unowned unfinished lane, prioritizing established work that unblocks multiple systems. Report lane, why, head, unblocked/blocked work, first slice.
- **P07 — Verify:** NO new feature. Verify current completed work with the narrowest sufficient tests/build. Fix only defects caused by that work if necessary; commit fix if any; STOP.
- **P08 — Finish:** Freeze scope. Finish only the currently active slice, focused-test, commit, STOP.
- **P09 — Lane guard:** Do not modify another active lane. Use existing contracts or mark dependency blocked. Complete one independent owned slice if available; otherwise report boundary reached.
- **P10 — Reconcile:** Remote moved. Compare remote vs working changes, preserve both valid sides, reconcile only active-slice overlap, focused-test, commit safely, STOP. No force/destructive discard.
- **P11 — Test hang:** Stop the expensive verification command; freeze implementation; use the smallest valid test/filter/project/build target; fix only slice-caused failures; commit if validated; STOP.
- **P12 — Park:** NO new feature. If needed, synchronize only relevant tracker/handoff docs, commit that sync, report final head/status/blockers/local verification, STOP.
- **P13 — Recheck:** NO implementation. Recheck whether a parked lane's known blockers are genuinely removed by current remote state. Report unblocked now, still blocked, REACTIVATE/KEEP PARKED, and first slice if reactivated.
- **P14 — Integration check:** NO implementation. Establish producer authority, consumer, existing/minimal boundary, persistence/idempotency needs, focused tests, and smallest integration slice. Avoid duplicate authority/circular dependencies.
- **P15 — MSFS gate:** NO speculative implementation. Identify exactly what requires real MSFS validation, minimum scenarios, expected evidence/state transitions, pass conditions, and failure evidence; STOP.

Additional text after a command narrows/modifies that invocation when compatible, e.g. `P07 only check SQLite persistence changes`.

The expanded reference versions remain in `docs/ai/prompt-library.md`, but normal P-command execution must not load that file.
