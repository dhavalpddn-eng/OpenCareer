# Economy / settlement foundation

Updated: 2026-09-19.

Read `AGENTS.md` and `docs/project-state.md` first. This is the compact handoff for economy/finance work.

## Branch and verification

- Branch: `feature/economy-ledger-settlement`
- Base: `feature/military-operations-foundation` at `4ca6f8815581dd71a1e54a17da8c5da0cf41a0dd`.
- Current code head before this handoff update: `dec4b8349b75ad37503e90f08d1a8cbdbfc3243e`.
- Latest **fully verified** implementation in this work block: `955ce454cec28844db67fbd61acda6d4dbfc3dbb`.
- Linux run 35454813105 at `955ce454`: **227/227 xUnit + 29/29 SimLab**, 0 build warnings/errors.
- Windows run 35454813132 at `955ce454`: WinUI x64 Release + live-probe build + **227/227 xUnit**, 0 build warnings/errors.
- A later real test execution at `c2c3e56c` reached **233/234 passing**; the only failing test was the same-purchase retry path re-running affordability after the initial cash debit. Commit `dec4b834` fixes that by recognizing the persisted purchase before rechecking affordability.
- The first Actions jobs for `dec4b834` failed **before any workflow steps were created** (`steps=[]`, no job log blob), so the current head is not yet being called CI-verified. Re-run both Linux and Windows CI before merging.

## 2026-09-19 pause checkpoint

This work block materially expanded the economy from "pay and ledger" into the first acquisition/ownership accounting path:

- explicit career opening-balance ledger transaction, with stable idempotency and append-only `OpeningEquity = 13` persisted account code;
- cash and financed aircraft purchase accounting;
- immutable aircraft-loan agreement generated only from an approved `LoanDecision`;
- cent-rounded amortization by active-play billing cycle, including final-payment reconciliation;
- recurring ownership schedule builder that feeds exact loan principal/interest plus insurance/storage/other fixed costs into the existing active-play billing engine;
- atomic SQLite purchase persistence that writes the purchase ledger transaction and consumes a unique listing/ownership in one database transaction;
- financed purchase persistence stores loan ID, lender, APR, term and scheduled payment as immutable origination terms;
- purchase retry path is idempotent and does not charge cash twice;
- ledger account-balance query;
- Finances UI summary cards for settled income, operating costs, aircraft assets and outstanding loan balance;
- immutable `ContractEconomicSnapshot` on `JobContract`, so accepted compensation and the quote inputs/multipliers are not rewritten when later market conditions change.

Wolfram independently rechecked the existing 120-cycle example of **$80,000 at 6% with a $888.17 scheduled payment** using the same cent-rounding convention: first cycle interest/principal is **$400.00 / $488.17**, final payment is about **$887.23**, total principal is exactly **$80,000**, and the ending balance is $0. These are gameplay-finance calculations, not current real loan offers.

## Existing economy retained

The branch keeps the previously implemented deterministic foundations:

- `MarketState` / `MarketTickEngine`: demand, capacity, backlog, prices and structural-capacity response.
- `EconomicCycleEngine`: deterministic regional demand/cost cycles with mean reversion.
- `RouteDemandProfile`: passenger and cargo pressure / scarcity / trend / urgency.
- `CareerCredit`: game-only 300–850 credit score, lender terms, LTV and debt-service affordability.
- `AircraftDealer`: fictional dealers, persisted-stock quote model, deterministic promotions/discounts and civilian-ownership restrictions.
- `BankruptcyPolicy` and `OfflineLiabilityPolicy`: bounded recovery stages and protected-absence rules.

Do not replace those systems with UI-entered balances or AI-authored numbers.

## New authoritative ledger

Files:

- `src/OpenCareer.Domain/Economy/EconomyLedger.cs`
- `src/OpenCareer.Application/Economy/IEconomyLedgerStore.cs`
- `src/OpenCareer.Application/Economy/EconomySettlementService.cs`
- `src/OpenCareer.Infrastructure/Economy/SqliteEconomyLedgerStore.cs`

The ledger is balanced and auditable.

Current accounts include:

