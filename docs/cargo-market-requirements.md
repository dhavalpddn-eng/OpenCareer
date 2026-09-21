# Named cargo and commodity market requirements

Status: accepted gameplay requirement. Capture now; implement after the playable flight/session/dispatch foundation. This document must not be used to justify expanding finance complexity before Chapter 4–6 are functional.

## Product requirement

A cargo job must not be represented only as "general cargo: 4,000 lb."

Every generated cargo contract should carry one or more **named commodity lots** with economic identity.

Examples explicitly requested by the user include:

- coffee,
- phones,
- televisions,
- food,
- live plants / horticulture.

The catalog must be data-driven and extensible to a broad real-world-style set of goods rather than a hard-coded handful.

"Cargo" is a mission/job family. It is **not** the commodity name.

## Commodity catalog

A future `CommodityDefinition` should be able to represent:

- stable commodity ID,
- display name,
- category/subcategory,
- unit of trade,
- typical unit mass,
- typical unit volume,
- base/reference wholesale value,
- density / volume-limited behavior,
- perishability,
- shelf-life,
- refrigeration/frozen requirements,
- live-organism handling where applicable,
- fragility,
- theft/security risk,
- hazardous-material classification where applicable,
- special handling,
- value density,
- economic market segment,
- allowed/prohibited mission contexts.

Examples of distinct definitions rather than one "food" bucket:

- green coffee beans,
- roasted coffee,
- fresh berries,
- frozen food,
- shelf-stable packaged food,
- cut flowers,
- nursery plants,
- smartphones,
- televisions,
- laptops,
- semiconductors,
- medical supplies,
- machine parts,
- textiles,
- mail/parcels.

The goal is breadth and meaningful economics, not literally mirroring every SKU sold in the world.

## Commodity lots / manifest

A shipment contains one or more `CommodityLot` records.

A lot should carry:

- commodity ID,
- quantity and unit,
- physical mass,
- volume where relevant,
- origin,
- destination,
- ownership/shipper/consignee,
- accepted/loaded timestamp,
- declared value,
- origin market price snapshot,
- destination market price snapshot or delivery valuation,
- handling requirements,
- condition/quality,
- expiration/spoilage state where relevant,
- contract-specific insurance/security requirements.

The flight record must preserve the manifest that was actually accepted. Later catalog-price changes must not rewrite history.

## Market snapshots

Commodity economics should reuse the deterministic world/economy foundation rather than inventing a disconnected random price generator.

Each airport/region/time market can expose a `CommodityMarketSnapshot` containing:

- local supply pressure,
- local demand pressure,
- local price,
- recent direction/trend,
- scarcity/surplus classification,
- active event modifiers,
- availability/stock confidence.

Prices are **in-game market values** unless a specific external data source is explicitly connected and labeled. Do not present generated values as current real-world wholesale quotes.

Offline-first core gameplay must not depend on internet pricing.

## Origin and destination economics

For each cargo lot, the UI should be able to explain:

- origin unit price,
- origin shipment value,
- destination unit price,
- destination shipment value,
- absolute/percentage market spread,
- why the route exists: surplus, shortage, urgent demand, event, contract relationship, scheduled supply chain, etc.

Example presentation:

```text
Roasted coffee
Quantity: 6,000 lb
Origin market: $7.40/lb
Origin lot value: $44,400
Destination market: $8.05/lb
Destination value: $48,300
Market spread: +8.8%
Freight contract pay: $3,260
```

Those numbers are illustrative only.

## Freight pay is not cargo value

Transporting $500,000 of phones does not mean the pilot earns $500,000.

Freight compensation should be calculated separately from shipment market value using factors such as:

- route distance/time,
- mass,
- volume,
- aircraft capacity utilization,
- urgency,
- handling complexity,
- perishability,
- security/theft risk,
- hazardous or regulated handling,
- route difficulty,
- customer/employer relationship,
- market scarcity,
- reliability requirements,
- declared-value/insurance surcharge.

Shipment value matters for risk, insurance, security and consequence, but ordinary carrier pay is freight revenue.

A later player-owned trading/business mode may allow the company to own inventory and realize buy/sell spread. That is a distinct mechanic from being paid to transport somebody else's goods.

## Commodity behavior examples

### Coffee

Distinguish green beans from roasted coffee. They can have different value, sources, demand and handling profiles. Harvest disruption, port/ground-logistics issues or regional café/retail demand can change job supply and price.

### Phones

High value per pound, relatively low volume, theft/security-sensitive. Small aircraft can transport economically valuable loads without enormous weight.

### Televisions

Bulkier and fragile with lower value density than phones. Jobs can become volume-limited before weight-limited.

### Food

Split into perishability/temperature classes. Fresh and frozen products create time and temperature constraints; shelf-stable products behave differently.

### Plants / flowers

Time-sensitive and handling-sensitive. Live plants/flowers may have temperature, delay and route/regulatory constraints. The game should model the economic/condition effect without pretending to provide real customs/agricultural legal advice.

## Economic events and job generation

Named goods allow existing world events to create specific consequences.

Examples:

- poor coffee harvest -> reduced supply, higher destination prices, more urgent coffee movements,
- electronics factory outage -> phone/TV scarcity and altered lanes,
- festival/holiday demand -> food/flowers/consumer goods surge,
- storm -> emergency food/water/medical logistics,
- agricultural disease/restriction -> plant movements reduced or rerouted,
- military/government event -> parts, equipment, food, medical and other authorized logistics demand.

Event effects must remain scoped and deterministic under the existing world-simulation invariants.

## Cargo condition and mission outcome

Delivery should preserve more than "arrived/not arrived."

Potential condition inputs:

- excessive delay,
- hard landing / high G,
- temperature-control failure when that system exists,
- prohibited aircraft/environment exposure,
- damage event,
- diversion,
- partial delivery,
- mission-specific handling violations.

Do not invent sensor evidence the simulator cannot provide. Unsupported condition checks should remain unavailable or mission-authored rather than guessed.

## Postflight cargo report

For each lot show:

- commodity name,
- quantity,
- weight/volume,
- origin and destination,
- starting/ending condition,
- declared value,
- origin/destination market value,
- market spread,
- freight pay attributable to the shipment,
- penalties/bonuses and why,
- reputation/relationship effect,
- economic event/context that influenced the job when applicable.

The overall flight report can aggregate total cargo mass and total declared/market value while retaining individual line items.

## Data architecture

Keep the boundaries separate:

```text
Economy/world simulation
  -> Commodity market snapshots
  -> Job generator selects named commodity lots
  -> Dispatch validates aircraft/runway/payload/range
  -> FlightSession carries immutable accepted manifest
  -> Mission validator evaluates delivery/condition
  -> Atomic settlement pays freight and changes reputation/economy once
  -> Postflight UI explains physical + market outcome
```

AI may generate narrative around a shipment, customer or disruption. AI never chooses authoritative quantity, value, payment, market state or completion.

## Implementation order

Do not implement the full commodity catalog now.

Required order remains:

1. live SimConnect validation,
2. flight-state detection,
3. durable FlightSession recovery,
4. aircraft/runway/dispatch feasibility,
5. first end-to-end mission settlement,
6. then named commodity catalog + market-driven cargo generation.

Before step 6, preserve these requirements in domain/API designs so no early cargo system assumes a single anonymous "cargo" field.
