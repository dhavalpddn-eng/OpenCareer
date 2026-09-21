# Flight-system research review — 2026-09-17

Purpose: record evidence gathered with the newly connected project tools and the design changes accepted from that review. This is a research handoff, not an implementation or live-MSFS acceptance record.

## Result

The existing FlightSession/FlightLeg direction is sound. The review adds four meaningful improvements:

1. **Pilot-log dimensions** — preserve day/night, cross-country, instrument-like time and event-level landing attributes instead of only total hours.
2. **Intent versus actuality** — store planned route/alternate separately from actual route/arrival/diversion and score safety outcome separately from contract outcome.
3. **Evidence provenance** — important derived facts carry confidence/source so unknown airport/aircraft data is never silently converted into mission truth.
4. **Adaptive telemetry persistence** — use state-dependent sample rates plus a bounded high-rate landing buffer rather than persisting a whole flight at touchdown frequency.

The accepted implementation rules are now in `docs/flight-session-design.md`.

## Sources and plugin findings

### Microsoft Flight Simulator 2024 SDK

Official MSFS 2024 SimConnect documentation confirms:

- `AircraftLoaded` and `FlightLoaded` system events,
- `Pause_EX1` with distinct pause flags,
- `PositionChanged` for dialog-driven position changes,
- `SimStart`/`SimStop`, with documentation warning that extra pairs can occur,
- `Crashed` and `CrashReset`,
- `SIMCONNECT_RECV_EVENT_FRAME.fSimSpeed` for simulation rate,
- one-second and visual-frame data delivery mechanisms.

Design consequence: simulator events are useful evidence but **must not themselves be the flight state machine**.

### FAA / real-world flight-time semantics

FAA material continues to support the existing rule that powered-aircraft flight time begins when the aircraft first moves under its own power for the purpose of flight and ends when it comes to rest after landing. OpenCareer keeps this separate from load/session time and from anti-exploit career-credit time.

### ForeFlight Mobile

Live ForeFlight MCP airport/performance calls could not be used because the connected account requires an active Starter/Essential/Premium ForeFlight license.

The available connector schema is still useful as a data-shape reference: its logbook model has separate total, PIC, night, cross-country, actual-instrument and simulated-instrument hours and event records distinguishing takeoff/landing, day/night, towered/untowered and full-stop.

OpenCareer should preserve equivalent useful dimensions where simulator/reference evidence exists, but must not claim ForeFlight verification of KRME, the F-22 or performance data.

### Wolfram

Wolfram was used to sanity-check adaptive telemetry sizing.

Illustrative six-hour sampling profile:

- 20 min parked at 0.25 Hz,
- 25 min taxi at 2 Hz,
- 260 min climb/cruise at 1 Hz,
- 35 min descent at 2 Hz,
- 10 min below 2,000 ft at 5 Hz,
- 5 min below 500 ft at 10 Hz,
- 5 min final/touchdown at 20 Hz.

Result: ~35,100 samples instead of 432,000 samples at a constant 20 Hz, about a 91.9% reduction. This supports a bounded high-rate event buffer plus decimated normal track rather than whole-flight high-frequency persistence.

### Supabase

No Supabase projects are currently connected.

Current Supabase documentation confirms Postgres, Realtime, Auth, Storage and server-side functions are available, but none improve the authoritative single-player flight loop enough to justify adding an online dependency.

Decision: SQLite remains authoritative and offline-first. Supabase is reserved for a future optional account/cloud-sync/community feature, not flight detection, mission completion or economic settlement.

### Vercel

No Vercel teams/projects are currently connected.

Vercel serverless/background capabilities are useful for optional web services, AI/web companion surfaces or shareable reports later. They are inappropriate as a dependency for in-flight state, telemetry processing, save recovery or authoritative settlement.

Decision: no Vercel dependency in Chapters 3–4.

### OpenAI Platform

The OpenAI Platform connector is connected to the user's Personal organization/default project. No API key was created because the current flight-system work does not require one.

Decision: future AI may generate dispatcher/copilot/passenger/debrief narrative, but normalized telemetry, logbook dimensions, state transitions, scoring and settlement remain deterministic local code.

### Kling AI

Kling is useful for future concept art/video/mockups. It adds no authoritative flight-system evidence and paid generation is unnecessary for this engineering review.

Decision: keep it out of flight logic.

### Figma

Created a FigJam implementation reference for the state machine:

- Observing -> Armed -> TaxiOut -> TakeoffCandidate -> Airborne -> Approach -> LandingEpisode -> TaxiIn -> Complete,
- rejected takeoff path,
- bounce loop,
- touch-and-go/go-around return to Airborne,
- connection-loss Suspended state with continuity-based resume,
- Interrupted terminal path when continuity fails.

Diagram: https://www.figma.com/board/smkdlv7jrQAUhmKlpAh4H5

### Notion

No existing OpenCareer/flight pages were found in the connected Notion workspace. A research summary can be maintained there as a human-readable project note, while GitHub remains the authoritative technical handoff.

## Architectural boundary after review

```text
MSFS 2024
  -> SimConnect adapter
  -> normalized telemetry + simulator evidence
  -> FlightEvidenceProcessor
  -> deterministic FlightStateMachine
  -> FlightSession / FlightLeg / LandingEpisode
  -> local SQLite checkpoint + event windows
  -> mission validator
  -> atomic settlement
  -> UI / logbook / debrief

Optional external services
  -> narrative, sync, sharing, visuals
  X never required for detection, recovery, completion or settlement
```

## Remaining gate

None of this replaces the current technical exit gate: the existing probe still must be run against the user's installed MSFS 2024 + SDK at KRME/F-22 before detector thresholds are implemented or claimed live-valid.
