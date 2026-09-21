# OpenCareer simulation model

## Current invariants (audit correction, 2026-09-17)

1. Temporary restrictions do not directly overwrite structural capacity.
2. Demand and capacity are rates; backlog is a stock. Backlog pressure is normalized by `BacklogClearanceHorizonDays`, independent of API update duration.
3. The application uses `WorldSimulation.Advance`, an hourly UTC clock with stored pending time. Identical inputs/checkpoints produce identical results for daily, six-hour, five-minute and bulk requests on the tested runtime.
4. `MarketTickEngine` and `EconomicCycleEngine` remain low-level numerical integrators. They are not an application clock and arbitrary caller-selected subdivisions are not guaranteed identical. Event boundaries are chosen by the world clock, never by the UI refresh cadence.
5. Event starts and ends split only economically relevant intervals; irrelevant scopes, segments and mission-only events cannot add economic substeps. A 0.2-day outage lasts 4.8 hours.
6. `GetOperationalEffects` projects restrictions at requested career time without consuming saved randomness, including the incomplete hour. Use this for dispatch checks rather than treating the last economic checkpoint as current operational state.
7. Expansion uses actual served volume and realized contribution per structural capacity, with a smoothed investment signal. Zero service, nonpositive current margin or a finance freeze prevents positive expansion. This is an aggregate investment proxy; real company balance sheets, fixed costs, borrowing and aircraft delivery lead times remain future work.
8. Independent event/scope random streams store their current state and next occurrence; regional RNG state is also checkpointed. Events use exponential waiting times after the previous instance ends. No duplicate overlapping instance is generated for one event/scope.
9. Airport, region, nation, fleet and global scopes are separate. The current coordinator owns one market/location. A future shared world coordinator must own global/national schedules before many markets are run together.
10. Civilian, government and military access are distinct. Dispatch receives explicit eligibility and route evidence; defaults deny unverified operations.
11. Domain code receives career time explicitly and does not read the wall clock.

## Implementation and verification

Run:

```sh
dotnet run --project src/OpenCareer.SimLab/OpenCareer.SimLab.csproj --configuration Release
```

The executable regression suite returns a nonzero exit code when a scenario fails. It covers request partitioning, repeated polling, JSON checkpoint/resume, PRNG resume, catalog ordering, unrelated-event independence, scope and segment isolation, exact event boundaries, pending-hour operational restrictions, long closures, finance freezes, finite input validation, contract authorization/deadlines/lifecycle timestamps, all 14 market segments over two three-year event paths, and a ten-year integrated path. CI runs the same command.

Context7 checked official .NET documentation for positional-record JSON round trips. Wolfram independently confirmed that the new clearance horizon removes caller-duration scaling from backlog pressure and that realized contribution is zero with no service. Native C# regression checks establish the behavior of the implementation.

The older print-only single-seed demonstration and historical mathematical experiments are not the current acceptance gate. A successful build alone does not establish economic correctness. Numerical health checks do not establish gameplay balance or MSFS performance.

## Runtime and persistence boundaries

- Pure domain simulation runs outside MSFS. Catch-up must run away from simulator callbacks and the UI thread.
- Partial hours are intentionally retained rather than dropped or recomputed. The economic snapshot can lag requested time by less than one career hour; operational effects are available at the requested time.
- Event/cycle/configuration state is serializable. Atomic file/SQLite storage, migration handling and crash-safe financial settlement are not implemented.
- Runtime-specific floating-point behavior is not a cross-platform bitwise replay guarantee. Pin and version the simulation before production saves; test Windows/Linux replay before promising portability across implementations.
- Telemetry sample targets and immutable telemetry models exist; the SimConnect adapter, bounded buffer, landing capture and WinUI shell do not.

## Gameplay calibration

Ordinary charter/cargo/reposition opportunities complement rare disruptions. Charter demand targets passenger/charter segments; cargo demand targets cargo segments. The local charter and reposition arrival rates sum to 42 per eligible airport per career year, about one arrival every 8.7 days before accounting for active-event duration and suppression. These are provisional economic-time settings, not guaranteed visible jobs or real-time notifications.

Wages, actual mission generation, purchases, maintenance, credit and persistent inflation are not connected yet. Preserve the agreed no-XP progression through qualifications, relationships, reputation, capital and access.

See [development-backlog.md](development-backlog.md) for implementation gaps, acceptance criteria and the next milestone.
