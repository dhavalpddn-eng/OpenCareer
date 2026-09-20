# OpenCareer mission tutorial system

Status: accepted product/tutorial design.

Purpose: make every unfamiliar mission family teachable the first time the player encounters it, without turning the entire career into a forced training mode.

This system sits beside the general app intro tutorial. The app intro teaches where things are and how a normal job works. Mission tutorials teach procedures that are unique to a mission family or unusual operating environment.

## Three tutorial layers

OpenCareer uses three distinct tutorial layers:

1. **App onboarding**
   - what OpenCareer is,
   - navigation,
   - simulator connection,
   - career concepts,
   - basic flight/checklist behavior.

2. **First-job onboarding**
   - browse a job,
   - understand eligibility,
   - dispatch,
   - accept,
   - prepare,
   - fly,
   - park/shutdown,
   - review debrief and settlement.

3. **Mission-family tutorials**
   - trigger the first time a player accepts a mission with unique procedures,
   - explain mission-specific setup, execution and completion,
   - use live checklist/evidence where MSFS exposes trustworthy state,
   - remain replayable later from Help/Training.

Completing the app intro does not mark mission-family tutorials complete.

## Trigger rules

A mission tutorial appears when all are true:

- the mission family has a tutorial definition,
- the player has not completed the current tutorial version for that mission family,
- the accepted mission actually requires the special procedure,
- the tutorial is not explicitly disabled by the player.

The player may:

- start the guided tutorial,
- view a short briefing only,
- skip it for this mission,
- mark it learned,
- reopen it later.

Skipping a tutorial does not bypass mission requirements.

Tutorial completion is versioned independently by mission family. If a mission procedure materially changes, only that tutorial can reappear.

## Live guided behavior

Mission tutorials reuse the Current Flight checklist system.

Each step may contain:

- instruction,
- reason,
- target state,
- current observed state,
- Controller binding,
- Keyboard binding,
- completion method,
- optional diagram/illustration,
- failure/recovery guidance.

Completion modes:

- **Telemetry verified** — auto-complete when normalized simulator evidence proves the required state.
- **OpenCareer verified** — complete from authoritative mission/application state.
- **Manual confirm** — player confirms when no trustworthy state is available.
- **Instructor cue** — informational step that does not gate progress.

Do not invent SimVars or claim OpenCareer can verify an aircraft-specific mechanism until that integration has been validated.

## Input binding display

Every actionable tutorial step shows both input paths inline when known.

```text
[ ] Extend hook
    Controller: D-pad Left
    Keyboard: H

[ ] Arm mission system
    Controller: UNBOUND
    Keyboard: Shift + M
```

Rules:

- Controller and Keyboard are resolved independently.
- **UNBOUND** is amber/orange and never blank.
- **UNBOUND — REQUIRED** is red when a required/safety-critical step has no usable mapping.
- **Binding unavailable** is amber/orange when OpenCareer cannot reliably resolve the player's configured mapping.
- **Cockpit only** is shown when the action must be performed through the virtual cockpit or aircraft-specific UI.
- **No input required** is shown for observation/automatic steps.
- Never invent a binding.

## Tutorial timing

A mission tutorial is phase-aware. It does not dump the whole procedure at mission acceptance.

Typical structure:

```text
Briefing
-> setup
-> departure
-> mission ingress
-> special procedure
-> recovery/egress
-> landing/arrival
-> shutdown/debrief
```

Only the current and immediately upcoming steps should dominate the UI. The full procedure remains viewable.

## Standard unique mission tutorials

The following mission families require dedicated first-time tutorials when implemented.

### Banner tow

Teach:

- aircraft/mission eligibility,
- banner pickup area and pickup direction,
- required setup before the pickup run,
- safe approach to the pickup line,
- hook/pickup action where supported,
- confirmation that the banner was successfully acquired,
- climb-out after pickup,
- tow speed/altitude/route constraints,
- release/drop procedure,
- return and landing.

The checklist should make the unusual pickup sequence explicit. If banner attachment cannot be verified from supported simulator data, OpenCareer uses its own mission state/manual confirmation rather than pretending telemetry proves it.

### Aircraft carrier operations

Separate tutorials for:

- **carrier takeoff**,
- **carrier landing**.

Carrier takeoff teaches:

