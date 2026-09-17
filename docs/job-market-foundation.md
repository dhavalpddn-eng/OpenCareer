# Job market foundation — compact handoff

Updated 2026-09-17. Read `AGENTS.md` and `docs/project-state.md` first. Read this file only when working on airport/job-market/career-economy generation.

## Parallel branch

Work lives on `feature/job-market-foundation` so Astra can continue telemetry/SimConnect work without editing the same files. Rebase/merge only after checking the current `feature/m1-simulation-core` head and rerunning both CI paths.

Latest tested branch implementation: `50f4c783db6c44a2a4fe23860c1d2d5302518c86`. Linux CI run 35284265143 passed **105/105 xUnit tests + 29/29 SimLab scenarios**. Windows CI run 35284265147 passed the WinUI x64 Release build and the same test project.

## Accepted player decisions

- **Starter board:** level 1 exposes only **1–2 jobs**.
- **Airport cap:** career level expands visibility up to an airport-specific mature capacity. Current scale presets are Local 10, Small 16, Regional 20, Large 35, MajorHub 45, MegaHub 60. These are game-design capacities, not claims about real daily flights.
- **Meaningful levels:** level is a meta-progression summary derived from verified flying/contracts/routes/trust/qualifications. It may reveal more work/convenience/prestige but never bypasses licenses, ratings, aircraft capability, military authorization, employer trust, affordability or dispatch feasibility.
- **Early duration:** early-career boards favor jobs estimated around **2–4 flight hours**, with mission-type exceptions such as ferry/reposition work.
- **Employer trust:** repeated successful, safe, on-time work builds persistent trust. Real company names may be used only when their real airport presence/service is sourced. Large employers can later provide duty deadhead when the relationship permits it.
- **Airport specialization:** airport/company/job mix should reflect real airport roles: airline hubs, cargo hubs, military/government activity, UAS/research, skydiving, GA, medical, tourism, etc. Specialties are strong tendencies rather than a generic board.
- **Military/security events:** regional security phases can shift work toward military/government missions, including transport, surveillance/recon, patrol, medevac, evacuation and logistics. Severe active conflict can suppress ordinary civilian work. Ceasefire/recovery immediately refreshes toward humanitarian, cargo, medical, survey and infrastructure-recovery work.
- **Dream jobs:** show at most **1–2 aspirational locked jobs**. They never grant access.
- **Route familiarity:** repeated service progresses Untried -> Discovered -> Familiar -> Established -> Preferred -> Core. Current successful-flight thresholds are 1 / 2 / 5 / 12 / 25.
- **Pay direction:** base pay should primarily reflect time + distance + payload, then market supply/demand, urgency, scarcity, difficulty, employer relationship and operating costs. Final settlement is not implemented yet.
- **Cargo/passenger economy:** cargo goods and passenger route trends affect demand. Popular destinations, seasonality, capacity shortages and commodity pressure can move job frequency and later pay.
- **Deadhead/travel:** player may pay to travel without their aircraft; the aircraft remains where it physically is. Some large-company duty assignments may provide employer-paid deadhead.
- **Refresh behavior:** jobs expire/replenish individually at deterministic random times. Internal generation buckets are an implementation aid, not a whole-board wipe. Major world/security transitions can force an immediate relevant-board rebuild.
- **World/social feed:** current direction is AI narration from OpenCareer's own simulated world state, **without live news/social collection**. The OpenAI narrator is optional; a deterministic offline narrator is mandatory fallback. Feed prose is never authoritative for money, mission completion, ownership, access, airspace state or world-event creation.

## Implemented on this branch

### Job visibility and meaningful level

- `CareerProgressEvidence`, `CareerLevelSnapshot`, `CareerLevelPolicy`.
- Default max level: 50.
- Current merit sources: verified flight hours, completed contracts, route milestones, employer-trust milestones and qualifications.
- Board growth uses a smooth level curve and an airport mature-capacity ceiling.
- Wolfram design check for the chosen curve (`cap=60`) produced approximately: level 1 = 2, level 5 = 9, level 10 = 16, level 20 = 28, level 30 = 40, level 40 = 50, level 50 = 60. Actual level-1 count is deterministically 1 or 2.

### Airport/job board

- `AirportMarketCapacity` separates career-market size from runway feasibility.
- `JobMarketPolicy`, `JobMarketAccess`, `JobMarketDestination`, `JobMarketGenerator`.
- deterministic generation by career seed + airport + generation bucket;
- randomized per-offer expiry rather than a single board expiry;
- early 2–4-hour preference;
- 1–2 locked dream previews as the board grows;
- route/relationship weighting;
- stronger UAS/research weighting for survey/photography/surveillance;
- local-capable jobs can remain at the origin.
- `JobBoardState` keeps still-valid offers, retires expired/consumed IDs, fills only open slots and supports explicit event-driven board replacement. It is domain state only; SQLite persistence is still pending.

### Relationships and network

- `EmployerTrustState` with New / Known / Trusted / Preferred / Partner tiers.
- failures and accepted-job cancellations reduce trust.
- large-company deadhead eligibility is relationship-dependent.
- `RouteExperience` requires repeated successful service before a route becomes established/preferred/core.

### Regional security / recovery

