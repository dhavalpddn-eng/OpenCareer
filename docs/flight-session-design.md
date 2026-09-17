# Flight session, logging and recovery design

Status: accepted design baseline for Chapter 4. Implementation remains gated on the real Windows/MSFS telemetry validation in `docs/simulator-connection.md`.

## Core model

OpenCareer separates four concepts that must not be conflated:

1. **Simulator observation** — OpenCareer has a valid aircraft/flight loaded and is collecting telemetry.
2. **FlightSession** — one continuous player session with one aircraft identity. It can contain one or more legs.
3. **FlightLeg** — one departure-to-arrival segment within the session.
4. **Logbook entry** — a completed free/practice flight the player chooses to commit, or a career/job flight that is committed by the authoritative settlement flow.

Loading into the simulator may start observation and a provisional FlightSession, but it does **not** immediately start flight-hour credit.

A job landing never completes a mission by itself. The mission owns its completion profile and settlement remains a separate atomic operation.

## Load-in and preflight grace

The user wants tracking to begin when the aircraft loads, with tolerance for slow aircraft/scenery loading.

The implementation should therefore:

- observe `FlightLoaded`, `AircraftLoaded`, `SimStart`/`SimStop` where reliable,
- require valid, stable aircraft identity and telemetry before arming the session,
- tolerate loading screens and temporary telemetry gaps without manufacturing taxi/flight time,
- store load time separately from loggable movement time,
- support valid spawn-on-ground and mission-authorized spawn-in-air cases,
- tune stability windows from the live F-22/KRME trace rather than hard-coding thresholds before observation.

MSFS also exposes `PositionChanged`, which should be captured as evidence of a user position change rather than silently accepting it as flown distance.

## Real-world-inspired flight time

FAA flight-time semantics are the realism baseline, not a claim that OpenCareer is a legal logbook. For powered aircraft, flight time begins when the aircraft moves under its own power for the purpose of flight and ends when it comes to rest after landing. This naturally includes taxi.

OpenCareer should record separate clocks:

- **Observed wall time** — diagnostic elapsed real time while the session is attached.
- **Simulated operational time** — active simulator elapsed time after accounting for simulation rate and pause.
- **Movement/flight time** — starts at credible self-powered movement for flight and ends at rest after landing.
- **Airborne time** — actual airborne intervals.
- **Taxi-out / taxi-in time** — ground movement around each leg.
- **Career-credit time** — the value used for licensing, aircraft-family experience and the 50–80 real-flying-hour economy calibration.
- **Paused time**, **accelerated time** and **slew time** — reported separately.

### Time acceleration

Time acceleration is allowed. It must not multiply progression hours.

Provisional deterministic rule:

```text
career_credit_delta = wall_active_delta * min(simulation_rate, 1.0)
```

Only intervals that are valid, unpaused, non-slew and attached to the same aircraft are eligible.

Consequences:

- 1 real minute at 1x = 1 credit minute.
- 1 real minute at 4x or 8x = 1 credit minute.
- 1 real minute at 0.5x = 0.5 credit minute.
- pause or slew = 0 credit minutes.

This preserves the user's ability to accelerate long cruise without turning acceleration into an experience exploit. The logbook can still show the larger simulated operational duration.

Independent calculation example: 30 min at 1x, 30 min at 4x, 20 min at 0.5x and 10 min at 8x equals 90 real active minutes and 240 simulated minutes, but 80 career-credit minutes.

MSFS 2024 officially exposes simulation speed in `SIMCONNECT_RECV_EVENT_FRAME.fSimSpeed`; the exact efficient production sampling method remains to be chosen after the live runtime gate. Do not add a high-frequency frame subscription merely to solve this before profiling it.

## Departure and arrival identity

Usually the airport where the player spawns and takes off is also the departure airport. OpenCareer still stores these separately because they can differ.

Store:

- spawn/load position,
- planned departure airport from the job/flight plan,
- detected actual takeoff airport/runway,
- planned destination,
- detected actual arrival airport/runway.

For normal airport departures, **actual departure is the airport/runway associated with the takeoff**, not merely the nearest airport at load time. A job validates that actual departure against its planned departure with an airport/scenery tolerance.

Off-airport helicopter, bush, military or special missions can use a named landing zone or coordinates instead of forcing an airport identifier.

## Sessions and multi-leg operations

A `FlightSession` can contain multiple `FlightLeg` records.

This matches real-world practice better than forcing every stop into a new app session and supports airline, cargo, ferry and training work.

Examples:

- KRME -> KSYR cargo: one session, one leg.
- Airline route A -> B -> C: one session, two planned legs.
- Cargo milk run A -> B -> C -> D: one session, three planned legs.
- Free flight with a full-stop landing at B, then another departure without ending the session: a new leg may begin inside the same session.
- A touch-and-go remains an operation/event inside the flight rather than forcing a session split.