- carrier/deck start context,
- launch area/catapult or short-deck procedure only when actually supported by the aircraft/environment,
- aircraft configuration,
- launch readiness,
- power/application sequence,
- deck departure,
- climb-away requirements.

Carrier landing teaches:

- approach setup,
- landing configuration,
- carrier alignment,
- approach target/cueing where OpenCareer can derive it,
- touchdown zone,
- arrest/recovery logic only when supported,
- bolter/go-around behavior,
- successful recovery confirmation,
- deck-safe/parked state.

OpenCareer must not invent carrier-arresting telemetry. Where MSFS/add-on state is unavailable, the mission evaluator uses defensible position/flight evidence plus explicit/manual mission cues.

### Glider tow

Teach:

- tow aircraft/glider relationship,
- tow connection/setup,
- takeoff behavior,
- climb profile,
- release area/altitude,
- release action,
- separation,
- return/landing.

### Skydiving

Teach:

- passenger/load state,
- climb to jump altitude,
- jump-run alignment,
- speed/altitude constraints,
- jump authorization/release,
- safe separation,
- descent/return.

### Firefighting

Teach:

- tank/load state,
- refill/load procedure where applicable,
- ingress,
- drop zone,
- altitude/speed/track constraints,
- drop action,
- confirmation/effect scoring,
- repeat/refill or return.

### Agricultural application

Teach:

- product/load state,
- field/target boundary,
- run direction,
- working altitude/speed,
- spray/application activation,
- turn/reposition behavior,
- coverage progress,
- completion.

### Search and rescue

Teach:

- search area,
- search pattern,
- altitude/speed expectations,
- target-detection/cue rules,
- orbit/mark/confirm steps,
- pickup/landing/hoist step only when supported by the mission/aircraft,
- return.

### Medevac / organ transport

Teach:

- patient/cargo urgency,
- loading confirmation,
- dispatch/timing constraints,
- smooth-flight/safety priorities where scored,
- destination handoff,
- terminal conditions.

### Survey / photography / mapping

Teach:

- survey lines or orbit,
- required altitude/speed/heading tolerance,
- sensor/capture action where modeled,
- progress by leg/area,
- missed-strip recovery,
- completion.

### Pipeline / power-line patrol

Teach:

- corridor entry,
- altitude/speed constraints,
- route following,
- observation/inspection events,
- anomaly/report actions,
- completion.

### Sightseeing

Teach:

- passenger boarding,
- route landmarks,
- altitude/speed/comfort constraints where applicable,
- required viewing/orbit segments,
- return.

### Ferry / reposition

Usually uses the normal flight tutorial, but first-time ferry onboarding explains:

- aircraft location changes are persistent,
- destination matters,
- repositioning is not teleportation,
- completion requires the mission-defined terminal state.

### Cargo

Teach:

- named cargo manifest,
- weight/value/handling constraints,
- loading confirmation,
- route/condition requirements,
- unloading/handoff,
- damage/loss consequences only where supported.

### Passenger / charter

Teach:

- passenger manifest,
- departure readiness,
- timing/comfort/safety constraints where used,
- destination arrival,
- passenger handoff/terminal state.

### Helicopter external-load / sling missions

Only when a supported aircraft/environment can provide enough evidence.

Teach:

- hover/setup,
- load acquisition,
- stable lift,
- transit limits,
- placement/drop zone,
- release,
- return.

Never fake sling-load state from generic aircraft telemetry.

### Offshore / ship / helipad operations

Teach:

- landing area identification,
- approach path,
- wind/obstacle considerations when authoritative data exists,
- stabilized hover/approach,
- touchdown/landing zone confirmation,
- departure.

### Emergency/disaster logistics

Teach:

- special airport/zone restrictions,
- priority cargo/passenger,
- damaged/limited infrastructure,
- mission-specific arrival/handoff,
- alternate/diversion logic.

### Government surveillance / reconnaissance

Teach:

- authorization context,
- target/area ingress,
- orbit/track requirements,
- altitude/speed/position tolerances,
- observation duration,
- mission exit/return.

### Patrol / intercept / escort

Teach:

- authorization,
- rendezvous/intercept geometry,
- required proximity/relative-position windows,
- escort route/formation envelope where modeled,
- disengagement/return.