- Cash
- ContractRevenue
- WageIncome
- ReimbursementIncome
- FuelExpense
- MaintenanceExpense
- AirportFeesExpense
- OtherOperatingExpense
- InsuranceExpense
- InterestExpense
- AircraftAsset
- LoanPayable
- StorageExpense
- OpeningEquity

Money values are whole cents. Each non-zero posting has exactly one debit or one credit. Each transaction must balance.

SQLite stores money as integer cents rather than floating-point currency.

## Exactly-once contract settlement

`ContractSettlementEngine` accepts only a validated `JobContract` whose status is `Completed` and whose completion timestamp is not later than settlement.

Current treatment:

- Pilot wage / salary duty -> player receives `PilotCompensation`.
- Direct mission fee -> player receives the quoted mission fee.
- Company revenue -> company/player cash receives `GrossCustomerRevenue`.
- Employer-covered fuel, maintenance and airport fees are not charged to player cash.
- Owner/independent operating costs create expense postings and reduce cash.
- Other explicitly supplied operating costs remain player costs.

The transaction ID is the contract ID and the idempotency key is stable per contract settlement.

The SQLite store rejects conflicting reuse of either ID/key. Same-transaction retries return `AlreadyPosted`. A per-store write gate serializes concurrent posts, and tests prove 12 simultaneous duplicate posts credit cash exactly once.

This is the authoritative money movement layer. A quote, mission result, AI narration or UI button cannot change money without a ledger transaction.

## Deterministic pay quotes

File:

- `src/OpenCareer.Domain/Economy/ContractPayQuoteEngine.cs`

Accepted user direction is implemented:

1. time,
2. distance,
3. payload-distance,
4. market demand,
5. urgency,
6. difficulty,
7. employer/relationship strength,
8. estimated operating-cost recovery when the player/company bears those costs.

Current default gameplay calibration:

- time component: $850 per estimated flight hour,
- distance component: $0.65/NM,
- payload-distance component: $0.0001 per lb-NM,
- demand factor is bounded,
- urgency/difficulty/relationship add bounded premiums,
- owner/operator quotes can recover estimated costs plus a 10% cost-recovery margin.

These are **gameplay tuning values**, not real pilot wage, freight-rate or aircraft-market claims.

A representative early employee job used in tests:

- 3 flight hours,
- 400 NM,
- 500 lb,
- balanced demand,
- 10% urgency,
- 10% difficulty,
- 20% relationship.

Wolfram independently evaluates the current policy at approximately **$2,883.66 pilot cash pay**, or **$961.22 per flight hour**. At that unchanged illustrative rate, 65 hours would produce about **$62,479** before discretionary spending, close to the existing $64,000 cash/reserve first-aircraft tuning target. Real playtesting must still tune the distribution rather than one example.

## Casual-play recurring ownership costs

Recurring ownership liabilities now have an authoritative **active-play** settlement foundation rather than a real-calendar punishment loop.

Rules:

- one gameplay billing cycle is **30 career-credit flight hours**;
- the default protected-absence policy still accrues **$0 while the player is away**, including long real-world absences;
- pause, slew and invalid/non-credit simulator time do not advance the billing clock;
- simulation acceleration cannot multiply the billing clock because settlement uses `FlightTimeLedger.CareerCreditTime`;
- there is **no minimum charge per session**: one 15-hour block and fifteen 1-hour blocks telescope to the same cent-rounded liability;
- billing-cycle progress is stored in SQLite and advanced in the same transaction as the recurring-cost ledger posting, so restarting the app cannot reset accrued active-play progress;
- duplicate activity settlement uses a stable ownership/activity idempotency key; stale concurrent billing state is rejected instead of overwriting progress;
- loan principal reduces `LoanPayable`; interest, insurance and storage use distinct ledger expense accounts;
- a flight that crosses a 30-hour cycle boundary can consume the next supplied cost cycle without requiring a marathon session.

Current representative financed-light-aircraft stress fixture:

- loan payment component: **$455.79 per active billing cycle**,
- insurance + storage: **$670.00 per cycle**,
- total fixed cash carrying cost: **$1,125.79 per 30 active hours**,
- ordinary 50-hour fallback maintenance fixture: **$575**,
- combined fixed + routine-maintenance burden: about **$49.03 per active hour**, or about **4.9%** of the current $1,000/hour progression center before fuel/airport costs and their contract cost-recovery treatment.

