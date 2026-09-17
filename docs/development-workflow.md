# OpenCareer development workflow

Working sequence for bounded tasks and fresh chats. AGENTS.md governs architecture; [project state](project-state.md) records current progress; [backlog](development-backlog.md) holds detailed open items.

## Chapter 1 — Project continuity and working rules

Status: handoff and domain test foundation established.

At the start of each task:
1. Read AGENTS.md and project-state.md.
2. Confirm remote branch head; inspect only files relevant to the current chapter/task.
3. Choose one complete, reviewable change with observable acceptance criteria.
4. Preserve unrelated work; use the feature branch.
5. Implement, run relevant verification, and fix failures.
6. Commit code and update project-state.md with results, limitations and the next task.

Keep one bounded task per chat where practical. Consult past chats only for necessary missing decisions. Do not repeat full-repository audits or unrelated tests without a concrete reason. Runtime/API changes require current official documentation. Use Wolfram for calculations that benefit from independent verification, not routine edits.

Exit gate: a new chat can identify the current branch, implemented features, unresolved limitations and next task from repository files alone.

## Chapter 2 — Windows application shell

Status: next chapter; not implemented.

Purpose: launch a usable companion without requiring MSFS.

Visual reference: [dashboard concept](ui-concept.md). Read only for UI tasks; the populated concept is a later target, while this chapter implements the shell and empty/disconnected states.

Tasks:
- Create OpenCareer.App using WinUI 3, .NET 10 and Windows x64.
- Add NavigationView, connection status and an empty Current Flight screen.
- Separate Views/ViewModels from domain and application services.
- Add startup error handling and useful logging.
- Display Waiting/Disconnected without blocking the UI.

Exit gate: build and launch on Windows with MSFS closed; navigation works; no startup crash. Domain tests remain green. A Linux domain build does not satisfy this gate.

## Chapter 3 — Simulator connection and telemetry

Status: not implemented. Depends on Chapter 2.

Purpose: reliably observe the installed simulator and aircraft.

Tasks:
- Create isolated OpenCareer.SimConnect adapter and mockable application boundary.
- Serialize connection operations; handle cancellation, disconnect and reconnect.
- Normalize telemetry, including missing values, aircraft identity, pause, slew and simulation rate.
- Bound telemetry handoff and use adaptive sampling independently of UI refresh.
- Identify the user's installed F-22 at KRME without inventing add-on-specific capabilities.

Exit gate: connect, disconnect, restart MSFS and reconnect without duplicate subscriptions or frozen UI. Demonstrate missing-value handling with automated tests and live Windows verification.

## Chapter 4 — Flight detection and durable session recovery

Status: not implemented. Depends on Chapter 3.

Purpose: safely record long flights and interruptions.

Tasks:
- Implement explicit flight/ground states with hysteresis and multiple signals.
- Handle bounce/recontact, pause, slew, aircraft changes and simulator faults.
- Create FlightSession milestones and flight summaries.
- Add versioned SQLite schema, parameterized access, atomic checkpoints and previous-valid-save recovery.
- Recover career/session state after app or simulator interruption without duplicate effects.

Exit gate: verified takeoff-to-parking/shutdown session is saved and recovered. Test noisy telemetry, crashes and repeated events. Support 1–3-hour sessions and six-hour cases using replay; separately prove live behavior. Do not claim MSFS aircraft-state restoration merely because career data reloads.

## Chapter 5 — Aircraft registry and feasible dispatch

Status: basic capability models exist; registry/planner not implemented. Depends on Chapter 4.

Purpose: offer work that the actual aircraft can perform.

Tasks:
- Build installed-aircraft registry without a supported-model whitelist.
- Record performance source and confidence.
- Separate civilian ownership, government access and military assignment.
- Validate runway, weight, payload, fuel, range, weather and margins before offers.
- Use installed scenery/facilities where available.
- Verify F-22 military eligibility separately from civilian purchase eligibility.

Exit gate: feasible routes pass; incompatible or unknown-critical-performance cases are rejected with clear reasons. No impossible job appears as ready to accept.

## Chapter 6 — Home airport, employment and playable missions

Status: contracts, location guards and duration preferences exist; orchestration/job generation not implemented. Depends on Chapter 5.

Purpose: complete the first end-to-end career operation.

Tasks:
- Add career creation and home-airport selection, starting tests at KRME.
- Connect local job acceptance/departure to pilot and aircraft locations.
- Generate jobs from feasible candidates and economic demand.
- Add briefing, active mission, result and logbook flows.
- Settle mission money, reputation and travel once in an atomic transaction.
- Provide a viable employee path; include authorized military training for the F-22 scenario.

Exit gate: accept a feasible local job, fly it, validate its objectives, park/shut down, settle once and reload the result. Landing alone cannot complete the mission. No teleporting or duplicate payout.

