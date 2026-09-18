# Economy balance and exploit audit

Status: verified deterministic core on `feature/economy-ownership`.  
Verified code head: `a1fb6b397d5cee59831049a810365ad86fd2fa2e`.

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
- same route: 2% per recent match, counted to 5,
- same market: 1% per recent match, counted to 5,
- total repetition penalty capped at **30%**.

Related variants share families. Cargo, Express Cargo and AOG Parts Delivery are all `CargoLogistics`, so changing the label does not reset the farm counter.

Regression coverage proves:
- six identical 10-minute Express Cargo jobs pay less than 90% of one fresh 1-hour Express Cargo job,
- the best single mission-kind spam over 64 hours earns less than a diversified neutral rotation,
- fresh mission types cannot exceed the configured hourly envelope.

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

## Verification

Final audit head passed:
- **129/129 xUnit** on Linux,
- **129/129 xUnit** on Windows,
- **29/29 SimLab** deterministic scenarios,
- Windows WinUI Release build,
- Windows live-probe build.

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
