# Beginner flight learning and accessibility balance

Updated: 2026-09-19.

This is an isolated review/tuning document on `feature/economy-loan-balance-review`. It does not claim the complete tutorial UI or qualification system is already implemented.

## Product rule

A player who has never used a flight simulator must be able to learn OpenCareer without being trapped by landing smoothness, route-following knowledge, or repeated career punishment.

OpenCareer should teach flying progressively while still preserving meaningful consequences for genuinely unsafe operation.

## Beginner guidance modes

Default guidance is adaptive rather than time-only:

- **Full** guidance for the first 10 completed flights / first 20 career-credit hours, and whenever recent attempts show persistent difficulty.
- **Standard** guidance through roughly 25 completed flights / 50 career-credit hours, or whenever a player who would otherwise be on Light still shows meaningful recent difficulty.
- **Light** guidance only when the player has both enough experience and recent evidence that they are coping.

Guidance must be user-selectable and may remain enabled indefinitely. A player is never forced to lose route or landing assistance merely because they have accumulated hours.

## Route following

For ordinary career jobs, route perfection is not a mission-completion requirement.

Beginner navigation UX should provide, when the relevant data is available:

- planned route line;
- current aircraft position;
- next waypoint / destination bearing and distance;
- course-deviation direction;
- a clear **Return to route** action after getting lost;
- destination/airport marker;
- active-leg progress;
- optional autopilot/navigation setup coaching when the aircraft/profile supports it;
- a live checklist step explaining what to do next.

A missed waypoint or route deviation may affect efficiency/debriefing later, but it does not become an automatic career failure if the player safely recovers and reaches the intended destination.

If the player is genuinely lost, the mission should pause completion and guide them back rather than instantly fail.

Special missions that explicitly require a geographic path can use stricter route rules, but those rules must be stated in the briefing/tutorial.

## Landing learning

**A smooth landing is never required to complete an ordinary job.**

A safe but rough landing can complete the flight and produces coaching instead of a mission failure.

Examples that remain completion-capable when no actual unsafe outcome/damage occurs:

- firm touchdown;
- bounce/recontact sequence;
- imperfect flare;
- slightly off-center touchdown;
- go-around followed by a safe landing.

A safe go-around is treated as good judgment, not a failure.

Unsafe outcomes remain meaningful:

- crash;
- runway excursion;
- verified aircraft damage;
- other mission-specific safety violations.

During training/practice, those events do not damage long-term career reputation. Physical consequences for a career-owned aircraft remain authoritative; training mode is not a free aircraft-repair exploit.

## Landing coach behavior

Full/Standard beginner assistance should emphasize avoiding unsafe outcomes rather than chasing a perfect landing score.

When telemetry/profile confidence permits, the coach may show:

- runway alignment;
- altitude/AGL trend;
- sink-rate trend;
- bank/pitch trend;
- gear/flap state;
- go-around prompt;
- touchdown/debrief metrics after landing.

Aircraft-specific target approach/Vref speeds must only be shown when a trusted aircraft/performance profile supplies them. Do not invent approach speeds for unsupported aircraft.

## Checklists and controls

The beginner tutorial should use the live checklist design already requested for OpenCareer:

- each step shows the action;
- current telemetry marks it complete when observable;
- controller and keyboard bindings are shown beside the action when known;
- an unbound required control is shown clearly in orange/red rather than silently omitted;
- the checklist can be collapsed after the player is comfortable;
- unique mission types get their own first-use walkthrough.

SimConnect does not automatically imply that every custom MSFS control binding is discoverable. Binding display must come from a verified binding source or OpenCareer-managed mapping; never invent a key.

## First 50 beginner stress pass

Matrix: 10 beginner archetypes × 5 play styles, 30 attempts each.

Archetypes included absolute first flight, no navigation knowledge, rough lander, controller-only, keyboard/mouse, autopilot-dependent, anxious go-around pilot, checklist skipper, visual-VFR learner and slow learner.

Play styles included short practice, balanced jobs, route-heavy flying, landing practice and weekend long trips.

The initial pass found **8 harsh-learning failures**:

- guidance could fall to Light while the player was still visibly struggling;
- several controller/keyboard/rough-landing profiles accumulated too many punitive career consequences after the early tutorial period.

Notably, the initial pass did **not** show that rough landings themselves needed to fail jobs; the existing design direction of coaching safe rough landings was correct.

## Tuning applied

1. **Adaptive guidance instead of time-only fade.**
   - Recent difficulty can raise Light -> Standard or restore Full guidance.
   - A player with 80 hours can still receive Full guidance if the last six attempts show they are struggling.

2. **Training/practice is career-safe.**
   - Training crashes/unsafe outcomes do not create long-term career/reputation/economic punishment.
   - Physical aircraft damage remains a separate authoritative consequence when the player owns the aircraft.

3. **Stronger route-recovery support.**
   - Full and Standard guidance assume the player is given a highly visible route-rejoin path rather than being left lost.

4. **Landing assistance is tuned toward safety, not smoothness.**
   - Full/Standard coaching materially reduces the stress-model probability of an unsafe landing when the player follows the prompts.
   - It does not magically fly the aircraft and does not remove rough landings from the simulation.

## Fresh 59-scenario pass

The second matrix uses 59 fresh scenarios and independent deterministic seeds. It includes the original 50 style/archetype combinations plus nine additional edge profiles:

- no trim knowledge;
- cannot flare yet;
- taxi + route confused;
- autopilot-button learner;
- frequent go-around pilot;
- over-controlling controller user;
- keyboard user without good rudder control;
- long breaks between flights;
- absolute beginner whose goal is eventually flying airliners.

Acceptance gates:

- >=70% completion rate over the learning run;
- <=6 route-recovery retries;
- <=2 punitive career consequences;
- guidance never remains Light while recent difficulty is >=50%;
- rough-landings can achieve coached successful completion;
- smooth landing is never a mission-completion requirement;
- recoverable route deviations do not create career failure;
- go-arounds are safe/neutral choices;
- training/practice does not damage long-term career standing.

## Interpretation

The beginner experience should feel like **flight school with a career attached**, not a pass/fail airline checkride on flight one.

Players who already know flight simulation can reduce guidance immediately. Players who do not can leave the route/checklist/landing coach enabled for as long as they need without earning less money or less legitimate flight-time credit solely because they used the assistance.