## Chapter 7 — Persistent economy, absence and recovery

Status: deterministic world simulation and policy rules exist; career integration incomplete. Depends on Chapter 6.

Purpose: make the world persistent without turning absence into punishment.

Tasks:
- Connect demand, labor, fuel, maintenance capacity and events to job supply and operating costs.
- Keep temporary disruptions separate from permanent structural capacity.
- Integrate protected absence for personal bills, passive income and personal deadlines.
- Add warning/restructuring/recovery flows before bankruptcy execution.
- Preserve employee work after financial failure.
- Implement inflation explicitly rather than labeling generic price movement as inflation.

Exit gate: equivalent elapsed-time requests produce equivalent world state; long absence/reload cannot duplicate payments or create protected-period debt. Repeated active financial mistakes can lead to recoverable bankruptcy.

## Chapter 8 — Dealers, credit, ownership and storage

Status: credit/dealer quotes and progression examples exist; transactions/UI not implemented. Depends on Chapter 7.

Purpose: turn career performance and savings into meaningful ownership choices.

Tasks:
- Derive credit inputs from settled career history.
- Persist dealer inventory, new/used condition, relationships and issued discounts.
- Connect lender comparison, affordability and cash-purchase decisions to UI.
- Atomically consume stock, debit deposit/cash, originate loans and transfer ownership.
- Add repayment schedules, default/restructuring handling and duplicate-payment protection.
- Require local storage availability, costs and delivery/reposition arrangements.
- Balance 50–80 real flying hours toward a small cash purchase or larger financed purchase; never guarantee approval from hours alone.

Exit gate: cash and financed purchases survive reload; repeated purchase requests cannot duplicate aircraft or loans; expired/sold offers fail safely; reserves and absence protections remain intact. Validate balancing with actual career income/cost scenarios.

Detailed rules: [credit and dealers](credit-and-dealers.md).

## Chapter 9 — Maintenance, company growth and route expansion

Status: mostly planned. Depends on Chapter 8.

Purpose: create ongoing ownership decisions and sustainable expansion.

Tasks:
- Persist wear separately from damage; use supported simulator inputs and documented fallbacks.
- Default routine management to automation.
- Require credible manual-ground evidence and settle bounded rewards once per flight.
- Add airport relationships, route access, scarce storage and additional bases.
- Add NPC employees with funded, bounded operations and no automatic uncontrolled offline borrowing.
- Show operating margins and risks before expansion.

Exit gate: maintenance, staffing, routes and facilities affect availability and profit coherently. Manual procedures remain optional. Expansion cannot bypass geography, safety or protected absence.

## Chapter 10 — Airport variety, military careers and special events

Status: event engine and mission categories exist; distinct playable objectives mostly planned. Depends on the Chapter 6–9 systems required by each feature.

Purpose: make different airports and careers play differently.

Tasks:
- Maintain provenance-bearing public/military/defense-support airport classifications.
- Preserve KRME's civilian/government/UAS mix and defense-support role.
- Weight heavy military airports appropriately without inventing real operations.
- Add distinct training, escort, recon/surveillance, delivery, ferry/transport and public-sector objectives.
- Add military/reserve career onboarding and qualifications.
- Connect rare events to relevant jobs, resources and operational restrictions.
- Keep optional AI briefings outside authoritative simulation decisions.

Exit gate: each mission family has its own completion/failure tests, valid aircraft/authorization filters and coherent rewards. Events affect only intended locations/markets; military assignments do not become civilian-owned assets.

## Chapter 11 — Balance, performance and release readiness

Status: not complete. Applies incrementally; final gate follows a playable career loop.

Purpose: prove reliability, economic pacing and usable gameplay.

Tasks:
- Run representative 1-, 3- and 6-hour missions, long absences and recovery scenarios.
- Measure ownership progression for strong, ordinary and struggling careers.
- Check dealer discounts, borrowing and manual rewards for exploitable loops.
- Profile UI responsiveness, telemetry queues, memory, CPU and database writes alongside MSFS.
- Test installation, upgrades, migrations, backups and failure messages on Windows.
- Conduct playtests for repetitive jobs, meaningful airport choices and viable employment.
- Prototype optional EFB/route synchronization only with verified SDK support and a .PLN fallback.

Exit gate: documented Windows/MSFS evidence, no unresolved critical save/settlement defects, measured runtime impact and confirmed playable progression. Passing headless tests alone does not establish performance or engagement.

## Chapter completion record

Mark a chapter complete only when its exit gate is met. Record:
- What changed and the implementation commit.
- Automated test results and any live verification.
- Unresolved limitations.
- The next bounded task.

Do not copy whole chat histories or large logs into the handoff. Keep detailed evidence in the relevant feature document or CI run.