FAA legal interpretations permit a pilot to treat multi-stop travel as one flight with segments in appropriate logging contexts. OpenCareer uses an explicit parent-session/child-leg model so the data remains unambiguous.

## Takeoff and landing episodes

Never infer takeoff or landing from one `OnGround` edge.

### Takeoff

Use multiple signals and hysteresis, such as:

- on-ground state,
- ground/indicated speed,
- AGL trend,
- vertical speed,
- sustained airborne time,
- position progression.

Exact thresholds are tuned from live aircraft traces and must tolerate aircraft-specific SimVar behavior.

### Landing and bounce handling

The first credible touchdown opens one **landing episode**. Short bounce/recontact cycles remain part of that same landing.

A new landing episode is created only after evidence of a real go-around/new approach, such as sustained airborne time plus meaningful climb/AGL separation. This prevents a bounced landing from appearing as three landings.

Record every touchdown sample needed for postflight analysis, but report one landing result for a bounce sequence.

### Touch-and-go / stop-and-go

Touch-and-go is a legitimate arrival/departure operation and is not automatically a penalty.

OpenCareer records:

- touchdown,
- whether a full stop occurred,
- subsequent takeoff,
- whether the mission/training objective called for it.

Some real-world requirements distinguish any landing from a **full-stop** landing, so those are separate facts in the model.

Training missions can award proficiency/qualification credit for correctly executed touch-and-go work. Normal passenger/cargo missions should not provide a farmable bonus simply for adding unnecessary circuits.

## Safety-over-schedule decisions

A safe go-around, diversion or emergency landing should not be treated like careless mission failure.

When supported by credible simulator/mission evidence:

- go-arounds can be neutral or safety-positive,
- an emergency diversion can preserve or improve safety reputation,
- passenger/cargo outcomes and contract economics can still change,
- self-created or repeatedly farmed "emergencies" must not generate rewards.

Mission scoring should distinguish **safe operational judgment** from **meeting the original schedule**.

## Default completion rule

Jobs may define their own completion profile.

The default conventional airport job completes flight operations only after all required conditions are satisfied, normally:

- correct destination/terminal area,
- aircraft safely on ground,
- taxi to acceptable gate/ramp/parking,
- stationary,
- parking brake set where appropriate,
- engines shut down,
- state stable for a configured confirmation window.

Helicopter, bush, carrier, military, rapid-turn, medevac or other special jobs can replace inappropriate conditions. The mission still validates its own cargo/passenger/objective requirements before settlement.

## Disconnect, crash and resume

A connection loss suspends the active session; it does not immediately discard it.

Checkpoint:

- session/leg IDs,
- aircraft identity,
- last credible position,
- origin/destination,
- state-machine state,
- time accumulators,
- fuel baseline/current value,
- route summary,
- landing/takeoff milestones,
- assistance flags,
- mission evidence already observed,
- settlement/idempotency identifiers.

On reconnect, resume automatically when aircraft identity, position and continuity are plausible.

If identity changed or position jumped beyond the allowed recovery envelope:

- do not silently resume the old mission,
- preserve the interrupted record,
- require an explicit recovery/abandon path,
- never duplicate rewards.

OpenCareer can recover career/session data after a simulator crash. It must not claim it can restore the aircraft to its previous in-flight MSFS state unless a separately verified simulator mechanism is implemented.

## Slew, teleport, pause and aircraft changes

### Pause

Full pause and Active Pause produce no time, distance, fuel-use inference or progression credit for the paused interval.

### Slew

Slew is allowed for setup/recovery, but is not a teleport loophole.

While slew is active:

- career time is frozen,
- route distance is frozen,
- job progress is frozen,
- movement is tagged as assisted.

On slew exit compare entry/exit position. A material displacement beyond a tuned tolerance invalidates flown-route evidence for a normal career job. Free-flight records may remain viewable but must be flagged as assisted.

### Position changes / teleport

A `PositionChanged` event or implausible position jump is explicit evidence. Normal career jobs do not accept teleported route progress.

### Aircraft change

Changing aircraft during an active job is not allowed.

- active career job: suspend/terminate according to mission policy; never carry progress into the new aircraft,
- free flight: close the prior provisional session and start a new observation/session after the replacement aircraft stabilizes.

## Free flight and practice

Free/practice flights are supported and useful for progression.

OpenCareer tracks them provisionally. At completion the player can choose **Log flight** or **Discard from pilot logbook**.

A valid logged free/practice flight can contribute to:

- total career-credit hours,
- category/class/family experience,
- exact-model/type experience when known,
- takeoff/landing counts,
- training and qualification prerequisites where applicable.

Discarding a free flight must not erase real consequences for a career-owned aircraft. Fuel consumption, wear, damage and maintenance exposure are physical asset effects and remain authoritative if the owned aircraft was actually operated.

## Aircraft-experience progression

