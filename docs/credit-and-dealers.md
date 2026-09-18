# Career credit and aircraft dealers

Implemented initially 2026-09-17 as deterministic quote rules; expanded 2026-09-18 with transactional ownership persistence on `feature/economy-ownership`. User decision: F-22 at KRME is the first integration scenario; 50–80 real flying hours can fund either a small cash purchase or a larger financed purchase. The F-22 remains a military assignment, separate from civilian shopping. The specific installed add-on and its performance are not yet verified.

## Credit experience

Credit score is a game score from 300–850. Inputs are verified real flight hours (experience contribution caps at 80), completed and failed jobs, safety score, employer trust, on-time and missed loan payments. Smoothed history prevents one successful flight/payment creating perfect credit. Protected offline absence and simulator faults must never become player missed payments or failures.

The application must derive this snapshot from settled career records. Safety and employer scores are bounded 0–100 aggregates supplied by career scoring; the UI cannot assign them. No demographic data, real credit reports or XP gate is involved. Initial rates and weights are fictional game balancing values.

Loan approval requires acceptable score, no unresolved default, civilian asset eligibility, adequate deposit after reserve, and an affordable amortized payment. Principal is limited by the smallest of lender ceiling, loan-to-value against the lower of price/appraisal, and debt-service capacity. APR combines lender base, career risk premium and a bounded market-rate adjustment. Loan terms never change merely because the market subsequently changes; accepted terms must be persisted at origination.

| Lender | Minimum score | Base APR | Maximum risk premium | Maximum LTV | Debt-service share | Principal ceiling |
|---|---:|---:|---:|---:|---:|---:|
| Community Aviation Credit | 600 | 6% | 6 percentage points | 80% | 30% | $150,000 |
| Commercial Fleet Finance | 720 | 4.5% | 4.5 percentage points | 85% | 35% | $5,000,000 |
| Second Horizon Finance | 500 | 11% | 9 percentage points | 65% | 25% | $300,000 |

Income means a conservative monthly operating net income before debt service; existing debt payments are subtracted once. Do not count gross mission revenue as net income. Before integration, define income observation windows and account for seasonality, fuel, maintenance, insurance and local storage costs. Low history must not imply guaranteed business income. Rates are decimal annual interest rates with no fees currently modeled; monthly payments round upward to cents. Final amortization schedule must reconcile principal/interest and final-payment rounding.

## Dealers

Three fictional starting profiles accept distinct persisted inventory snapshots:

- Griffiss Used Aircraft: used inventory through $250,000, no markup, promotions up to 8%.
- Factory Aircraft Sales: new inventory through $100 million, 4% markup, promotions up to 5%.
- Regional Fleet Exchange: new/used inventory through $20 million, 2% markup, promotions up to 12%.

These are fictional businesses anchored at KRME for initial testing, not claims about real tenants. New/used listings retain distinct IDs, asking/appraised prices and condition. New condition must be 100%; used condition must be positive and at most 100%. Inventory generation, wear records and condition-based appraisal remain outside this quote layer. Installed aircraft supply capabilities; no whitelist of purchasable models is introduced.

Discount chance and size improve with credit standing and dealer relationship. Seed includes career seed, dealer ID, listing ID and UTC day. Inventory ordering and screen refresh do not change a draw. Offers expire at next UTC midnight. Persist the offered terms and history snapshot; do not rebuild issued prices when career statistics update. Stable listing IDs must survive reload. Different days can have different promotions but a deal is never guaranteed. Maximum chance is 75%, and each dealer's discount cap is enforced.

Cash buyers need enough available cash after operating reserve; credit-score approval is not required for cash. Financing quotes use discounted sale price and current appraisal. Quote generation remains side-effect free. The ownership transaction layer revalidates persisted stock, offer and storage evidence before transferring money or ownership atomically. Remote offers may be browsed later, but purchase/delivery must use the existing geography rules; shopping must not teleport the pilot or aircraft. Military-only aircraft are filtered from civilian inventory and rejected by civilian underwriting.

## Integration and data integrity work remaining

Purpose/player experience: shop local new/used aircraft, compare terms, see explicit loan rejection reasons, and choose cash versus borrowing without mandatory grind.

Application boundary: load settled history, dealer inventory and issued offers; evaluate quotes; refresh eligibility at acceptance. Quote records are trusted domain output, not signed proof: never accept a client-submitted price, ownership flag, score or appraisal without reloading authoritative records.

Persistence: add versioned SQLite records for career evidence aggregates, unique asset/listing IDs, dealer relationships, offers, loans and amortization entries. One transaction must consume inventory once, debit deposit, create the loan, transfer ownership and assign storage/delivery. Use idempotent purchase IDs and optimistic concurrency. Persist terms and history provenance. Add migrations, backups and rollback tests before real money settlement.

Simulator inputs: installed aircraft identity and registry capability/access metadata; normalized verified flight outcomes feed scoring. Live F-22 testing still depends on SimConnect, flight detection and mission-specific military objective validation. Military access does not permit buying the assigned F-22.

UI: dealer inventory/comparison, condition/appraisal, sale price/discount/expiry, cash remaining after reserve, credit factors, APR, term, monthly payment, deposit and decline reason. No UI is implemented in this change.

Events/failure cases: stock sold, expired offer, stale affordability, repayment due, paid/missed repayment, changed appraisal, default, recovery and world rate shock. Protected absence must remain protected. No random credit rejection or random default. Inventory refresh and loan acceptance must not reroll discounts.

Acceptance gates now also cover atomic cash/financed purchase, duplicate purchase rejection, rollback, persisted loan schedules, insurance redo persistence and maintenance persistence. Actual recurring loan servicing/default/restructure and price/wage balancing over 50–80 settled flying hours remain downstream.

## Verification references

Wolfram independently evaluated the 120-month payment on $80,000 at 6% annual interest as $888.1640155332152, rounded upward by the game to $888.17. Context7's official [.NET immutable serialization documentation](https://github.com/dotnet/docs/blob/main/docs/standard/serialization/system-text-json/immutability.md) informed checkpoint representation. No live financial rates or real aircraft prices are asserted.