- `RegionalSecurityState`: Normal, ElevatedTension, ActiveConflict, Ceasefire, Recovery.
- state can be Simulated or `CuratedLiveSignal`; a live signal must include source provenance.
- severe conflict can suppress civilian-track selection and heavily boost government/military tracks.
- ceasefire/recovery boosts cargo, humanitarian, medical, survey and recovery work.
- `JobScenarioKind` includes Standard, OrganTransport, TroopMovement, HumanitarianAirlift, ConflictReconnaissance, RecoverySupply and InfrastructureAssessment.
- scenario generation is deterministic. External AI does not decide eligibility or rewards.

### Cargo and passenger demand

- `RouteDemandProfile`, `DemandPressure`, `PassengerRouteDemand`, `CargoCommodityDemand`.
- cargo categories currently include general freight, express parcels, mail, perishables, medical supplies, AOG parts, industrial parts, electronics/high-value goods, humanitarian supplies and government/military logistics.
- passenger purposes include general, business, leisure, VFR, major events, seasonal, evacuation and recovery.
- scarcity/trend/urgency produce a bounded route-attractiveness multiplier from **0.25x to 4x**.
- `JobMarketGenerator` now consumes route demand: passenger/charter work follows passenger pressure; cargo/express/AOG/medical/disaster/military-transport work uses matching commodity pressure.
- equal-distance route tests verify cargo scarcity materially changes job selection.

### World/social feed runtime

- `WorldSignal` stores normalized world state; only validated/unexpired signals may affect deterministic economics.
- `IWorldFeedNarrator` isolates narration from game authority.
- `OpenAiWorldFeedNarrator` uses the Responses API with strict JSON-schema output and **no tools/web-search field**. It receives only OpenCareer facts and recent stored posts.
- every AI post must reference supplied fact keys; unknown facts invalidate the response.
- `DeterministicWorldFeedNarrator` is the zero-network fallback.
- `WorldFeedNarrationService` automatically falls back when the AI path times out, errors or returns invalid data.
- `SqliteWorldFeedPostStore` persists the feed timeline with parameterized SQL. Active reads hide expired posts while historical reads retain them.
- `WorldFeedCoordinator` throttles ordinary generation, carries historical context forward for continuity, preserves expired posts and supports forced refresh after major simulated world transitions.
- AI-generated posts are explicitly disclosed as generated from OpenCareer simulated state with no live web/news collection.
- See `docs/live-world-feed.md` for the runtime boundary and future WinUI composition steps.

## Calibration notes

The airport-capacity curve was checked with Wolfram before implementation. UAS specialization was increased after CI showed the original multiplier produced too little observable separation; this is gameplay tuning rather than a real-world statistic.

The route-demand pressure function was also checked with Wolfram: a balanced route evaluates to 1.0x, an illustrative high-demand/low-capacity/trending route to about 2.19x, and an illustrative weak route to about 0.65x, with extremes capped at 4x.

The existing first-aircraft example needs $64,000 cash/reserve. Prior Wolfram calibration implies average net savings around $1,280/flight-hour for 50h, $985/hour for 65h, or $800/hour for 80h. Actual job pay is intentionally not finalized until one-time ledger settlement and playtesting are connected.

## Real-data boundary

Official MSFS 2024 SimConnect facility APIs can provide physical airport data such as position, runways, starts, approaches, taxi parking, helipads and jetways. They do **not** provide economic labels such as cargo hub, airline hub, military-heavy, tourism or UAS-research. Keep those in OpenCareer's sourced airport/economic data layer.

For U.S. airports, preferred external calibration sources are:

- FAA passenger enplanement and all-cargo datasets for airport scale/cargo intensity;
- BTS T-100 segment/market data for carrier presence, routes, passengers, freight/mail, capacity, departures and aircraft hours;
- airport/operator/DoD/government official sources for specialty roles that traffic datasets do not describe.

See `docs/airport-employer-data.md`. Do not infer a military base, airline base, cargo operator or employer relationship solely from runway dimensions.

## Real-mission inspiration rule

Use documented historical mission patterns as inspiration, not literal reenactments of tragedies. Examples already verified from official U.S. military sources include an FB-111A donor-heart transport in 1986, C-17 troop/equipment airlift supporting an African Union mission in 2014, and C-17 ECMO medical evacuation. Preserve the *operational pattern* (urgent organ lift, troop/logistics movement, specialized medevac), while the generated OpenCareer contract remains fictional unless a live-data feature explicitly labels factual information with provenance. See `docs/real-mission-inspiration.md`.

## Still not implemented

- SQLite persistence/application service for `JobBoardState` itself; world-feed posts are now persisted separately;
- persistence of normalized `WorldSignal` records if/when needed beyond the existing simulation save;
- normalized real employer/airport data pack/importer and licensing/attribution review;
- long-running passenger/cargo trend evolution layered over `RouteDemandProfile`;
- pay quote + authoritative one-time ledger settlement;
- paid personal deadhead quote/settlement and employer-issued duty deadhead orders;
- WinUI DI/configuration for the AI narrator, secure API-key loading and World/Network feed page;
- conversion of offer drafts into fully validated `JobContract` requirements;
- UI binding for the job market.

Next job-market work should keep the no-collector AI feed boundary, wire persistent job-board/deadhead/contract-generation services, then rebase onto Astra's current feature head and rerun both CI paths before any merge.
