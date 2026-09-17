# Regional conflict simulation foundation

Updated 2026-09-17.

This system implements the player decisions for dynamic geopolitical/security simulation without making AI authoritative.

## Player direction encoded

- conflict risk is dynamic rather than a fixed random-war timer;
- curated real-world conditions may seed a new career, then the save diverges into fictional alternate history;
- curated baseline risk is slightly amplified for gameplay by a bounded policy value, currently 1.15x;
- a long/important border can create persistent surveillance and supply work without implying that the neighboring regions are likely to go to war;
- land-border exposure, maritime exposure, dispute friction, alliances and economic interdependence are modeled separately;
- escalation can move through Normal -> Surveillance -> Logistics Buildup -> Mobilization -> Active Conflict;
- rare high-pressure surprise escalation can jump directly toward mobilization;
- active conflicts later move through Ceasefire -> Recovery -> Resolved;
- conflict duration uses a mixture from days to years instead of one fixed timer;
- most conflict effects remain regional;
- a very rare systemic/world-war state can mobilize strongly allied regions without automatically turning every country into an active combat zone;
- ordinary passenger/cargo aviation remains available in non-frontline systemic-conflict regions;
- frontline civilian activity can be heavily suppressed by severe conflict;
- player operational work can nudge a side's campaign balance, but daily and lifetime influence are capped;
- helping both sides can cancel the player's strategic influence;
- humanitarian/medical/evacuation/recovery flying helps reduce disruption without requiring the player to choose a side;
- conflict stages drive different aviation demand: surveillance first, then cargo/logistics, then military transport/readiness/fighter-related operations;
- active conflict supports troop movement, reconnaissance and an AirfieldReinforcement scenario for military transport;
- recovery shifts toward cargo, humanitarian, medical, survey and infrastructure work.

## Real-world baseline boundary

No live news collector is part of the conflict engine.

The domain accepts a `CuratedRealWorld` baseline only when it carries:

- an as-of timestamp;
- source provenance;
- normalized regional risk values.

Active real-world conflicts can be seeded through `CuratedConflictSeed`, which also requires source provenance. Once the career simulation advances, subsequent escalation, ceasefire, recovery and alternate-history events are deterministic OpenCareer simulation rather than claims about current real-world events.

Do not infer war risk from border length alone. `BorderSecurityPressure` primarily creates surveillance/utility demand. Actual cross-border escalation depends heavily on `DisputeFriction`, tension and other state.

A production real-world baseline data pack is **not yet populated** on this branch. It should be separately versioned and updated from reputable/official sources so game code is not tied to stale geopolitical claims.

## Main domain types

- `ConflictRegionProfile`
- `ConflictRegionConnection`
- `CuratedConflictSeed`
- `RegionalConflictPosture`
- `ConflictCampaignState`
- `SystemicConflictState`
- `ConflictWorldState`
- `ConflictPlayerContribution`
- `ConflictSimulationPolicy`
- `ConflictAviationDemandProfile`
- `ConflictRiskModel`
- `ConflictWorldEngine`

## Probability and duration calibration

Escalation is staged. A peaceful border with high surveillance demand but very low dispute friction has extremely low calculated outbreak pressure.

The active-conflict duration sampler currently uses this gameplay mixture:

- 15%: 3-14 days;
- 45%: 14-120 days;
- 30%: 120-540 days;
- 10%: 540-1825 days.

Wolfram calibration gives an illustrative mixture mean of about 249 days. This is a gameplay distribution, not a claim about real wars.

At fixed escalation pressure, Wolfram first-passage checks of the staged transition model show that low and medium pressure almost never reach active conflict, while very high sustained pressure can progress on a multi-year scale before connection-specific outbreak checks. This is intentional: the world should produce surveillance/logistics crises much more often than wars.

The default systemic-conflict hazard is only 1% annualized while its prerequisites are satisfied. It additionally requires at least two active campaigns. This keeps the world-war scenario exceptional.

## Aviation/job integration

`JobMarketGenerationRequest` now optionally accepts `ConflictAviationDemandProfile`.

`JobMarketGenerator` multiplies existing airport/security weights by this finer conflict-stage demand. The existing high-level `RegionalSecurityState` still controls civilian suppression and government/military access; the conflict demand profile controls *which* aviation work grows at each escalation stage.

Examples:

- normal high-security border: surveillance rises, other aviation is mostly normal;
- surveillance: surveillance/survey work rises;
- logistics buildup: cargo and military transport rise;
- mobilization: transport, readiness, patrol/escort/intercept pressure rises;
- active conflict: military transport, recon, medical, evacuation and humanitarian work rise while local passenger continuity falls;
- ceasefire/recovery: humanitarian, medical, cargo and reconstruction/survey work dominate.

`AirfieldReinforcement` is now a generated military-transport scenario during active conflict. The future airport-control layer must determine whether a specific destination is actually eligible as contested/reopened; this foundation does not fake airport ownership state.

## Player influence

Operational contributions are side-specific and bounded by:

- max 0.03 campaign-balance shift per simulated day;
- max 0.15 accumulated player bias.

This means a player can matter over many flights but cannot single-handedly decide a campaign.

Neutral humanitarian/medical/evacuation/recovery contributions do not move the side balance. They can reduce disruption/severity within a small daily cap.

## Determinism and persistence

The engine advances on UTC-day boundaries and derives independent random streams from:

- career seed;
- region/connection/campaign key;
- simulated day.

No wall-clock RNG is used. Saving at day 180 and continuing to day 365 must produce the same result as advancing directly to day 365.

## Still needed

1. Populate/version the curated real-world regional baseline data pack.
2. Add country/region/airport mapping so airport job boards receive the correct regional conflict state automatically.
3. Add persistence of `ConflictWorldState` to the main career save database.
4. Add airport-control/airfield-status state for reinforcement/reopening missions.
5. Add explicit coalition campaign identities if we want more detailed multi-side systemic conflicts.
6. Feed conflict transitions into the persistent AI social/world feed.
7. Add UI map layers for tension, conflict, ceasefire and recovery.
8. Playtest/calibrate outbreak frequency across hundreds of regions and multi-decade careers.
