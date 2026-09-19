# Long-term career balance audit

Updated: 2026-09-19.

This audit runs only on the isolated `feature/economy-loan-balance-review` branch. It does not modify the sidework production branch.

## Scope

The long-term harness runs 50 deterministic careers per batch:

- 5 pilot skill/reliability profiles: Elite, Strong, Average, Struggling, Risky/Recovering;
- 5 session styles: Short, Balanced, Long, Marathon, Weekend Mixed;
- 2 career strategies: Airline Employee and Owner-Operator.

Each career runs to **1,000 active career-credit flight hours**. One batch therefore represents 50,000 active flight hours. Two independent seed batches represent **100 distinct long-term careers / 100,000 active flight hours**.

The simulator uses current domain code for pay, career level, credit, lender decisions, airline employment access, extended-duty premium, employer trust, protected absence, and owner operating-cost recovery.

The wear/repair layer remains a review-only stress model because production component wear/repair persistence is not implemented yet.

## Long-term acceptance gates

A scenario is treated as balanced only when:

- employment never requires aircraft ownership;
- airline progression can reach regional jet, narrowbody and widebody within the skill-adjusted endgame windows;
- qualifications and employer trust remain required in addition to career level;
- first ownership does not appear implausibly early and remains reachable for persistent owner-operator careers;
- marathon duties retain their bounded hourly premium without becoming the only viable progression path;
- owner careers do not become insolvent from modeled maintenance/repair pressure;
- normal owner earnings do not collapse far below equivalent employee earnings;
- major repairs may draw the operating reserve, but cannot exceed both repair reserve and available operating cash;
- protected real-world absence creates no catch-up ownership bill;
- all careers finish without negative ending cash.

## First 50-career pass: issues found

The initial substantive 50-career run exposed eight balance failures:

- Elite / Long owner: first ownership at 90h, later than the 85h target;
- Elite / Marathon owner: first ownership at 192h;
- Strong / Long owner: first ownership at 90h;
- Strong / Marathon owner: first ownership at 198h;
- Average / Marathon owner: first ownership at 198h;
- Struggling / Marathon owner: first ownership at 192h;
- Risky/Recovering / Marathon owner: first ownership at 346h;
- Risky/Recovering / Long owner: two repair events exceeded accumulated repair reserve.

The ownership delays were not caused by money. They came from the review rule requiring a raw minimum completed-job count. That unintentionally punished players who flew fewer, longer jobs.

## Tuning pass 1

### Ownership pacing

Removed raw completed-job-count gating from first-aircraft ownership.

First ownership is now paced by:

- the required qualification;
- actual earned cash;
- the aircraft price;
- required operating reserve;
- financing eligibility for later financed upgrades.

This preserves the intended 50–80h-ish early ownership economy without discriminating against marathon pilots simply because they complete fewer contracts per hour.

### Repair reserves

Raised the provisional per-active-hour repair reserve used by the balance model:

| Aircraft class | Before | Tuned |
| --- | ---: | ---: |
| Basic piston | $8/h | **$12/h** |
| Advanced twin | $25/h | **$32/h** |
| Turboprop | $55/h | **$70/h** |
| Light jet | $110/h | **$140/h** |

Routine maintenance rates were not raised in this pass. The extra reserve is specifically for unscheduled repairs and condition risk.

After this pass, all ownership-timing failures disappeared. One repair-model warning remained: the Risky/Recovering long-session owner had two major repairs larger than the accumulated hourly repair reserve.

## Tuning pass 2

The remaining warning exposed a modeling error rather than an economy defect.

A major repair is not supposed to be paid only from the accumulated hourly repair bucket. The aircraft-class operating reserve exists specifically to absorb infrequent shocks.

The balance rule was corrected to settle repairs in this order:

1. accumulated repair reserve;
2. available operating cash/reserve;
3. only flag a balance failure if the repair exceeds both.

Drawing the operating reserve is therefore allowed and expected after an unusually expensive repair. Insolvency is not.

## Final verification

After the second tuning pass:

- final first 50-career batch: **0 balance violations**;
- Linux run `35429344500`: **296/296 xUnit passed** plus deterministic SimLab;
- second independent 50-career seed batch: **0 balance violations**;
- Linux run `35429398121`: **297/297 xUnit passed** plus deterministic SimLab.

The second batch uses different deterministic event/repair/failure seeds, so this is not merely the same 50 careers rerun.

Across the final audit the review branch now covers **100 distinct 1,000-hour careers / 100,000 active flight hours** without a long-term balance violation under the defined gates.

## What was not tuned

The following were deliberately left alone:

- the 25% maximum extended-duty premium;
- protected absence / zero offline personal catch-up bills;
- employer-paid fuel, maintenance and airport fees for airline employees;
- airline path requiring qualifications, experience and employer trust;
- regional -> narrowbody -> widebody endgame structure;
- personal airliner ownership being unnecessary for airline flying;
- loan amortization math and lender formulas owned by the separate finance workstream.

## Production boundary

The long-term test proves internal balance for the current review model; it does not claim the unfinished systems already exist.

Still required before these values become authoritative production gameplay:

- explicit license/type-rating persistence;
- authoritative aircraft wear/component-health/damage model;
- real repair shop/MRO state and parts availability;
- company/business management and airline fleet economics;
- live job-generation distributions feeding the same stress harness;
- playtesting for fun and perceived pacing, not only numerical stability.