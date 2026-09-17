# OpenCareer

OpenCareer is a standalone persistent career and aviation-world simulator for Microsoft Flight Simulator 2024.

MSFS 2024 remains the flight simulator. OpenCareer owns the career, economy, employers, contracts, company management, aircraft history, maintenance, world events, and progression.

## Core principles

- Start from zero: work for other operators before owning an aircraft.
- No XP grind. Progress through qualifications, reputation, relationships, capital, and access.
- A living economy creates work from demand rather than spawning arbitrary mission lists.
- Every installed aircraft should have meaningful work based on capabilities, including military aircraft.
- Missions begin before engine start and end after parking, shutdown, and unloading/turnaround.
- Runway and aircraft-performance feasibility is checked before a job is offered.
- The companion must have negligible impact on MSFS performance.
- Single-player first. Local persistence. No mandatory server, login, or subscription.

## Planned architecture

- `OpenCareer.App` - lightweight Windows UI shell.
- `OpenCareer.Domain` - pure career/economy/operations domain model.
- `OpenCareer.Application` - use cases and orchestration.
- `OpenCareer.Infrastructure` - SQLite, local persistence, clocks, files, external adapters.
- `OpenCareer.SimConnect` - MSFS telemetry/event bridge.
- `OpenCareer.Planning` - dispatch, runway feasibility, route and fuel planning.
- `OpenCareer.Economy` - market simulation, companies, events, finance and contracts.
- `OpenCareer.Tests` - deterministic unit/integration tests.

## Simulation layers

1. Personal pilot career
2. Employer/contractor career
3. Private company ownership
4. Public-company/capital-markets layer
5. Government and military career/contracting
6. Persistent world economy and event engine

## Flight lifecycle

`Accepted -> Preparation -> Servicing -> Loading -> ReadyForStart -> EngineStart -> Ramp -> TaxiOut -> DepartureReady -> Airborne -> Landed -> TaxiIn -> Parked -> Unloading -> Shutdown -> Complete`

## Development status

Bootstrap in progress. The first milestone is a deterministic headless simulation core plus a telemetry abstraction so the economy and mission engine can be tested without launching MSFS.


## Audit follow-up and development checklist

The simulation core now includes an authoritative career clock, scoped event scheduling,
checkpoint replay, contract dispatch guards and executable regression checks. It is not yet
a playable application: job generation, a persistent money ledger, aircraft ownership,
inflation, WinUI 3 and SimConnect integration remain unfinished.

- [Current simulation rules and verification](docs/simulation-model.md)
- [Remaining work, next milestone and gameplay decisions](docs/development-backlog.md)

Run the same regression suite as CI:

```sh
dotnet run --project src/OpenCareer.SimLab/OpenCareer.SimLab.csproj --configuration Release
```
