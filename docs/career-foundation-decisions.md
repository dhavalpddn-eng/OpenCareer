# Career foundation decisions

Status: authoritative design baseline for implementation and balancing.

## Early progression

The first personally owned used light aircraft should normally become financially reachable after roughly 50-80 real flying hours. The initial calibration uses a $60,000 representative used light aircraft, 10% down payment, $2,000 protected operating reserve, and about $125/hour net career savings. This produces an acquisition-cash target of $8,000 and a center point of 64 flying hours. Aircraft prices and financing remain market-driven; this is a tuning invariant, not a guaranteed unlock.

The player may remain an employee, use employer aircraft, take independent work, finance earlier at greater risk, or wait longer and buy with a stronger balance sheet. There is no XP gate on ownership.

## Persistent world without absence punishment

Markets, employers, job supply, world events, maintenance queues, aircraft availability, relationships and other world state continue advancing while the app is closed.

Fixed player liabilities use an inactivity settlement policy. The baseline gives three days of grace, then accrues solo fixed liabilities for at most 30 billable days during one absence. A staffed/passive operation can accrue for up to 90 days because it can also continue generating operational results. Time beyond the cap is recorded as protected inactive time rather than hidden debt.

This is not a bankruptcy shield during active play. Accepted contracts, deliberate purchases, active financing decisions, maintenance neglect, losses and other player-created obligations remain consequential. Bankruptcy should emerge from sustained poor decisions or excessive leverage, with employee work remaining a recovery path.

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

Maintenance, financing, dispatch preparation and ground handling default to automation. Eligible manual ground procedures can provide a small bounded benefit. The initial manual reward policy caps direct reward at $35 per flight, rejects duplicates and requires a completed, high-confidence user-confirmed procedure. This makes manual handling meaningful without turning repetitive clicking into the dominant income source.

## Testing requirements

Tests must cover the 50-80 hour tuning invariant, inactivity grace/caps, long absences, staffed versus solo settlement, duplicate manual procedures, manual reward cap, home-base/geographic access, KRME mixed demand, authorization filtering, checkpoint idempotency, reconnect/resume, and bankruptcy recovery.

Time-dependent application code should depend on .NET `TimeProvider`; tests should use `FakeTimeProvider` so multi-day and multi-month scenarios execute deterministically without wall-clock waits.

## Real-world references

- Oneida County, Griffiss International Airport overview and UAS program.
- Oneida County, FAA-designated New York UAS Test Site.
- Eastern Air Defense Sector, About Us / 224th Air Defense Group.

Real-world classifications are provenance-bearing inputs and should be refreshed independently from save-state schema so later source changes do not corrupt existing careers.
