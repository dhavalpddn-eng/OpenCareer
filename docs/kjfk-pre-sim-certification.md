# Rapid KJFK C172 pre-simulator certification

The automated release gate is `KjfkPlayableLoopCertificationTests`.
It exercises production application/domain services against an isolated SQLite database.
It does not launch MSFS or certify native simulator behavior or interactive WinUI rendering.

## Boundary and authority

The existing SimConnect test transport supplies the stock TITLE `C172SP Classic Passengers`
without an aircraft catalog reply or aircraft.cfg. Production discovery and persistent
registry merge it with the canonical C172 reference source. Only normalized simulator
samples, a clock, and UI preferences are fixtures. Contract, board, profile, installed
registry, Fleet, FlightSession, ledger and logbook persistence use production SQLite stores.
A delegating checkpoint observer asserts durable cleanup ordering and injects one I/O failure;
it never substitutes authoritative state.

The test uses DevelopmentFlightService, CareerJobAircraftSelectionSource,
CareerJobStartInputSource, CareerJobStartActionService, CareerJobPlayableLoopCoordinator,
acceptance/reservation/dispatch bridges, FlightSessionRuntime/PersistenceService,
mission/cost evidence sources, CareerJobCompletionActionService, terminal settlement,
logbook, profile/finalization, and CareerFlightAbandonCoordinator. The production Jobs and
Shell view models call these actions. Copied production XAML/code-behind resources verify
button labels, enablement bindings, handler links, confirmation and registration contracts.

## Executable acceptance cases

- One canonical installed C172 option, complete reference merge, KJFK local offer, Ready/CanStart.
- Actual Jobs start action persists InProgress contract, exact contract-owned reservation and session.
- Engine start, taxi, takeoff, airborne; reconstructed services recover the same session and reservation.
- Fresh continuity evidence resumes the recovered session; no completion before mission/flight evidence.
- Provisional contact, short low airborne bounce, stable recontact and a second bounce: one landing episode,
  two bounces, unchanged SessionId/ContractId/aircraft reservation; duplicate telemetry has no effect.
- Low-speed recontact followed by rollout, TaxiIn, Parked, Shutdown and enabled completion action.
- Exactly one zero-value settlement and authoritative automatic career logbook entry; no compensation,
  reputation or experience/qualification gain; KJFK location retained.
- Checkpoint deletion follows durable completion, ledger, logbook, profile application and Fleet release.
- Five repeated completion requests plus a reconstructed-service replay do not add mutations.
- A new started flight interrupted by a distant position jump can be discarded without completing it.
- Interrupted checkpoint remains Interrupted through cancellation and release; failed final clear retries safely.
- Another offer immediately reaches Ready and starts; active cancellation still persists Cancelled and cleans up.

## Defects exposed by the complete chain

1. Normalized telemetry never populated BounceRecontact. The reducer's existing bounce support was
   therefore unreachable from live normalized telemetry. A bounded observed recontact now produces it,
   including when provisional contact bounces before the first stable landing confirmation.
2. A rollout indication consumed on the same sample as a higher-priority landing/bounce event could
   retire the processor's landing context before the reducer reached TaxiIn. Rollout is retained until
   a subsequent eligible ground observation.
3. Fractional-second telemetry reproduced the original profile/logbook ordering exception. SQLite
   stored the profile marker at millisecond precision while the logbook retained exact ticks. Schema 13
   adds an exact UTC-tick save marker committed atomically with the existing revision/payload. Legacy
   timestamps remain readable without inventing lost precision. The ordering invariant is unchanged.
   The entire lifecycle fixture now deliberately uses sub-millisecond timestamps.
4. Windows build jobs previously checked out the PR merge ref. Both build workflows now select the
   PR head explicitly; Windows asserts the checked-out SHA and runs the certification gate separately.

Bounce evidence uses named defaults of at most five seconds airborne and fifty feet AGL. These are
conservative event-classification bounds, not impact/damage calibration. Two ground samples still
confirm touchdown. Disconnect, pause, slew, excessive height/time or observation gaps discard transient
bounce evidence. The continuity policy and its teleport limits are unchanged.

## Verification boundary

Local focused tests, the full xUnit suite and SimLab must pass before publication.
PR #105 validates the exact pushed source SHA with WinUI x64 Release, the SimConnect probe,
the focused certification and the full xUnit suite. It remains unmerged.
Only native MSFS delivery, aircraft-specific signal behavior and interactive UI operation require
subsequent Windows/MSFS observation. No production fake telemetry or completion shortcut is added.