OpenCareer evaluates mission geometry from real player telemetry and its own deterministic mission state; it does not imply MSFS provides native intercept/combat objectives.

### Conflict-operation tutorials

For later fictionalized conflict missions, create separate tutorials for each retained mission family, such as:

- reconnaissance,
- logistics,
- escort,
- patrol/intercept,
- CAS-style simulated support,
- SEAD-style simulated support if retained.

These tutorials must clearly identify OpenCareer-simulated threats/effects as simulated game state.

## Tutorial variants

One mission family may have multiple tutorial variants when the procedure changes materially.

Examples:

- banner pickup from a ground rig vs another supported setup,
- fixed-wing carrier takeoff vs VTOL/STOVL carrier departure,
- arrested carrier landing vs vertical deck recovery,
- helicopter SAR landing vs supported hoist workflow,
- firefighting water pickup vs airport refill.

Variants share common steps but track completion separately when the player must learn a different procedure.

## Pre-mission briefing

Before the first mission of a unique family, show a compact briefing:

- what makes this mission different,
- what unusual controls/actions are required,
- whether any required Controller/Keyboard actions are unbound,
- what OpenCareer can auto-verify,
- what requires manual confirmation,
- failure/recovery conditions,
- approximate special-procedure phase.

Required unbound inputs should be identified before the player launches.

## Practice/training mode

Where practical, unique procedures should support a non-career or employer-training practice path before a paid/authoritative mission.

Practice:

- does not award normal mission settlement,
- may log flight experience where ordinary rules permit,
- can mark the tutorial learned,
- does not bypass license/qualification requirements for real jobs unless the career system explicitly grants that qualification.

## Failure and recovery teaching

Tutorials must explain recoverable failure states instead of only showing success.

Examples:

- missed banner pickup -> climb away and reset for another run,
- carrier bolter -> go around and re-enter,
- missed survey line -> re-fly only the missed segment,
- firefighting drop outside tolerance -> continue/reposition according to mission rules,
- glider release missed -> continue to safe release/reset condition.

Tutorial guidance never silently rewrites mission completion rules.

## Persistence

Persist:

- mission tutorial family ID,
- variant ID,
- tutorial version,
- completed/skipped state,
- last completed step if resumable,
- preferred tutorial verbosity.

Tutorial completion is preference/training state, not mission completion state.

## Settings

Add tutorial controls:

- Show app intro on new profile,
- Show first-job tutorial,
- Show first-time mission tutorials,
- Show checklist every flight,
- Show Controller/Keyboard bindings on checklist steps,
- Tutorial detail: Full / Compact,
- Reset all tutorials,
- Reset selected mission tutorial.

Resetting tutorials never resets career progress.

## Architecture

Recommended model:

```text
Mission definition
    -> tutorial family/variant key
    -> mission tutorial catalog
    + binding resolver
    + normalized telemetry/evidence
    + authoritative mission state
    -> tutorial coordinator
    -> Current Flight checklist/tutorial ViewModel
    -> WinUI / optional EFB projection
```

The tutorial system observes mission state; it does not become the authority for mission success.

Suggested interfaces:

```csharp
public interface IMissionTutorialCatalog
{
    MissionTutorialDefinition? Get(string familyId, string? variantId);
}

public interface IInputBindingResolver
{
    InputBindingState Resolve(string actionId, InputDeviceKind device);
}

public interface IMissionTutorialProgressStore
{
    MissionTutorialProgress Get(string familyId, string? variantId);
    void Save(MissionTutorialProgress progress);
}
```

Do not put SimConnect access directly in tutorial ViewModels.

## Acceptance criteria

- A player can finish the app tutorial and still receive a separate first-time banner-tow tutorial later.
- A player can receive separate first-time carrier takeoff and carrier landing tutorials.
- Every unusual mission family has a dedicated tutorial definition before that mission family is considered production-ready.
- Every actionable step displays Controller and Keyboard bindings or an explicit status.
- Missing bindings are never blank.
- Live-verifiable steps auto-complete only from trustworthy evidence.
- Unsupported aircraft-specific actions remain manual/cockpit-only rather than falsely verified.
- The tutorial explains recovery from common mistakes.
- All mission tutorials can be reopened later.
- Tutorial completion never grants mission completion, money, ownership, qualification or reputation.
