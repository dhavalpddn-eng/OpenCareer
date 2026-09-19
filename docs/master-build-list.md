# OpenCareer Master Build List

Status: **CANONICAL high-level list of unfinished major product systems**.

This file is the active "what is left to build" list.

Rules:

- Keep only unfinished major systems here.
- Remove an item only after it is implemented, integrated, tested and accepted.
- Partial foundations do not count as complete.
- Git history and `docs/development-master-checklist.md` preserve completed implementation history.
- New major scope goes here when accepted.
- Technical dependency order in `ASTRA.md` can override list order.

## Remaining

1. **Production Home / Dashboard** — full career home using real application data and the canonical OpenCareer visual language.
2. **Reusable WinUI design system** — production tokens/components for the retro-modern shell, vivid paper panels, controls, lists, status states, typography and accessibility.
3. **Full tutorial engine — IN PROGRESS** — core engine is implemented and CI-green: versioned first-run app tour, persistent resume/skip/completion, shell overlay/navigation, Settings replay, first-job walkthrough, and banner/carrier previews. Keep this item until local interactive/visual acceptance plus automatic first-job/mission-family trigger integration are verified.
4. **Live per-flight checklist** — phase-aware preflight through shutdown checklist with trustworthy live auto-verification.
5. **Controller + keyboard binding system** — per-step verified bindings, UNBOUND states and aircraft/device-sensitive mapping.
6. **FlightEvidenceProcessor** — normalized telemetry to stable taxi/takeoff/airborne/approach/landing/go-around/parking evidence.
7. **Persistent FlightSession / recovery** — SQLite checkpoints, multi-leg sessions, route/event persistence, interruption recovery and duplicate-effect prevention.
8. **Installed-aircraft registry** — installed aircraft identity, capability/confidence, location/experience and installed/employer/rented/assigned/owned access.
9. **Dispatch system** — payload, fuel/range, runway, weather/environment, qualification and mission feasibility with explicit blocking reasons.
10. **Playable Jobs system** — market/location/career-driven work with employee viability and accept-to-active-operation flow.
11. **Specialized mission framework** — banner tow, carrier operations, glider tow, skydiving, firefighting, SAR, survey, medevac, special government work and other unique mission families.
12. **Debrief + Logbook** — trustworthy flight/mission history, evidence, incidents, metrics and one-time settlement record.
13. **Career progression** — licenses, ratings, experience, recency, reputation, relationships, qualifications and milestones.
14. **Economy + Markets** — authoritative persistent economy, named cargo, market pressure/events, long absence and bankruptcy/recovery.
15. **Aircraft purchasing + financing** — persistent dealers/lenders, used/new inventory, loans, insurance and atomic ownership transfer.
16. **Hangars + Bases** — physical fleet geography, storage, local services, relationships, expansion and reposition logistics.
17. **Maintenance** — wear, service, repairs, parts/MRO, downtime, history and supported incident consequences.
18. **Company system** — optional business ownership, contracts, routes, utilization, staff, margins and expansion while preserving employee-only play.
19. **Military / Government career system** — separate qualifications, assigned aircraft, patrol/surveillance/logistics/intercept/escort and later fictionalized conflict operations.
20. **World Map** — airports, bases, fleet/company geography, routes, opportunities, events, markets and authorized government/military overlays.
21. **MSFS EFB companion** — thin in-sim projection for Current Flight, checklist, objectives, route and next action while Windows remains authoritative.
22. **AI dispatcher / copilot / passengers** — optional narrative/dialogue layer that never owns authoritative game state.
23. **Settings / diagnostics system** — full simulator health, inputs, tutorials, units, accessibility, backup/recovery, logs and optional-service controls.
24. **Installer / update / release pipeline** — packaging, migrations, backups, production logging, performance validation, signing and update strategy.
