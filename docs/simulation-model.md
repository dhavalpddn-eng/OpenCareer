# OpenCareer simulation model

This document records the first two design-review passes performed before the simulation core was committed.

## Non-negotiable invariants

1. Temporary airport, weather, airspace, or ground-service restrictions never directly overwrite permanent market capacity.
2. Demand and capacity are rates; backlog is a stock.
3. Multi-day catch-up is internally advanced in bounded substeps so a seven-day catch-up is equivalent to seven one-day advances for deterministic inputs.
4. Prices can react quickly; permanent capacity reacts only to a smoothed investment signal.
5. World events affect more than money: airspace, navigation, failure hazard, airport services, maintenance capacity, finance, and mission availability are independent dimensions.
6. Random streams are derived by subsystem/entity from one career seed. Adding a new unrelated feature must not consume random values from an existing subsystem and reshuffle an established save.
7. Civilian, government, and military aircraft access are separate concepts. A military-only F-22 can receive military missions without becoming a privately purchasable company asset.
8. Domain logic receives time explicitly. It does not call `DateTime.UtcNow` or `DateTimeOffset.UtcNow` internally.

## Audit pass 1

The original prototype had two defects:

- `ElapsedDays` changed capacity response but not demand volume, backlog decay, or price response, so tick granularity could change outcomes.
- A temporary `CapacityShock` was multiplied into capacity and then written back as the next permanent capacity. A one-day 50% availability shock cut structural capacity roughly in half and it recovered only slowly.

The revised model separates structural capacity from available capacity and advances catch-up with <=1-day internal steps.

Wolfram check:

- 210 days advanced daily vs. 30 x 7-day catch-up calls: difference norm `0` because both use the same bounded internal steps.
- Structural capacity before a one-day 50% availability shock: about `102.79`.
- Structural capacity immediately after that shock: about `102.87`, not ~`51`.
- The disruption instead creates backlog, which the market subsequently works down.

## Audit pass 2

The second pass added:

- slow capacity-investment memory (21-day default half-life),
- effective operating-cost shocks,
- mean-reverting regional demand and cost cycles,
- independent deterministic random streams,
- non-economic world-event dimensions.

Wolfram stochastic stress test:

- 40 independent paths,
- 10 simulated years per path,
- recurring temporary demand/capacity/cost shocks,
- rare larger shocks.

Observed summary:

- median final backlog: `0`,
- median worst temporary backlog: about `100.6`,
- 95th percentile worst backlog: about `404.1`,
- median final structural capacity: about `141.8`,
- 5th-95th percentile final structural capacity: about `128.9`-`161.4`,
- maximum capacity observed: about `169.4`,
- minimum price observed: about `1.14`,
- maximum price observed: about `2.56`,
- no non-finite or negative terminal state was observed in the run.

These values are calibration evidence, not final gameplay balance targets.

## Runtime strategy

OpenCareer should do as little work as possible while MSFS is actively rendering a flight.

- Economy/world simulation uses coarse deterministic ticks and can be deferred during active flight.
- On shutdown, elapsed career time can be caught up deterministically.
- Live telemetry uses adaptive sampling: ~0.25 Hz parked/cold, 2 Hz taxi, 1-2 Hz cruise/descent, 5 Hz below 2,000 ft AGL, 10 Hz below 500 ft, and 20 Hz in the final 100 ft/touchdown window.
- High-resolution landing samples belong in a bounded in-memory buffer and are persisted in batches after landing, not written to SQLite every frame.
- The future SimConnect adapter should be a single-reader/single-writer boundary and should never block the simulator callback path.

## Next mathematical layers

- company competition and market share,
- financing/credit cycles and public-company valuation,
- insurance and component reliability hazard curves,
- airport/ground-service queueing,
- runway performance and dispatch feasibility,
- military/government contract payout calibration,
- market-information quality and imperfect competitor knowledge.
