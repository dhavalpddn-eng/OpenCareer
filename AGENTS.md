# OpenCareer AI Agent Instructions

## Prompt command router

OpenCareer's canonical AI development prompt library is located at:

`docs/ai/prompt-library.md`

When the user sends a prompt command matching `P01` through `P15` (case-insensitive), treat it as an instruction to read the current version of `docs/ai/prompt-library.md` and execute the matching prompt using the current chat's lane, repository state, audit context, blockers, and active ownership boundaries.

Examples:

- `P02` — execute the current-lane Next Slice workflow.
- `P03` — execute the Boundary Audit workflow.
- `P05` — execute Hung Chat Recovery.
- `P12` — execute Completion / Park.
- `P13` — recheck whether the current parked lane has become unblocked.

Rules:

1. Always use the CURRENT remote repository state as truth.
2. Read the prompt library entry at execution time; do not rely on a remembered older copy.
3. Fill placeholders from the current chat/repository context when they are known.
4. If a required placeholder cannot be determined safely, ask only for the missing information rather than inventing it.
5. A short command such as `P03` is sufficient; the user does not need to paste the full prompt.
6. Additional text after the command modifies that invocation when compatible. Example: `P07 only check the SQLite persistence changes`.
7. Prompt commands do not override lane ownership, anti-hang rules, repository architecture, or safety constraints.
8. If this file and the prompt library disagree about workflow details, the current prompt-library entry controls the command behavior.
