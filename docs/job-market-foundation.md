# Job market foundation — compact handoff

Updated 2026-09-17. Read `AGENTS.md` and `docs/project-state.md` first. Read this file only when working on airport/job-market/career-economy generation.

## Why this branch exists

Parallel work for the local airport/job-market loop lives on `feature/job-market-foundation` so Astra can continue telemetry/SimConnect work without editing the same files. Merge/rebase only after checking the current `feature/m1-simulation-core` head.

## Player intent already fixed

- Career starts from a meaningful home airport; no free company teleporting.
- KRME Griffiss International is the first calibration hub and must stay mixed civilian/government/defense/UAS rather than being treated as purely military.
- Military-heavy airports should create substantially more military/government opportunities than ordinary public airports while retaining appropriate civilian work.
- Jobs should help build geographic connections, employer/customer relationships and later base/storage expansion.
- First used light-aircraft ownership should become reasonably attainable around 50–80 real flying hours through economics, not an hour unlock.
- Typical sessions are 1–3 hours and may extend to about six.

## Implemented in this branch

New domain-only, deterministic job-board foundation:

- `JobMarketPolicy`: tuning surface for board size, refresh cycle, range bias, locked previews, route/relationship boosts, military/government weighting and UAS specialty weighting.
- `JobMarketAccess`: controls which service tracks are currently actionable without weakening authoritative `JobContract` dispatch validation.
- `JobMarketDestination`: candidate destination with distance, route strength, relationship strength and market attractiveness.
- `JobMarketGenerator`: stable seeded generation by airport + market cycle; produces offer drafts only, not settled contracts or money.
- UAS-heavy airports boost survey/photography/surveillance-style opportunities.
- Established routes and relationships increase destination selection probability.
- Local-capable job types may remain at the origin instead of creating fake cross-country travel.
- Locked jobs can be shown at reduced weight or hidden by policy; a preview never grants access.

Tests cover deterministic replay, cycle changes, locked visibility, KRME-vs-low-defense military mix, relationship/route weighting, UAS specialization and invalid inputs.

## Provisional defaults awaiting player answers

These are deliberately policy knobs, not final design promises:

- 8–12 offers per board.
- 8-hour market refresh.
- 250 NM distance decay with a small long-range floor; hard generation ceiling 1,200 NM.
- Established-route boost +60%; relationship boost +75% at full strength.
- Government track multiplier 1.35; military multiplier 1.80.
- Locked previews enabled but downweighted to 15% of their normal selection weight.
- Civilian market split: 60% employee / 25% independent / 15% company before access filtering.

With current KRME demand values, Wolfram's simple normalized track check gives about 27.1% civilian, 35.1% government and 37.7% military before locked-access suppression. In an illustrative low-defense mixed public airport (82% civilian, 15% government, 5% military demand), the same policy gives ~8.1% military, so KRME's military share is about 4.7x higher. This is a tuning diagnostic, **not a claim about real Griffiss sortie proportions**.

The current acquisition example needs $64,000 cash/reserve. Wolfram confirms that reaching it in 50–80 flight hours requires average net savings of roughly $1,280/hour at 50h, $985/hour at 65h, or $800/hour at 80h. Actual job pay is intentionally not coded here yet; settlement needs the real career/economy pipeline and playtesting.

## Ten decisions requested from the player

1. Board density: 15–25 jobs or a tighter 6–12?
2. Early range: mainly 100–250 NM or occasional 500–1,000+ NM immediately?
3. Employer relationships: strong exclusive chains or modest bonuses?
4. Airport specialties: hard specialties or soft tendencies?
5. Military-heavy weighting: roughly 3x, 5x, 10x+ versus normal public airports?
6. Show aspirational locked jobs or only actionable jobs?
7. Establish a route after one flight or after repeated service (e.g. 3–5)?
8. Pay priority: time/distance/payload/risk versus scarcity/urgency dominating special cases?
9. Allow paid passenger/deadhead travel to another market, or mostly require flying jobs?
10. Continuous job creation or market-cycle refresh (e.g. 6–12 hours)?

When answers arrive, change policy values/relationship rules rather than rewriting the generator.

## Data-source boundary

Official MSFS 2024 SimConnect facility APIs can provide airport identity/position plus physical data such as runway counts/details, starts, approaches, taxi parking, helipads and jetways. That is useful for feasibility/capacity. The documented airport facility members do **not** provide career-economic labels such as "military-heavy", "cargo hub", "tourism" or "UAS research". Keep those classifications in OpenCareer's airport/economic data layer and source/curate them separately; never infer them from runway size alone.

Official SDK reference: `SimConnect_AddToFacilityDefinition`, `SimConnect_RequestAllFacilities`, and `SIMCONNECT_FACILITY_LIST_TYPE_AIRPORT` in the MSFS 2024 SDK.

## Not implemented yet

- No dollars/pay quotes or ledger settlement.
- No generated `JobContract` requirements; aircraft/qualification/authorization rules remain authoritative downstream.
- No persistence of board snapshots or accepted offers.
- No deadhead travel, employer chains or route service-count state yet.
- No broad airport classification dataset yet.
- No UI binding yet.

Those should follow the player's ten answers and the playable telemetry/flight foundation.
