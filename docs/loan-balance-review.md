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

## Loan-term clock semantics

The production types still use names such as `TermMonths`, `ScheduledMonthlyPayment`, `VerifiedMonthlyNetIncome` and `ExistingMonthlyDebtPayments`, while actual repayment advances one installment over each **30 active career-credit hours**.

That creates a semantic trap even if the math is correct.

For example, a 120-"month" loan currently represents **120 active billing cycles = 3,600 career-credit flight hours**. It is therefore not a literal ten-year wall-clock loan under the protected-absence design.

Before Fleet/Finances UI is built, choose one vocabulary and keep it consistent:

- rename gameplay-facing concepts to **billing cycles / active-cycle payment / active-cycle income**, or
- explicitly label them as a simulated month whose clock advances only through active career-credit flying.

Do not display a literal calendar due date or "10 years remaining" unless the repayment engine is changed to a real-calendar model. Real-calendar repayment would conflict with the current no-punishment-for-being-away rule.

## Payoff-horizon balance warning

The 30-active-hour billing clock makes the current real-world-style term counts extremely long in gameplay time.

At the current mapping:

- 24 installments = **720 active flight hours**;
- 36 installments = **1,080 active flight hours**;
- 60 installments = **1,800 active flight hours**;
- 120 installments = **3,600 active flight hours**;
- 180 installments = **5,400 active flight hours**.

For the representative $80,000 Community loan at 6.7090909%, Wolfram gives:

| Term | Scheduled installment | Approx. ownership burden / active hour | Active hours to scheduled payoff |
| --- | ---: | ---: | ---: |
| 24 cycles | $3,571.27 | $152.88/h | 720 h |
| 36 cycles | $2,459.55 | $115.82/h | 1,080 h |
| 60 cycles | $1,573.14 | $86.27/h | 1,800 h |
| 120 cycles | $916.92 | $64.40/h | 3,600 h |

The current 120-cycle starter fixture is easy to carry but may effectively never be paid off in a normal career. This is not a math defect; it is a game-time-scale decision.

Before locking lender terms, treat **term length** and **active-hours-per-billing-cycle** as gameplay tuning variables rather than copying real-world 120/180-month labels directly. Preserve proportional accrual so casual players remain fair regardless of session length.

## Progression bypass risk

The current progression policy explicitly targets first ownership in the **50–80 career-credit-hour** range for both:

- the $60,000 cash light-aircraft path plus $4,000 reserve;
- the $250,000 larger-aircraft financed path with 20% down plus $14,000 reserve.

The acquisition/credit engine itself does not enforce that policy. A cheaper financed aircraft can therefore bypass the intended progression window if it is surfaced by inventory.

Illustrative current-engine case:

- aircraft price: **$100,000**;
- deposit: **$20,000**;
- operating reserve: **$4,000**;
- savings pace: **$1,000 per career-credit hour**;
- cash gate is met at about **24 hours**;
- an illustrative 24-hour / eight-job / no-failure history with safety 90 and trust 70 evaluates to about **658 credit**, already above the Community lender minimum of 600.

That means a financeable $100,000 listing can become mechanically reachable around 24 hours if normalized income also passes underwriting. This does not break the loan math, but it can break the intended **50–80 hour first-aircraft progression**.

### Required integration rule

Do not treat every civilian dealer listing that passes generic underwriting as an equally valid first-aircraft path.

Before finance is exposed in the playable dealer UI, bind one of these progression controls:

- aircraft-class / career-tier finance eligibility;
- minimum verified career standing for financed ownership;
- tier-specific operating reserve / deposit targets;
- or inventory gating that prevents cheaper financed aircraft from bypassing the intended ownership window.

The existing `CareerProgressionPolicy.FinancedLargerAircraft` already encodes the intended 64-hour cash requirement; the acquisition path needs to consume an equivalent rule instead of leaving it as a calibration-only policy.

## Marathon-flight reward rule

Long flights should remain **optional, high-total-value work**, not a punishment and not a prerequisite.

The review now models an explicit **extended-duty premium** rather than keeping marathon hourly pay flat.

Current review tuning:

- premium starts after **6 flight hours**;
- it ramps linearly;
- full premium is reached at **12 hours**;
- maximum duration-only premium is **25%**;
- aircraft class, route type, seniority/reputation and mission difficulty can still add separate premiums later.

Under equal moderate context, the reviewed fixture becomes:

- 1-hour mission: **$977.29 total / $977.29 per hour**;
- 15-hour mission: **$18,049.53 total / $1,203.30 per hour**.

That is about **18.5x the total payout** and about **23.1% higher hourly pay** than the 1-hour reference. The regression test now requires at least a 20% hourly premium for the 15-hour fixture.

Design rule going forward:

- marathon missions should pay **much more total cash** because the player committed much more active time;
- marathon duty should also pay a materially higher hourly rate;
- duration premium should remain bounded so it does not become the only efficient progression path;
- long missions must not become the only efficient path to progression;
- one- to three-hour players should retain competitive hourly progression;
- experienced players should still have access to occasional long-haul cargo/passenger/ferry work rather than having every non-special mission above six hours hard-filtered forever.

The current board policy still gives zero duration suitability to ordinary non-special jobs above six hours. That is intentionally flagged for later tuning: preserve the early-career short-job preference, but allow rare optional long-haul work at higher career standing.

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


## Independent 70-hour test-career run

A deterministic review-only career scenario now runs against the actual C# domain code in CI. It does not modify the sidework production branch.

Career shape:
- 20 × 2-hour employee cargo jobs at 260 NM;
- 5 × 3-hour employee cargo jobs at 400 NM;
- 1 × optional 15-hour ferry marathon at 1,500 NM;
- **70 career-credit hours**, **26 completed jobs**.

Moderate common quote context:
- demand 1.00;
- urgency 0.15;
- difficulty 0.15;
- relationship 0.20;
- employee track, so fuel/maintenance/airport costs are employer-covered for this phase.

Exact C# quote results:
- 2h job: **$1,969.23**;
- 3h job: **$2,961.17**;
- 15h marathon: **$18,049.53**;
- marathon extended-duty multiplier: **1.25**.

Career outcome:
- total wages after 70 active hours: **$72,239.98**;
- average: **$1,032.00 per active hour**;
- first point at which the $60,000 cash aircraft + $4,000 reserve target is satisfied: **70 hours**;
- all-short control of 35 × 2h jobs reaches the same cash target at **66 hours**;
- both remain inside the declared **50–80 hour** first-ownership window;
- a full year of protected real-world absence produces **$0** catch-up fixed liability.

Representative financing check at 70 hours:
- one failure, safety 93, trust 85, no prior payment history;
- active-cycle-equivalent income: **$30,959.99 per 30 active hours**;
- credit score: **722**;
- $100,000 aircraft / $20,000 deposit / $80,000 Community principal;
- loan approved;
- scheduled installment: **$945.30**;
- first-cycle loan + insurance + storage: **$1,615.30**;
- adding the current $575/50h maintenance reserve gives about **$65.34 ownership burden per active hour**;
- that is about **6.33%** of this test career's $1,032/hour employee earning rate.

Independent amortization cross-check for that same loan:
- annual rate: **7.3963636%**;
- first interest: **$493.09**;
- first principal: **$452.21**;
- final opening principal: **$938.23**;
- final interest: **$5.78**;
- final adjusted payment: **$944.01**.

Linux CI at review head `40f3737` passed **266/266 xUnit** plus SimLab after this scenario was added. The scenario's exact cent values therefore match the production C# rounding behavior, including the 15-hour wage of **$18,049.53**.
