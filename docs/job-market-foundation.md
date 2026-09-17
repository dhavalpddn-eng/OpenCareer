# Job market foundation — compact handoff

Updated 2026-09-17. Read `AGENTS.md` and `docs/project-state.md` first. Read this file only when working on airport/job-market/career-economy generation.

## Parallel branch

Work lives on `feature/job-market-foundation` so Astra can continue telemetry/SimConnect work without editing the same files. Rebase/merge only after checking the current `feature/m1-simulation-core` head and rerunning both CI paths.

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
- **Cargo/passenger economy:** cargo goods and passenger route trends must affect demand. Popular destinations, seasonal travel and local shortages can move job frequency/pay.
- **Deadhead/travel:** player may pay to travel without their aircraft; the aircraft remains where it physically is. Some large-company duty assignments may provide employer-paid deadhead.
- **Refresh behavior:** jobs expire/replenish individually at deterministic random times. Internal generation buckets are an implementation aid, not a whole-board wipe. Major world/security transitions can force an immediate relevant-board rebuild.
- **World/social feed:** optional online GPT/news-assisted feed is allowed for flavor and validated external signals, but it is never authoritative for money, mission completion, ownership, access or airspace state. Persist normalized signals/posts in SQLite; deterministic game logic decides effects.

## Implemented on this branch

### Job visibility and level

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

## Calibration notes

The airport-capacity curve was checked with Wolfram before implementation. Current UAS specialization was increased after CI showed the original multiplier produced too little observable separation; this is gameplay tuning rather than a real-world statistic.

The existing first-aircraft example needs $64,000 cash/reserve. The prior Wolfram calibration implies average net savings around $1,280/flight-hour for 50h, $985/hour for 65h, or $800/hour for 80h. Actual job pay is intentionally not finalized until the job/cargo/passenger economy and one-time ledger settlement are connected.

## Real-data boundary

Official MSFS 2024 SimConnect facility APIs can provide physical airport data such as position, runways, starts, approaches, taxi parking, helipads and jetways. They do **not** provide economic labels such as cargo hub, airline hub, military-heavy, tourism or UAS-research. Keep those in OpenCareer's sourced airport/economic data layer.

For U.S. airports, preferred external calibration sources are:

- FAA passenger enplanement and all-cargo datasets for airport scale/cargo intensity;
- BTS T-100 segment/market data for carrier presence, routes, passengers, freight/mail, capacity, departures and aircraft hours;
- airport/operator/DoD/government official sources for specialty roles that traffic datasets do not describe.

Do not infer a military base, airline base, cargo operator or employer relationship solely from runway dimensions.

## Real-mission inspiration rule

Use documented historical mission patterns as inspiration, not literal reenactments of tragedies. Examples already verified from official U.S. military sources include an FB-111A donor-heart transport in 1986, C-17 troop/equipment airlift supporting an African Union mission in 2014, and C-17 ECMO medical evacuation. Preserve the *operational pattern* (urgent organ lift, troop/logistics movement, specialized medevac), while the generated OpenCareer contract remains fictional unless a live-data feature explicitly labels factual information with provenance.

## Still not implemented

- persistent SQLite board service that keeps surviving offers and replenishes only expired/consumed slots;
- real employer/airport classification dataset and licensing/attribution review;
- commodity-level cargo economy and passenger-trend engine;
- pay quote + authoritative one-time ledger settlement;
- paid personal deadhead quote/settlement and employer-issued duty deadhead orders;
- online signal collector / GPT world-feed integration and SQLite feed persistence;
- conversion of offer drafts into fully validated `JobContract` requirements;
- UI binding.

Next job-market work should build the persistent board lifecycle plus cargo/passenger demand signals, then hook those outputs into authoritative contract generation after Astra's playable flight foundation is ready.
