# phpVMS integration notes

Updated: 2026-09-19.

Source: phpVMS v7 API documentation at `https://docs.phpvms.net/api/apis`.

## Role in OpenCareer

phpVMS is useful as an **optional virtual-airline integration**, not as OpenCareer's authoritative simulator, career, economy, or flight-state source.

A phpVMS API belongs to a specific phpVMS installation. It is not one global public flight database. Connecting therefore requires:

- the virtual airline's phpVMS base URL;
- a user API key issued by that phpVMS installation;
- the key sent in the `X-API-Key` request header.

OpenCareer core gameplay must continue to work without phpVMS.

## Useful endpoints

Current phpVMS v7 documentation exposes:

- `GET /api/user` — pilot/account context, current airport, rank, balance and bids;
- `GET /api/user/fleet` — subfleets and aircraft available through the user's rank;
- `GET /api/user/bids` — the user's currently selected/assigned flights;
- `PUT /api/user/bids` / `DELETE /api/user/bids` — optional bid synchronization;
- `GET /api/airlines` and `GET /api/airlines/{ID}`;
- `GET /api/airports`, `GET /api/airports/hubs`, `GET /api/airports/{ICAO}`;
- `GET /api/fleet` and `GET /api/fleet/aircraft/{id}`;
- `GET /api/flights`, `GET /api/flights/{FLIGHT ID}`, and `GET /api/flights/search`;
- `GET /api/pireps/{PIREP ID}`;
- `GET /api/pireps/{PIREP ID}/route`;
- `GET /api/pireps/{PIREP ID}/acars/positions`.

Flight records can include airline, flight number, route code/leg, departure, arrival, alternate, route text, planned times, distance, flight type and eligible subfleets. PIREPs can expose actual flight time, route, distance, fuel, landing rate and status, while route/ACARS endpoints expose historical route/position evidence.

## Best OpenCareer uses

### Full-time airline employee path

A connected phpVMS airline can provide schedule and fleet context for players who want to stay airline employees indefinitely:

- airline identity;
- available routes/schedules;
- user-rank fleet access;
- aircraft/subfleet assignment context;
- current bids;
- historical airline PIREPs.

OpenCareer should still own wages, trust, qualifications, progression, career settlement and local save state unless a future explicit synchronization mode says otherwise.

### Virtual-airline jobs

phpVMS schedules can be imported as **external opportunity templates**. OpenCareer then validates them against its own:

- installed/available aircraft;
- license/rating rules;
- aircraft capability;
- runway/performance feasibility;
- local career state;
- current mission rules.

External schedule availability must never bypass OpenCareer eligibility.

### Route and debrief enrichment

phpVMS route strings, PIREP route points and ACARS positions can enrich:

- planned-route display;
- airline route maps;
- historical flight comparison;
- debrief context;
- VA statistics.

They are external reference/history data. Live OpenCareer FlightSession state remains derived from normalized MSFS telemetry.

## Data authority

Use these authority rules:

1. **MSFS normalized telemetry / OpenCareer FlightSession** — what the player actually flew now.
2. **OpenCareer mission/dispatch state** — what the player was assigned and what conditions govern completion.
3. **OpenCareer economy/career database** — money, progression, ownership, reputation, qualifications.
4. **phpVMS** — optional VA schedules, fleet context, bids and VA historical records.
5. **OurAirports / FAA NASR / openAIP / other approved providers** — reference enrichment according to their specific domain.

Conflicts must preserve source provenance rather than silently overwriting authoritative OpenCareer state.

## Provider-neutral implementation shape

Do not make Domain or FlightSession depend on phpVMS JSON models.

The future external-airline adapter should normalize phpVMS into provider-neutral records such as:

- ExternalAirline;
- ExternalScheduledFlight;
- ExternalFleetType;
- ExternalAircraftAssignment;
- ExternalPilotAssignment;
- ExternalFlightHistory;
- ExternalRoutePoint.

The adapter should live in Infrastructure/ExternalServices (or the common provider layer established by the API workstream) and expose Application-layer contracts.

Secrets must never be committed. Store the phpVMS base URL and API key using the project's secure settings/secret-storage approach once that subsystem exists.

## Write operations

Bid writes are potentially useful but must be opt-in.

OpenCareer should initially treat phpVMS as read-only. Only later enable `PUT /api/user/bids` or `DELETE /api/user/bids` after:

- explicit user connection/permission;
- clear conflict handling;
- idempotent behavior;
- offline/retry semantics;
- tests against a controlled phpVMS instance.

OpenCareer must never create or alter a VA bid merely because the player viewed an OpenCareer job.

## FlightSession boundary

phpVMS PIREP/ACARS data must not be used to advance the live OpenCareer FlightSession reducer.

It may be compared after the fact, but engine start, taxi, takeoff, landing, parking, interruption and mission completion stay driven by OpenCareer evidence.

This keeps reconnect/recovery deterministic and prevents a network service from becoming a gameplay authority.

## Next integration point

When the external API branch establishes its provider abstractions, add a phpVMS adapter there rather than implementing another HTTP stack in the FlightSession branch.

The first useful read-only slice should be:

1. connection profile: base URL + API key;
2. `GET /api/user` health/authentication test;
3. `GET /api/user/fleet`;
4. `GET /api/flights/search`;
5. normalize schedules/fleet into provider-neutral models;
6. cache results with source/timestamp;
7. surface them to airline-employment/job-generation services.

PIREP/route/ACARS history can follow after the airline schedule/fleet path is stable.
