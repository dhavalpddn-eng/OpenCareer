# Economy / settlement foundation

Updated: 2026-09-18.

Read `AGENTS.md` and `docs/project-state.md` first. This is the compact handoff for economy/finance work.

## Branch and verification

- Branch: `feature/economy-ledger-settlement`
- Base: `feature/military-operations-foundation` at `4ca6f8815581dd71a1e54a17da8c5da0cf41a0dd`
- Latest verified implementation: `d8b25f6b0820d831bd9a45c04894605294203dd3`
- Linux CI run 35331620111: **204/204 xUnit tests + 29/29 SimLab scenarios**, 0 warnings/errors.
- Windows CI run 35331620076: WinUI x64 Release build + live-probe build + **204/204 xUnit tests**, 0 warnings/errors.

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

## Historical calibration reference

FSEconomy is now available as a historical calibration input only.

- methodology: `docs/economy-historical-calibration.md`
- small representative aircraft sample: `data/calibration/fseconomy-aircraft-reference.csv`
- OpenCareer progression and economy rules remain authoritative
- no runtime API dependency is introduced
- historical values are used to constrain relative aircraft/cost/earning relationships before simulation
- broad scenario sweeps should follow focused calibration rather than discover baseline ratios from scratch

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
- ledger status,
- recent transactions,
- explicit settlement rules.

It is intentionally read-only. It does not allow the UI to type a balance or fabricate transactions.

## Important boundaries

Not yet implemented end-to-end:

- generated job offer -> final pay quote -> persisted `JobContract`,
- verified `FlightSession` fuel/fees/maintenance actuals -> settlement,
- career opening cash transaction,
- aircraft ownership transfer/purchase,
- persisted loan origination/payment schedule,
- insurance/storage/maintenance recurring settlement,
- deadhead travel settlement,
- company payroll,
- named commodity lots and shipment market value,
- income-history observation window for credit underwriting.

The market simulation can influence future quotes, but existing jobs must preserve accepted quoted compensation so market changes do not rewrite history.

## Next bounded economy work

1. Bind `ContractPayQuoteEngine` when offer drafts become validated `JobContract` records.
2. Define the immutable accepted economic snapshot stored with a contract.
3. Feed verified FlightSession actual fuel/airport/maintenance costs into `ContractSettlementCosts`.
4. Create/open the career with an explicit opening-balance ledger transaction.
5. Implement atomic aircraft purchase: consume listing once, debit cash/deposit, create loan if needed, post aircraft asset, transfer ownership and assign storage/delivery.
6. Add loan amortization/repayment and protected-absence behavior.
7. Add insurance, storage, maintenance and paid-deadhead transactions.
8. After the flight/session/dispatch loop is playable, implement the named commodity catalog from `docs/cargo-market-requirements.md`.
9. Run balance simulations/playtests against the 50–80-hour ownership target and tune distributions, not isolated examples.

## Product decisions that should be confirmed before deeper tuning

- Starting cash and whether the pilot begins with any debt.
- How game-accelerated employee pay should be relative to company/owner revenue.
- Whether player-owned companies can intentionally accept loss-leading contracts for reputation.
- Loan late-payment grace, repossession/default severity and recovery path.
- Insurance deductibles/coverage tiers.
- How aggressively recurring hangar/storage/maintenance costs should continue while the player is away.
- Whether taxes are omitted entirely, simplified, or modeled only for owned companies.
