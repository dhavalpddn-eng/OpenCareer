# Airline career and company path

Updated: 2026-09-19.

This document records the intended airliner endgame without claiming the full company-management system already exists.

## Current implementation status

Implemented domain foundations:

- `EmployerType.Airline`;
- `EmployerScale.MajorAirline`;
- employer trust and relationship tiers;
- employer-provided deadhead eligibility;
- employee contract compensation where the employer covers fuel, maintenance and airport fees;
- career levels as meta-progression;
- aircraft capability/access checks;
- review-branch airline aircraft classes and employment requirements.

Not implemented end-to-end:

- persistent airline roster/employment contracts;
- airline bases and bid/schedule system;
- seniority;
- aircraft type-rating/qualification persistence;
- reserve/line-holder scheduling;
- airline-owned fleet state;
- player-owned company management;
- hiring employees;
- leasing/buying company aircraft;
- company routes, market share and operating P&L.

The full business/competition system is still the planned `P2 / M4-01 Competition and company management` milestone.

## Airline employment path

The player should be able to fly employer aircraft long before being able to personally buy an airliner.

Current review targets:

| Class | Visibility level | Verified flight hours | Earned qualifications | Employer trust | Typical career role |
| --- | ---: | ---: | ---: | --- | --- |
| Regional jet | 24 | 350 | 5 | Trusted | first airline/commuter jet |
| Narrowbody | 30 | 500 | 6 | Preferred | 737/A320-class airline work |
| Widebody | 35 | 700 | 7 | Partner | endgame long-haul airline work |

Level only controls visibility/meta-progression. It never replaces the required qualifications, flight experience or employer trust.

The review matrix currently reaches:
- regional-jet airline employment around 420 hours;
- narrowbody airline captain around 520 hours;
- widebody endgame airline captain around 750 hours.

These are balance targets, not hard real-world licensing claims.

## Virtual airline player experience

The intended progression is:

1. Start with local/general-aviation employee work.
2. Build verified hours, qualifications and employer relationships.
3. Join regional/cargo/charter employers and fly their aircraft.
4. Move into a virtual airline without buying the airplane.
5. Progress through regional jet -> narrowbody -> widebody assignments.
6. Save money while the airline pays aircraft operating costs.
7. Use savings/credit later for personal aircraft, a charter/cargo operator, or eventually a player-owned company.
8. Company ownership becomes a separate management game: fleet, leases/loans, employees, routes, maintenance, insurance, storage and operating cash.

Airliner access therefore does not depend on becoming rich enough to personally own a 737/A320 or widebody.

## Employer versus owner economics

For airline employment:
- player receives pilot wages;
- employer covers fuel;
- employer covers maintenance;
- employer covers airport fees;
- the player does not pay aircraft loan, insurance or repair shocks.

For a future player-owned airline:
- the company receives route/customer revenue;
- company cash pays fuel, maintenance, airport fees, insurance, leasing/loan payments and payroll;
- aircraft condition and repair reserves matter;
- personal cash and company cash remain separate;
- the player can still fly company aircraft as a pilot.

AI must never determine company balances, ownership or settlement.

## Endgame rule

Widebody airline flying is a required endgame path for the career design.

The player must not be forced to own a widebody to fly one. A successful airline career can remain an employee career indefinitely, while company ownership is optional deeper progression.

The business system should later support both:
- **career pilot endgame:** captain/long-haul/widebody progression with employer aircraft;
- **entrepreneur endgame:** own/manage a charter, cargo or airline company and operate a fleet.

These paths can overlap but neither replaces the other.