Do not reduce aircraft experience to an XP level.

The future experience ledger should be able to aggregate:

- total flight hours,
- aircraft category/class,
- propulsion/complexity traits,
- aircraft family,
- exact model/type where reliably identified,
- recent experience,
- takeoff/landing experience,
- day/night/IFR or mission-specific qualifications when those systems exist.

This supports the user's goal that moving into a newer/larger/more complex aircraft can depend on meaningful experience in similar aircraft.

Experience may affect employer checkout, rental eligibility, insurance, financing or operational authorization. Buying an aircraft should not be blocked by an arbitrary level when a more realistic training/insurance/lender rule fits.

## Postflight record

The first durable `FlightSession` summary should preserve enough data to display:

### Time and route

- load/session time,
- movement/flight time,
- taxi-out time,
- airborne time,
- taxi-in time,
- simulated time,
- career-credit time,
- paused/accelerated/slew time,
- flown distance,
- route map/polyline,
- departure/destination and intermediate legs.

Do not persist every high-rate telemetry frame. Keep a decimated route track plus high-resolution windows for takeoff/landing events.

### Aircraft and fuel

- aircraft identity/family when known,
- start/end fuel,
- fuel used,
- weight/payload baselines where reliable,
- engine/airframe wear inputs,
- assistance/telemetry-confidence flags.

### Landing and handling

- landing rate / vertical speed near touchdown,
- touchdown G,
- touchdown speed,
- pitch/bank near touchdown,
- bounce count within the landing episode,
- hard-landing classification,
- maximum positive/negative G,
- overspeed duration/events,
- other validated violations.

### Runway

- detected runway,
- runway available length,
- touchdown point where derivable,
- actual landing ground roll / runway used where derivable,
- takeoff roll where derivable,
- trusted predicted takeoff/landing distance required,
- remaining runway margin in distance and percent.

Never fabricate "how much runway you should have used." If the installed aircraft lacks a trusted performance profile, show actual observations and mark predicted-required distance unavailable/low-confidence.

### Passenger, cargo and economy

- passenger manifest/result where applicable,
- named commodity manifest,
- cargo quantity/mass/condition,
- shipment declared value,
- origin/destination market snapshots,
- freight payment,
- reputation changes,
- violations,
- maintenance/wear effects,
- final economic settlement.

See `docs/cargo-market-requirements.md`.

## Required simulator evidence before implementation

The current 1 Hz telemetry is only the first boundary. Chapter 4 will likely need additional verified evidence, including:

- aircraft identity/title/type,
- aircraft/flight loaded events,
- `SimStart` / `SimStop`,
- `PositionChanged`,
- simulation speed,
- crash/reset events where useful,
- higher-rate landing telemetry,
- additional speed/weight/control fields only when a concrete detector/scoring rule requires them.

The official SDK documents `AircraftLoaded`, `FlightLoaded`, `PositionChanged`, `SimStart`, `SimStop`, `Crashed`, `Pause_EX1`, and frame simulation-speed data. Exact native behavior still requires the live Windows/MSFS test.

## Test matrix for Chapter 4

Automated tests must cover at least:

- slow load / delayed telemetry arming,
- spawn on ground and authorized spawn in air,
- false ground-state flicker,
- taxi without takeoff,
- normal takeoff,
- rejected takeoff,
- normal landing,
- multi-bounce landing as one episode,
- go-around followed by a new landing,
- touch-and-go,
- stop-and-go,
- multi-leg job,
- free flight log/discard,
- pause and Active Pause,
- 0.5x / 1x / 2x / 4x+ acceleration accounting,
- slew without material displacement,
- slew/teleport with material displacement,
- aircraft change,
- SimConnect disconnect/reconnect,
- app restart from checkpoint,
- simulator crash/restart,
- duplicate takeoff/landing packets,
- mission completion requiring parking/shutdown,
- idempotent settlement after resume.

## Real-world / SDK references checked 2026-09-17

- FAA Safety Briefing, Nov/Dec 2014, flight-time interpretation: powered-aircraft flight time starts with self-powered movement for the purpose of flight and ends at rest after landing.
- FAA Glenn (2009) legal interpretation: a pilot may choose what is a discrete flight versus a segment in the discussed cross-country logging context.
- FAA JO 7110.65BB, 2025, §3-8-2: touch-and-go is treated as an arriving aircraft until touchdown and thereafter as departing.
- MSFS 2024 SDK `SimConnect_SubscribeToSystemEvent`: `AircraftLoaded`, `FlightLoaded`, `PositionChanged`, `Pause_EX1`, `SimStart`, `SimStop`, `Crashed`.
- MSFS 2024 SDK `SIMCONNECT_RECV_EVENT_FRAME`: exposes `fSimSpeed`, e.g. 4.0 at 4x simulation rate.

These references inform game semantics. OpenCareer is not a certified logbook or legal-compliance system.