Wolfram independently evaluates that combined representative burden at about **$49.026/hour**. This is gameplay calibration, not a real-world ownership-cost claim.

Session length is not a progression multiplier. Employee quote regression coverage now compares plausible **1h / 3h / 6h** jobs and keeps their hourly pay within roughly 0.25% of each other under equal context. Normal generated jobs above the six-hour session ceiling receive zero duration suitability; extended ferry/reposition/military transport remains optional special work. Early-career generation now prefers **1–3 hour** jobs.

## Persistence behavior

`SqliteEconomyLedgerStore`:

- creates ledger tables lazily,
- uses a SQLite transaction for each post,
- stores postings under the transaction header,
- keeps a unique transaction ID and unique idempotency key,
- re-reads an existing transaction before returning `AlreadyPosted`,
- rejects changed financial data under an existing ID/key,
- persists and reopens correctly,
- returns current cash and recent transactions,
- disables SQLite connection pooling for this store so save files release cleanly on Windows.

## WinUI Finances page

The `Finances` NavigationView destination now opens a real page:

- `Views/FinancesPage.xaml`
- `ViewModels/FinancesViewModel.cs`

The app composition root wires the ledger to:

`%LOCALAPPDATA%\OpenCareer\opencareer.db`

Current page shows:

- authoritative available cash,
- settled income,
- operating costs,
- aircraft asset book value posted by the ledger,
- outstanding loan liability,
- ledger status,
- recent transactions,
- explicit settlement rules.

It is intentionally read-only. It does not allow the UI to type a balance or fabricate transactions.

## Important boundaries

Not yet implemented end-to-end:

- generated job offer -> final pay quote -> persisted `JobContract` creation (the immutable accepted economic snapshot now exists);
- verified `FlightSession` fuel/fees/maintenance actuals -> settlement;
- app/career-creation wiring for the implemented opening-balance transaction (starting cash is still a product decision);
- fleet-registry ownership transfer/storage-delivery wiring after the implemented atomic purchase accounting/persistence;
- final ownership adapter that chooses the persisted loan/insurance/storage schedule automatically for each owned aircraft;
- direct advancement/reporting of loan payoff state from completed active-play billing cycles;
- deadhead travel settlement;
- company payroll;
- named commodity lots and shipment market value;
- income-history observation window for credit underwriting.

The market simulation can influence future quotes, but existing jobs must preserve accepted quoted compensation so market changes do not rewrite history.

## Next bounded economy work

1. Re-run Linux + Windows CI for `dec4b834`; do not merge until the post-fix head executes real workflow steps and passes.
2. Bind `ContractPayQuoteEngine` + `ContractEconomicSnapshot` when a `JobMarketOfferDraft` becomes a validated persisted `JobContract`.
3. Feed verified FlightSession actual fuel/airport/maintenance costs into `ContractSettlementCosts`.
4. Decide starting cash, then wire `CareerEconomyInitializationService` into new-career creation exactly once.
5. Connect the persisted aircraft purchase record to the fleet registry, storage location and delivery/geography rules; the financial/listing-consumption transaction already exists.
6. Use the persisted loan agreement to build each ownership's recurring-cost schedule automatically, then surface remaining principal/payoff progress in Finance/Fleet.
7. Add paid personal deadhead and employer-duty deadhead settlement.
8. Add company payroll/operating-cost settlement only after the single-aircraft ownership loop is playable.
9. After the flight/session/dispatch loop is playable, implement the named commodity catalog from `docs/cargo-market-requirements.md`.
10. Run broad balance simulations/playtests against the 50–80-hour ownership target and tune distributions rather than isolated examples.

## Product decisions that should be confirmed before deeper tuning

- Starting cash and whether the pilot begins with any debt.
- How game-accelerated employee pay should be relative to company/owner revenue.
- Whether player-owned companies can intentionally accept loss-leading contracts for reputation.
- Loan late-payment grace, repossession/default severity and recovery path.
- Insurance deductibles/coverage tiers.
- Whether taxes are omitted entirely, simplified, or modeled only for owned companies.
