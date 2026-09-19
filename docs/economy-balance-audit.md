# Economy balance and exploit audit

Status: verified deterministic core on `feature/economy-ownership`.  
Verified implementation/CI head: `b097d79fb5161ed9d6e5e1556c32c66387aa9195`.

This audit protects the economy foundation from obvious optimal-strategy exploits before the playable job/settlement loop exists. It does **not** certify the final game economy; fuel, recurring loan/insurance/storage execution, MRO/labor, live job supply and end-to-end settlement still need integration and playtesting.

## Core payout envelope

The career progression center remains **$1,000 net player savings per career-credit hour**.

Normal fresh repeatable jobs are bounded to **0.85–1.15x** that center after mission-family/context adjustments:

- fresh minimum: $850 net / career-credit hour,
- balance center: $1,000,
- fresh maximum: $1,150.

Direct operating costs are added separately to recommended gross revenue. They are not player profit.

## Repetition protection

A candidate job derives repeat exposure from the **last 8 completed contracts**, not UI state:

- same mission family: 4% penalty per recent match, counted to 5,
- same route corridor: 2% per recent match, counted to 5; A→B and B→A are the same corridor,
- same market: 1% per recent match, counted to 5, case-insensitive,
- total repetition penalty capped at **30%**.

Related variants share families. Cargo, Express Cargo and AOG Parts Delivery are all `CargoLogistics`, so changing the label does not reset the farm counter.

Regression coverage proves:
- six identical 10-minute Express Cargo jobs pay less than 90% of one fresh 1-hour Express Cargo job,
- the best single mission-kind spam over 64 hours earns less than a diversified neutral rotation,
- fresh mission types cannot exceed the configured hourly envelope.

## Total player-net ceiling

All stackable player money is capped by **actual career-credit time**, not quoted mission duration:

`max_player_net = $1,000 × actual_career_credit_hours × 1.15`

This means:
- completing a quoted long mission abnormally fast cannot turn it into a several-thousand-dollar-per-hour exploit,
- manual/mission/other monetary bonuses must pass through the same settlement ceiling,
- direct operating-cost reimbursement remains separate and cannot be counted as player profit.

## Time-acceleration protection

Simulation rate is a convenience, not a money multiplier.

At settlement:

`profit_factor = min(1, career_credit_time / simulated_movement_time)`

Only player profit is scaled. Valid direct operating-cost reimbursement remains intact.

Examples:
- 1x: profit factor 1.00,
- 4x: profit factor 0.25,
- 0.5x: profit factor 1.00.

## Ownership pacing

Current examples include:
- $250 initial storage,
- $420 standard-hull first-period insurance,
- operating reserve,
- current dealer markup/discount limits.

Neutral cash and financed calibration: **64.67 hours**.

Independent Wolfram verification of conservative boundary cases:
- fastest current used-aircraft cash path with maximum Fleet Broker promotion and maximum fresh payout: **50.89 hours**,
- slowest no-promotion fresh cash path at the fresh minimum: **76.08 hours**,
- fastest current larger financed path with maximum Fleet Broker promotion and maximum fresh payout: **51.78 hours**,
- slowest no-promotion fresh financed path at the fresh minimum: **76.08 hours**.

Therefore the current deterministic fresh-job envelope and dealer bounds preserve the intended **50–80 real career-hour** acquisition window even under current best/worst fresh conditions.

## Other exploit protections

### High-value cargo

Cargo value is not transport pay. Declared-value/security surcharge is capped at **25% of transport pay**. A same-payload smartphone load may pay more than coffee because of security risk, but cannot scale linearly with the much larger commodity value.

### Credit farming

Completed-job reliability evidence is capped to **two counted completions per verified real career-credit hour**. Spamming many tiny missions cannot independently manufacture a high credit score.

### Manual ground rewards

Manual procedure reward is capped at:
- $35 per flight,
- $35 per career-credit hour,
- one deduplicated reward per procedure kind in the quote.

### Used-aircraft maintenance reset

Purchased condition creates an irreducible baseline wear floor. Routine service removes recoverable wear/damage but does not turn a 60%-condition or 82%-condition used aircraft into new condition.

