# Twenty-career balance review

Updated: 2026-09-19.

This is an isolated balance harness on `feature/economy-loan-balance-review`. It does not modify the production sidework branch.

## What is authoritative versus provisional

The scenario runner uses the current domain code for:

- career level calculation;
- career credit score;
- lender underwriting and amortized payment;
- employee and independent-contract pay;
- extended-duty pay premium;
- protected offline absence;
- employer-covered maintenance behavior.

Two pieces are intentionally **review-only** because production systems do not exist yet:

1. **Aircraft qualification progression.** Current project architecture explicitly says level is meta-progression and must not replace licenses/ratings/qualifications. The review therefore records real career level but uses a simple qualification-count readiness ladder only to stress the economy. Production still needs the planned qualification/rating service.
2. **Aircraft wear/repair economics.** Dealer stock has condition percent and contract settlement has maintenance-reserve cost, but there is no authoritative aircraft wear, component-health or repair engine yet. The review uses a provisional repair envelope to discover where future repair costs would break progression.

Career level is therefore shown in every scenario but is **not** treated as a substitute for aircraft qualification.

## Provisional repair envelope

These are gameplay stress values, not real-world aircraft maintenance claims.

| Aircraft class | Qualifications used by review | Routine maintenance / h | Repair reserve / h | Base major repair | Operating reserve |
| --- | ---: | ---: | ---: | ---: | ---: |
| Basic piston | 1 | $11.50 | $8 | $3,000 | $4,000 |
| Advanced piston/twin | 2 | $30 | $25 | $10,000 | $14,000 |
| Turboprop | 3 | $70 | $55 | $18,000 | $25,000 |
| Light jet | 4 | $140 | $110 | $40,000 | $60,000 |
| Midsize/regional | 5 | $260 | $210 | $90,000 | $140,000 |
| Heavy transport | 6 | $500 | $420 | $220,000 | $350,000 |

Condition below 80 raises routine maintenance. Condition below 85 raises unscheduled-repair reserve and major-repair exposure. The model deliberately makes neglected used aircraft meaningfully riskier without making ordinary 80–90% condition aircraft unusable.

For owner-operator scenarios, routine maintenance + repair reserve are passed through the existing independent-contract operating-cost recovery model. The player still bears loan, insurance/storage and any repair shock above the reserved amount. Employee and military assignment aircraft keep maintenance with the employer/service, matching current contract compensation rules.

## Twenty scenarios

| # | Career shape | Hours | Level | Credit | Aircraft/access being tested | Result |
| ---: | --- | ---: | ---: | ---: | --- | --- |
| 1 | New weekend learner | 10 | 5 | 607 | Basic piston; 0/1 qualification | **Blocked as intended** — not qualified |
| 2 | Casual learner | 30 | 8 | 661 | Basic piston; 1/1 qualification | **Needs tuning** — ordinary financing can already make the first aircraft reachable before the 50h lower target |
| 3 | Careful saver before first plane | 55 | 11 | 706 | Basic piston | **Balanced** — financing works inside the 50–80h window |
| 4 | First cash-aircraft pilot | 67 | 11 | 719 | Basic piston | **Balanced** — $68k supports the $60k cash aircraft plus $4k reserve |
| 5 | Marathon-focused early pilot | 60 | 10 | 673 | Basic piston | **Balanced** — higher marathon earnings help, but ownership still lands inside target window |
| 6 | Sloppy used-piston owner | 70 | 11 | 611 | Basic piston at 65% condition | **Needs tuning** — projected major repair $5.4k exceeds the $4k starter reserve |
| 7 | Returning piston owner after one year away | 70 | 12 | 747 | Basic piston at 80% condition | **Balanced** — projected major repair $3.6k fits $4k reserve; protected absence adds $0 catch-up bill |
| 8 | Trusted twin-qualified employee | 80 | 13 | 779 | Advanced piston/twin | **Balanced** — qualified employee access; employer carries maintenance |
| 9 | Financed twin owner | 100 | 14 | 777 | Advanced piston/twin at 82% | **Balanced** — payment about $1,591.75 per active billing cycle; projected owner net about $977/h; $11.2k repair shock fits $14k reserve |
| 10 | Cash-rich but unqualified twin buyer | 35 | 9 | 669 | Advanced piston/twin; only 1/2 qualifications | **Blocked as intended** — cash does not bypass qualifications |
| 11 | Experienced pilot with poor credit | 120 | 15 | 609 | Advanced piston/twin | **Blocked as intended** — Commercial lender standing fails after repeated misses/debt |
| 12 | Turboprop-qualified employee | 150 | 17 | 793 | Turboprop | **Balanced** — access can progress without forcing ownership |
| 13 | Turboprop owner-operator | 170 | 18 | 798 | Turboprop at 85% | **Balanced** — payment about $4,091.97/cycle; owner net about $989/h; $18k repair shock fits $25k reserve |
| 14 | Marathon turboprop owner | 180 | 18 | 775 | Turboprop at 80% | **Balanced** — marathon premium remains valuable; owner net about $1,063/h; $21.6k repair shock fits reserve |
| 15 | Charter pilot not yet jet-qualified | 200 | 20 | 793 | Light jet; only 3/4 qualifications | **Blocked as intended** — high level/cash does not substitute for rating progression |
| 16 | Light-jet-qualified employee | 240 | 22 | 808 | Light jet | **Balanced** — qualified employer-aircraft path remains viable without ownership grind |
| 17 | Financed light-jet owner | 260 | 22 | 811 | Light jet at 85% | **Balanced** — payment about $9,376.83/cycle; owner net about $893/h; $40k repair shock fits $60k reserve |
| 18 | Regional-jet airline employee | 420 | 28 | 818 | Regional jet | **Balanced** — employer-aircraft path opens airline flying without requiring ownership |
| 19 | Narrowbody airline captain | 520 | 31 | 824 | 737/A320-class narrowbody | **Balanced** — endgame airline progression continues through employer qualifications/trust, not personal wealth |
| 20 | Widebody endgame airline captain | 750 | 37 | 833 | Widebody airliner | **Balanced** — long-haul/widebody endgame is reachable as an airline employee without buying the airplane |

