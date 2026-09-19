# FSEconomy historical calibration reference

Updated: 2026-09-19.

## Purpose

FSEconomy is an external historical reference dataset for OpenCareer calibration. It does **not** define OpenCareer prices, wages, progression, ownership rules, mission payouts, credit terms, market behavior, or runtime state.

OpenCareer remains authoritative.

Reference repository:

- `SKCwillie/FSEconomy-service`
- latest observed repository activity: 2023
- source aircraft fields include seats, cruise speed, fuel burn, purchase price, engine price, MTOW, empty weight and maximum cargo
- job examples and API models also expose route distance, job pay, passenger/cargo quantity, dry/wet rental values, return passengers and derived earnings

The upstream repository contains no license file in the inspected root. For that reason OpenCareer stores only a small factual calibration sample and provenance, not a wholesale copy of the upstream dataset.

## Calibration rule

Use FSEconomy to constrain **relationships**, not copy absolute economy values.

Useful ratios:

- aircraft price / usable seat
- aircraft price / payload capacity
- engine price / aircraft price
- fuel burn / flight hour
- fuel burn / nautical mile
- rental rate / aircraft value
- job pay / nautical mile
- job pay / passenger-nautical-mile
- job pay / cargo-nautical-mile
- net earnings / flight hour
- operating cost / gross revenue

OpenCareer progression targets remain stronger constraints than any FSEconomy value.

Current OpenCareer targets:

- first meaningful ownership: 50-80 career-credit flight hours
- representative cash acquisition requirement: about $64,000 including reserve
- target early net savings: about $1,000 per career-credit flight hour
- no advantage for marathon sessions
- financing affordability is separate from deposit accumulation
- absence does not create punitive catch-up liabilities

## How to use the sample

The checked-in sample is at:

`data/calibration/fseconomy-aircraft-reference.csv`

It spans light piston, utility, turboprop, light jet, regional and airline aircraft. Values are historical FSEconomy values and are not assertions about current real-world market prices.

For each OpenCareer aircraft class:

1. map one or more historical reference aircraft,
2. calculate relative price/capacity/operating-pressure ratios,
3. normalize the resulting curve around OpenCareer's desired progression,
4. reject parameter sets that invert sensible class progression without an explicit gameplay reason,
5. run focused scenario sweeps,
6. run broad regression/fuzz simulations only after the parameter range is narrow.

## Do not do this

Do not:

- call the FSEconomy service during normal gameplay,
- require an FSEconomy account or API key,
- make save files depend on FSEconomy identifiers,
- copy FSEconomy's booking-fee or financial formulas blindly,
- make FSEconomy's historical dollar amounts authoritative,
- replace OpenCareer's deterministic market, credit, dealer, ledger, mission or progression systems.

## Known upstream caveat

The inspected upstream `get_financials` implementation calculates dry cost with:

`job_time * (RentalDry + fuel_burn + FUEL_PRICE)`

That is not a conventional fuel-cost calculation of fuel burn multiplied by fuel price. Use upstream raw fields and empirical relationships rather than treating that derived formula as authoritative.

## Faster calibration workflow

Previous broad search:

`large parameter sweep -> inspect failures -> retune -> repeat`

Preferred workflow:

`historical relationship fitting -> OpenCareer progression constraints -> focused parameter sweep -> edge-case scenarios -> large regression run`

This reduces the search space while preserving OpenCareer's unique economy.
