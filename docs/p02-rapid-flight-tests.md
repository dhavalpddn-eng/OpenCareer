# P02 rapid live consequence tests

Baseline: `358ad379fc010af8f7833a5c5b33c3612a6c0881`.

Jobs → TEST / DEVELOPMENT → Generate KJFK test flight explicitly appends one
KJFK-to-KJFK local Reposition offer. Normal market generation is unchanged.
Pending test offers are reused; accepted/in-progress contracts or any saved flight
checkpoint block generation. Completing the normal terminal workflow permits the
next offer immediately. An explicit checkbox can position the pilot at KJFK using
the existing career location service; it does not move aircraft or the home base.
No active flight can be repositioned through this command.

The offer's existing persisted MarketId identifies development terms. Contract
creation uses zero pay rates and zero reputation reward/penalty. The normal
experience application records its exact-once debrief marker with a zero increment
for an authoritative development contract. Raw flight time remains in the real
logbook. No schema change or fabricated flight evidence is involved.

Normal provider resolution, eligibility, TITLE validation, reservation, session
start, telemetry, takeoff/landing detection, mission verification, consequences,
settlement, logbook, release, and checkpoint cleanup remain in use. The mission
validator still requires takeoff, landing, parking and shutdown at the destination.
There is no synthetic minimum airborne-time objective and no automatic completion.

Each new offer receives its own deterministic provider instance through the
existing resolver. Record that instance per flight; do not treat the previous
offer's airframe as the next offer's baseline. Provider consequence history is
retained by the existing pipeline; cumulative owned-aircraft condition snapshots
are not manufactured for provider aircraft. If diagnostics lack a before/after
condition, record it as unavailable, not zero.

## Windows procedure

Use `tools/Start-P02RapidTests.ps1` with the exact commit and downloaded bundle.
It checks a clean checkout, fetches the bundle, checks out the exact commit,
runs focused tests, builds and launches. It never resets or edits the career DB.

Load C172SP Classic Passengers parked at KJFK in MSFS. Confirm Settings shows
Connected, Live telemetry, and canonical `msfs-title:Cessna 172 Skyhawk`.
Generate the test offer; select its provider Cessna and use Accept & Start Flight.
Verify Current Flight is active before takeoff. Fly a short local circuit, return
to KJFK, park and shut down. Complete Career Flight through the normal button.
Verify terminal success, then capture Settings → Latest Flight Consequence.

Use the same aircraft type, fuel/payload, weather, runway and simulator rate for
Normal, Firm, Hard-but-survivable, and Bounce. Do not slew, teleport, switch planes
or reload during an active test. Target 5–10 minutes per circuit, not a timer bypass.

Capture SessionId, ContractId, airframe instance/ownership and AircraftId; block and
airborne duration; landings and bounces; touchdown and correlated descent sample
timestamps; sample-to-contact gap; crash flag; severity; ordinary and landing wear
contributions; available before/after wear and damage. Correlation uses a descent
proxy, not measured impact force. Preserve every result before starting the next.

| Test | Descent evidence | Landings | Severity | Wear delta | Damage delta | Crash |
|---|---|---|---|---|---|---|
| Normal | Pending | | | | | |
| Firm | Pending | | | | | |
| Hard | Pending | | | | | |
| Bounce | Pending | | | | | |

No calibration thresholds or correlation window were changed. No live results are
available for this feature yet. Focused tests were added but cannot be executed in
the development container because the .NET SDK is unavailable. Windows compilation
and the controlled simulator flights remain required.
