# Career foundation decisions

Status: authoritative design baseline for implementation and balancing.

## Early progression

The user clarified the 50–80 real-flight-hour target: a small aircraft bought outright or a larger aircraft financed, depending on career standing and affordability. There is no forced ownership hour gate. The default calibration now uses a fictional $60,000 used light aircraft, $4,000 reserve and $1,000 net savings per real flying hour: 64 hours to a cash purchase. A second calibration uses a $250,000 aircraft, 20% deposit and $14,000 reserve: also 64 hours. These are game tuning examples, not live wages/prices or guaranteed loans. Economy wage integration and end-to-end balance verification remain outstanding.

Credit standing derives from flight experience, completed/failed jobs, safety, employer trust and repayment history. Affordability separately checks verified net income, existing debt payments, collateral, deposit and reserve. A larger aircraft is reachable only when both deposit savings and lender approval work. "Pilot level" is represented by career standing rather than a new XP grind. Full details: [credit and dealers](credit-and-dealers.md).

## Persistent world without absence punishment

Markets, employers, job supply, world events, maintenance queues, aircraft availability, relationships and other world state continue advancing while the app is closed.

The provisional default is protected absence: world markets/events advance, but personal fixed bills and interest do not accrue while away, whether solo or staffed. Offline passive profits and maintenance progress must also pause for the protected operation to prevent free-income exploits. The previous three-day grace / 30-day solo / 90-day staffed cap is superseded as the default; custom capped assessment remains available as a pure quote, not a payment. Its protected duration now includes grace so durations reconcile exactly.

A future opt-in funded-operations mode may operate only against a prepaid reserve, then enter protection. Hiring alone must never enable uncontrolled offline debt. This mode, interest integration, personal contract deadline suspension and the atomic settlement ledger are not implemented yet. Never sum cumulative assessments across repeated polls: settlement needs a persisted absence ID and one transactional reconciliation.

Bankruptcy eligibility requires repeated active-play missed obligations, insolvency and an offered recovery path. The provisional threshold is three consecutive active misses. The domain now reports warning, restructuring and eligibility; it never liquidates a save automatically. Employee work remains the recovery path. Loans and asset liquidation still need application and persistence implementation.

## Geography and home base

A career has a home airport. Geographic access expands through actual flying, legitimate reposition/ferry work, employer/customer relationships, route familiarity and later bases. The player cannot freely teleport the business to the best market.

Aircraft storage is a local economic resource. Airport size, demand and scarcity affect parking/hangar cost and availability. A job can legitimately move the pilot or aircraft and thereby expose a new local market without making that airport a permanent company base.

## KRME first test market

Griffiss International Airport (KRME) is the first mixed-use reference airport. Oneida County describes Griffiss as serving commercial, corporate, business, governmental and general-aviation needs. It is also an FAA-designated UAS test site and anchors New York's UAS test infrastructure. The Eastern Air Defense Sector and New York Air National Guard's 224th Air Defense Group are located at the Griffiss complex and conduct the eastern-U.S. air-defense mission.

Therefore KRME is not modeled as a conventional active fighter base. It receives a strong mixed demand profile: civilian, government, defense/air-defense support, UAS/research and public-service opportunities. Military job generation must still respect installed-aircraft capabilities, qualifications and authorization. Fighter intercept/escort sorties are eligible only when the player's aircraft/access supports them; their existence is driven by defense relationships rather than pretending fighters are permanently based at KRME.

The KRME test matrix includes cargo, charter, utility, government courier, UAS/survey/surveillance, emergency/public-sector work, military training/readiness, patrol/surveillance, logistics, ferry/reposition, and authorized intercept/escort scenarios.

## Long sessions and recovery

The dominant flight length is 1-3 hours, with supported jobs extending to roughly six hours. Active job/session state must be checkpointed periodically and at important state transitions. Resume must preserve contract identity, aircraft, origin/destination, timestamps, economics, telemetry-derived milestones and already-awarded effects. Completion remains evidence-driven after resume; restarting the app must never duplicate rewards.

## Management depth

Maintenance, financing, dispatch preparation and ground handling default to automation. Eligible manual ground procedures can provide a small bounded benefit. The initial manual reward policy caps direct reward at $35 per flight, deduplicates procedure kinds within one quote and requires a completed, high-confidence user-confirmed procedure. This is a quote only: self-confirmation is not independent simulator evidence, and cross-call/payment deduplication is not implemented. Before real payouts, require flight-scoped evidence of manual work, one transactional settlement per flight, and a cap relative to wages or saved service costs. Automated handling must not reduce baseline pay.

## Testing requirements

Tests must cover the 50-80 hour tuning invariant, inactivity grace/caps, long absences, staffed versus solo settlement, duplicate manual procedures, manual reward cap, home-base/geographic access, KRME mixed demand, authorization filtering, checkpoint idempotency, reconnect/resume, and bankruptcy recovery.

Time-dependent application code should depend on .NET `TimeProvider`; tests should use `FakeTimeProvider` so multi-day and multi-month scenarios execute deterministically without wall-clock waits.

## Real-world references

- [Oneida County airport overview](https://oneidacountyny.gov/departments/airport/), checked 2026-09-17: civilian/government aviation and UAS test-site role.
- Oneida County, FAA-designated New York UAS Test Site.
- [EADS About Us](https://www.eads.ang.af.mil/About-Us/), checked 2026-09-17: EADS and 224th ADG at Griffiss Business and Technology Park.

Real-world classifications are provenance-bearing inputs and should be refreshed independently from save-state schema so later source changes do not corrupt existing careers.

## Implementation boundary and next work — 2026-09-17

Implemented as simulator-independent domain rules: ownership tuning validation, protected absence default, local acceptance/departure checks, completed-contract travel and connection history with replay protection, 1–3 hour preference / six-hour job-duration ceiling, recovery-stage eligibility, and bounded manual reward quotes. The prior 29 SimLab scenarios are restored; separate xUnit tests cover these additions. No existing contract API was removed. Application orchestration must use the location-aware methods; direct JobContract callers still need location checks at their boundary.

Not yet playable: WinUI shell, SimConnect connection, flight detector, SQLite transactions/migrations, active-session checkpoints/resume, aircraft-location persistence, employer-paid passenger transfers, home-base relocation, route relationships, actual storage inventory/pricing/purchase, bankruptcy execution, reserve-pilot onboarding, mission-specific military objectives, and market-driven job generation. These require the foundation sequence in AGENTS.md. Serializing a domain record is not a crash-safe database save and does not restore an MSFS aircraft in flight.

Airport demand numbers are provisional game weights, not measured operation shares. Heavy active military airports need sourced classifications and higher military weighting than ordinary public airports. KRME retains its civil mix; authorization tests are synthetic scenarios, not evidence that actual escort or intercept operations are offered there. Before offering jobs, dispatch must still verify the installed aircraft, runway, payload, fuel, weather and military qualifications. Distinct escort/training/ferry/recon success conditions remain to be implemented.

Next vertical slice: WinUI connection-status shell, isolated SimConnect adapter, normalized telemetry, reliable flight-state detection and versioned SQLite session recovery at KRME. Then wire these policies through a single mission/ledger transaction flow and run representative 1-, 3- and 6-hour job scenarios, long absence/reload cases, and economic balance simulations. Do not expand finance complexity before that loop works.

The first test aircraft is now the F-22 at KRME. The user confirmed both cash purchase and financing paths. Exact installed F-22 add-on identity/performance remains to be detected in MSFS; no fabricated performance profile is used. Manual-procedure evidence remains provisional where telemetry is unavailable. Protected absence is a reversible proposal because the user explicitly left that choice open.
