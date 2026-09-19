# Economy contract / ownership integration

Updated: 2026-09-18.

Read `AGENTS.md` and `docs/economy-foundation.md` first. This is the compact handoff for the child branch that turns economy quotes into persistent contracts, ownership and liabilities.

## Branch

- Branch: `feature/economy-contract-integration`
- Parent: `feature/economy-ledger-settlement`
- The branch was synchronized with parent casual-play invariants through parent head `ccdff12352174c3d052a23ee9ec9a4fd6313fd88`.
- Latest code head before this documentation commit: `8fc2deacd8b73d127ac567fd8200815b5ed1071b`.
- Confirmed Linux baseline before the latest balance-regression commit: `82c6e624ddb66cc49c09e7f43152ee0525ee0a8e` passed **246/246 xUnit + 29/29 SimLab**, 0 build warnings/errors.
- Windows CI for the most recent commits must be checked before merging. Do not promote pending results to verified.

## Implemented here

### Offer -> immutable JobContract economics

`JobContractFactory` converts an actionable `JobMarketOfferDraft` into a validated `JobContract`.

The contract stores a `JobContractEconomicSnapshot` containing:
- quote version/time;
- estimated duration/distance/payload;
- demand, urgency, difficulty and relationship inputs;
- estimated player operating costs;
- the exact accepted `ContractPayQuote`.

The stored compensation must equal the stored economic snapshot. Later market changes or retuning cannot rewrite an already-created contract.

Locked dream previews cannot become contracts. The original offer expiry becomes the contract acceptance deadline.

### Explicit career opening balance

`CareerOpeningBalanceEngine` creates an auditable opening transaction.

- opening cash is never disguised as contract revenue;
- duplicate initialization is idempotent;
- conflicting reuse of the same career identity is rejected;
- persisted account ID `OpeningEquity = 13`.

The actual starting-cash amount remains a product decision.

### Atomic aircraft acquisition

`AircraftAcquisitionEngine` and `SqliteEconomyLedgerStore.AcquireAircraftAsync` support cash and financed purchases.

One SQLite transaction:
1. revalidates the authoritative ledger reserve;
2. posts the aircraft asset/cash/loan transaction;
3. consumes the dealer listing exactly once;
4. persists the ownership record;
5. persists the loan when financed;
6. initializes active-play billing state;
7. initializes loan repayment state.

Civilian purchase still rejects military-only/non-civilian-sale aircraft.

A stale UI/credit cash snapshot cannot spend money that no longer exists in the authoritative ledger.

### Loan amortization and repayment state

`AircraftLoanAmortization` creates deterministic cent-rounded installments.

For the regression fixture of $80,000 principal, 6% annual rate, 120 installments and $888.17 scheduled payment:
- first interest = $400.00;
- first principal = $488.17;
- final interest = $4.41;
- final principal = $882.83;
- final payment = $887.24;
- total interest = $26,579.47.

Those cent-rounded totals were independently rechecked with Wolfram using exact rational arithmetic.

`AircraftLoanRepaymentState` stores remaining principal. When active-play recurring billing contains principal:
- ledger posting,
- billing-cycle progress,
- and remaining loan principal

advance atomically in the same SQLite transaction.

Duplicate activity settlement cannot reduce principal twice.

### Parent casual-play invariants preserved

The child explicitly synchronized the latest parent rules:
- persisted ledger enum numbers are schema and must never be reordered;
- existing codes 0–12 stay unchanged;
- `OpeningEquity = 13`;
- early ordinary jobs prefer 1–3 hours;
- routine jobs over the normal 6-hour session ceiling are filtered, with bounded extended-mission exceptions;
- one recurring ownership cycle = 30 career-credit flight hours;
- offline/protected absence accrues no default ownership bill;
- pause/slew/non-credit time does not advance billing;
- simulation acceleration does not multiply billing;
- no minimum per-session recurring charge;
- idempotent recurring settlement retries look up the original transaction before recomputing against advanced state.

### Deadhead financial settlement

`DeadheadTravelSettlementEngine` supports:
- player-paid deadhead: debit `TravelExpense`, credit `Cash`;
- employer-duty deadhead: zero player cash movement but an auditable idempotent travel transaction.

`TravelExpense = 14` is the next persisted ledger account code.

Personal travel uses an atomic cash guard, so it cannot overdraw authoritative cash. Location movement/persistence remains a separate career-state integration step.

### Short-session progression balance gate

Regression coverage now proves the existing calibrated employee quote can reach the first cash-aircraft target without marathon flights.

Representative path:
- 22 normal 3-hour employee jobs;
- 1 normal 1-hour employee job;
- 67 career-credit flight hours total.

Under the current calibration that produces enough cash for the existing $60,000 used-light-aircraft fixture while retaining the $4,000 operating reserve.

This is a gameplay regression target, not a claim about real pilot wages or real aircraft prices.

## Still open

- verified `FlightSession` actual fuel/airport/maintenance costs -> `ContractSettlementCosts`;
- production persistence for accepted `JobContract` itself;
- deadhead financial settlement -> persisted `CareerLocation` movement;
- missed-payment / arrears / default / repossession recovery policy;
- insurance product tiers/deductibles;
- company payroll and recurring business overhead;
- named commodity lots/market snapshots after the playable flight loop;
- full distribution playtest, not just representative regression cases;
- UI for owned aircraft, loan balance and payment progress.

## Safety / data-integrity rules

- Do not change persisted `LedgerAccountCode` numbers without a schema migration.
- Do not let UI, AI narration or a quote mutate money.
- Do not use a cached affordability snapshot instead of the ledger at purchase.
- Do not consume a dealer listing twice.
- Do not let a retry reduce loan principal twice.
- Do not charge protected real-world absence under the default policy.
- Do not make 15-hour flights economically superior to normal short sessions merely because they are longer.

## Next bounded work

1. Verify both Linux and Windows CI at the latest child head.
2. Persist accepted `JobContract` records so restart cannot lose the immutable pay snapshot.
3. Add an application adapter from the future verified `FlightSession` cost summary into contract settlement.
4. Bind owned-aircraft/loan summaries into Finances/Fleet UI.
5. Add configurable delinquency/arrears state only after product severity/grace rules are decided.
6. Create a draft PR into `feature/economy-ledger-settlement`; do not auto-merge.