## Balance findings

**14/20** scenarios are directly balanced under the current/provisional rules.

**4/20** are deliberately blocked and behave correctly:
- new pilot without the required qualification;
- cash-rich pilot missing twin qualification;
- experienced pilot with poor credit;
- charter pilot not yet jet-qualified.

**2/20** expose tuning problems:

### 1. Starter financing can arrive too early

The 30-hour casual scenario has level 8, credit 661, one qualification and enough deposit/reserve cash. The current Community underwriting can approve the $60,000 starter-aircraft financing path.

That does not violate a hard hour lock—there intentionally is no XP/hour lock—but it does undercut the declared **50–80 real flying hour** first-ownership pacing for an ordinary player.

Production should solve this through the finance/income-history design, qualification progression or starter-loan product terms, not by making career level a magic aircraft gate.

### 2. Low-condition starter aircraft can outrun the starter reserve

At 65% condition the provisional repair model produces:

- routine + repair reserve: about **$31.81/h**;
- representative major repair exposure: **$5,400**;
- starter operating reserve: **$4,000**.

This is the desired warning signal: a cheap worn aircraft should not be equivalent to an 85–100% condition aircraft. The eventual repair system needs either condition-adjusted required reserve, stronger inspection disclosure, insurance/repair coverage, or a lower purchase price large enough to compensate.

## Higher-aircraft repair scaling

The matrix supports a useful progression rule:

- employee/military aircraft let players unlock larger aircraft **before** they can personally afford to own them;
- owning a plane introduces repair/maintenance exposure;
- independent-contract operating-cost recovery can cover normal maintenance reserves;
- major repair shocks are paid from the aircraft-class operating reserve;
- poor-condition aircraft increase both hourly reserve and shock size;
- no repair costs accrue merely because the player was away from the game.

This prevents the economy from requiring ownership of every aircraft class just to experience that class.

## Remaining production gaps exposed by the matrix

1. Explicit licenses/ratings/qualifications are still a backlog item. `QualificationsVerified` is only a boundary boolean today.
2. No authoritative wear/component/repair state exists yet. Condition currently lives on dealer stock; flight settlement only accepts a maintenance-reserve amount.
3. Starter financing still needs a pacing guard that preserves the 50–80h target without turning career level into an artificial license.
4. Midsize/heavy personal ownership needs dedicated company/high-tier revenue economics. Current employee access is healthy, but generic individual financing is not intended to make an $4–8M aircraft trivial.
5. Repair reserve should eventually be calculated from the actual owned aircraft, condition, use, incidents and component state rather than a static class table.


## Explicit airline tiers

The review now distinguishes **regional jet**, **narrowbody airliner** and **widebody airliner** instead of lumping all airline flying into a generic heavy-aircraft tier. See `docs/airline-career-path.md` for the employer-aircraft progression and company-system boundary.
