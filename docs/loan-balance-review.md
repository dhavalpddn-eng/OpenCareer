# Independent loan balance review

Updated: 2026-09-19.

Review branch: `feature/economy-loan-balance-review`.

Source implementation reviewed: `feature/economy-contract-integration` at `9409d2a48b71d63eae9d225e37146bffa1e4b949`.

This branch does not change the loan implementation. It adds independent regression coverage and records balance findings for the sidework branch to consume or cherry-pick.

## Result

The current amortization and active-play repayment architecture is internally consistent for the tested cases.

Strong points already present:

- deterministic cent-rounded amortization;
- principal and interest are separated;
- principal reduction is persisted atomically with recurring-cost ledger settlement;
- duplicate activity settlement does not reduce principal twice;
- recurring ownership costs advance on career-credit flight time rather than wall-clock absence;
- one long session and many short sessions converge to the same 30-active-hour billing-cycle cost;
- ordinary career jobs are not required to be marathon flights.

## Casual-player regression gate

New review tests cover:

- thirty 1-hour flights;
- ten 3-hour flights;
- five 6-hour flights;
- two 15-hour flights.

All four shapes must produce the same first-cycle loan + insurance + storage cost.

The representative starter financing fixture uses the current qualified-career history, Community Aviation Credit, a $100,000 aircraft, $20,000 deposit and $80,000 financed principal.

Current result:

- credit score: **785**;
- rate: **6.7090909%**;
- scheduled installment: **$916.92**;
- insurance + storage fixture: **$670 / 30 active hours**;
- routine maintenance reserve fixture: **$575 / 50 active hours**;
- combined representative ownership burden: **$64.40 per active hour**;
- this is about **6.44%** of the current $1,000/hour progression center.

The review gate allows at most 8% for this representative starter-financing fixture.

## Independent amortization check

Wolfram independently reproduced the current Community-loan fixture using the production credit score/rate:

- principal: **$80,000**;
- annual rate: **6.7090909%**;
- 120 installments;
- scheduled installment: **$916.92**;
- total principal: **$80,000.00**;
- total interest: **$30,030.06**;
- total payments: **$110,030.06**;
- final adjusted payment: **$916.58**.

The review test hard-codes these values so an accidental math change is visible.

## Session-frequency / credit check

At the same 60 career-credit hours and otherwise comparable history:

- short-session example: 60 completed jobs / 2 failures -> score **755**;
- longer-session example: 20 completed jobs / 1 failure -> score **741**;
- Community rate difference: about **0.153 percentage points**;
- $80,000 / 120-installment payment difference: about **$6.32**.

That is small enough that one-hour players do not receive a dominant financing advantage merely from completing more jobs. A regression guard now limits this representative difference.

## Important underwriting finding

The largest remaining casual-player risk is **not amortization**. It is the meaning of:

- `VerifiedMonthlyNetIncome`;
- `ExistingMonthlyDebtPayments`.

The repayment system is intentionally based on **30 active career-credit hours**, but the credit API still names and accepts monthly income/debt values. If production integration later derives those values from a literal wall-clock calendar month, a player who only flies occasionally can be denied financing simply because they were away from the game.

Example with the current starter fixture:

- payment: **$916.92**;
- Community debt-service cap: **30%**;
- existing debt fixture: **$100**;
- minimum income required by the present formula: about **$3,389.73**.

If that input means real-calendar monthly game earnings, a casual player can fail underwriting despite having exactly the same active-flight earning power as a frequent player.

### Required integration rule

Before production underwriting is connected, income and debt-service capacity should be normalized to the same gameplay clock as repayment.

Preferred direction:

- derive affordability from a trailing active-career window, such as equivalent income per 30 career-credit hours; or
- explicitly convert settled active-career earnings/debt into a 30-active-hour equivalent before calling `CareerCredit.Evaluate`.

Do not derive loan affordability from elapsed real-world calendar time under the default protected-absence design.

A review test intentionally demonstrates this sensitivity so the integration boundary cannot be overlooked.

## Larger-loan stress reference

Using current representative rates and the same $670 fixed insurance/storage plus $575-per-50h maintenance reserve:

| Scenario | Payment / cycle | Approx. ownership burden / active hour | Income required by current DSR input |
| --- | ---: | ---: | ---: |
| $80k Community, score 785, 120 | $916.92 | $64.40/h | $3,389.73 |
| $200k Commercial, score 785, 180 | $1,584.91 | $86.66/h | $5,242.60 |
| $400k Commercial, score 785, 180 | $3,169.81 | $139.49/h | $9,770.89 |
| $800k Commercial, score 785, 180 | $6,339.62 | $245.15/h | $18,827.49 |
| $100k Specialist, illustrative score 650, 120 | $1,569.11 | $86.14/h | $6,676.44 |

These are gameplay stress references, not real-world loan or aircraft-cost claims.

Do not compare high-end aircraft against the starter $1,000/hour income center as a final balance verdict. Higher aircraft classes need their own revenue/cost bands. The table is only intended to expose scaling and underwriting pressure.

## Recommended sidework actions

1. Keep the current deterministic amortization and atomic principal-repayment implementation.
2. Preserve the 30-active-hour recurring-cost clock and zero default offline catch-up liability.
3. Define the production source for normalized affordability income before locking lender balance.
4. Keep ordinary one- to three-hour work economically viable; do not use long flights as the path to better loan terms.
5. Add high-aircraft-class revenue bands before tuning Commercial Fleet Finance against expensive aircraft.
6. Cherry-pick the review tests after the current loan branch stabilizes, rather than editing the same finance files concurrently.