## Adversarial BalanceLab

`tools/OpenCareer.BalanceLab` now runs in both Linux and Windows CI and fails the build if core economy guardrails break.

Current results:
- adversarial max-context family rotation: **$73,600 / 64 h = $1,150/h**,
- fastest cash acquisition under that strategy: **hour 51**,
- fastest financed acquisition: **hour 52**,
- best single mission-kind spam: **$59,570 / 64 h = $930.78/h**,
- 10-minute same-route Express Cargo spam: **$51,674.60 / 64 h = $807.42/h**,
- 1,000 greedy synthetic careers, six offers/hour: 64-hour net p05 **$68,665.35**, median **$69,046.24**, p95 **$69,398.51**,
- synthetic ownership timing: p05 **54 h**, median **55 h**, p95 **55 h**.

The synthetic Monte Carlo distribution is a stress fixture, not a claim about final live job availability.

## Strong / ordinary / struggling progression calibration

BalanceLab now includes three explicit deterministic 80-hour career profiles. They are calibration fixtures for payout/repetition behavior, not claims about final live job availability.

- **Strong:** diversified higher-demand work, **$71,676.06 at 64 h = $1,119.94/h**, first current cash-ownership threshold at **hour 53**.
- **Ordinary:** representative mixed work with moderate context/repetition, **$63,774.70 at 64 h = $996.48/h**, ownership at **hour 59**.
- **Struggling:** lower-demand work with more repeated families/routes/markets, **$54,214.14 at 64 h = $847.10/h**, ownership at **hour 70**.

The gates require strong progression to land in 50–60 hours, ordinary in 55–70, struggling in 65–80, while preserving descending hourly earnings and the global player-net ceiling. This keeps legitimate weaker play recoverable without making optimized play skip the intended ownership window.

## Ownership carrying-cost stress preview

BalanceLab now also projects current light-aircraft carrying costs from existing domain values without claiming those recurring costs are settled in the playable career yet.

Representative fixture:
- standard hull insurance: **$420/month**,
- light storage: **$250/month**,
- cash ownership fixed carrying cost: **$670/month**,
- representative 10-year Community Aviation Credit payment on a $40,000 principal: **$455.79/month**,
- financed fixed carrying cost: **$1,125.79/month**,
- normal 50-hour fallback maintenance on an 82%-condition used light aircraft with 50 ordinary landings: **$575**,
- current minimum operating reserve used by the first-aircraft progression policy: **$4,000**.

Calibration gates require the cash fixed cost to stay at or below 25% of reserve, financed fixed cost at or below 35%, normal 50-hour maintenance at or below 20%, and one financed billing cycle plus that maintenance event at or below 50%. Current values pass all four gates. Wolfram independently reproduces the representative loan payment at approximately **$455.78** before cent rounding and the maintenance quote at **$575**.

This is deliberately a **projection stress test**, not the unchecked end-to-end settled-cost gate. Actual recurring loan/insurance/storage billing, fuel/service settlement and active-play accrual must exist before that stricter checklist item can be marked complete.

## Verification

Verification state:
- Existing Linux branch audit baseline: **132/132 xUnit + 29/29 SimLab + BalanceLab PASS**.
- Latest Windows PR integration run **35411495605** at implementation head `bf81eee` against current `feature/m1-simulation-core`: **138/138 xUnit**, **BalanceLab PASS**, WinUI Release build passed and live-probe build passed.
- The Windows BalanceLab result reproduced the progression figures above, the carrying-cost preview, and all prior adversarial gates.

## Still open before final economy certification

The following are not yet playable/settled and therefore cannot be fully exploit-tested:

- authoritative market-driven job generation,
- atomic mission payout/reputation/location settlement,
- fuel/service/MRO/labor cost integration,
- recurring loan/insurance/storage billing,
- loan repayment/default/restructuring execution,
- cargo condition and final freight settlement,
- persistent active-play billing accrual,
- long-absence settlement,
- 1-, 3- and 6-hour representative career playtests.

Future recurring obligations must preserve already-accrued **active-play** cost across restarts so closing the app cannot reset a bill. Protected offline absence must also remain non-punitive and must not create passive profit.
