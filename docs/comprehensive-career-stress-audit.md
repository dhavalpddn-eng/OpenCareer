# Comprehensive 12,000-career stress audit

Updated: 2026-09-19.

This audit is isolated on `feature/economy-loan-balance-review`. It does not modify the sidework production branch.

## Coverage

The first tuned batch contains **6,000 deterministic long-term careers**:

- 12 career paths;
- 5 pilot skill/learning profiles;
- 5 session-length/playstyle profiles;
- 20 deterministic seeds per combination;
- **1,200 active career-credit flight hours per career**.

A second independent batch uses a different seed offset and repeats another **6,000 careers**. Final coverage is therefore **12,000 long-term careers / 14.4 million active flight hours**.

Career paths:

1. general civilian employee;
2. airline employee only;
3. airline long-haul specialist;
4. military-only career;
5. military warzone/conflict chaser;
6. military logistics specialist;
7. solo owner-operator;
8. charter business owner;
9. cargo business owner;
10. medevac business owner;
11. flight-school business owner;
12. regional-airline business owner.

Skill profiles range from an absolute beginner through slow learner, average, strong and expert.

Session styles cover short, balanced, long, marathon and weekend-mixed patterns.

## Systems exercised

The stress harness uses current domain logic for:

- beginner/adaptive flight guidance;
- civilian employee compensation;
- extended-duty/marathon pay;
- employer trust;
- airline regional/narrowbody/widebody access;
- military operation requirements and authorization;
- actual military capability gating;
- deterministic simulated combat resolution;
- military service compensation;
- civilian independent-contract compensation;
- company-contract revenue and operating-cost recovery;
- aircraft ownership maintenance/repair reserve behavior.

The following business pieces remain **review-only simulation assumptions**, because the full company-management subsystem is not implemented yet:

- startup-capital thresholds;
- business fixed overhead;
- fleet operating cost bands;
- owner draw policy;
- business insolvency/recovery trigger.

They are stress parameters, not claims that the production company system is already complete.

## Business profiles exercised

| Business | Startup target | Reserve | Variable operating cost | Fixed cost / 30 active h |
| --- | ---: | ---: | ---: | ---: |
| Flight school | 120h / $70k capital | $20k | $180/h | $6k |
| Charter | 150h / $100k capital | $30k | $320/h | $9k |
| Cargo | 180h / $140k capital | $40k | $450/h | $12k |
| Medevac | 220h / $180k capital | $60k | $600/h | $15k |
| Regional airline | 450h / $500k capital | $180k | $2,500/h | $45k |

All company revenue is generated through the current `ServiceTrack.CompanyContract` quote model, then the review harness subtracts the modeled company costs. Personal cash and company cash are kept separate.

## Airline-only path gates

A player who never wants to own a business or aircraft can stay an employee for the full 1,200-hour simulation.

The stress gates require:

- no personal aircraft operating-cost burden;
- airline-only average long-term wealth cannot fall more than 10% behind ordinary civilian employment;
- regional, narrowbody and widebody access still require hours + qualifications + employer trust;
- widebody must remain reachable by the skill-adjusted long-term window, with slower beginners allowed more time than strong/expert pilots.

## Military-only / warzone path

Military-only careers are forbidden from silently switching to civilian employment to rescue the economy.

The harness uses the real military requirement catalog and aircraft capability/access checks.

The warzone-chaser path deliberately seeks fast-jet escort/intercept/air-support work once qualified and uses `SimulatedCombatEngine` with high threat exposure.

Acceptance gates require:

- no civilian fallback jobs;
- valid military qualification/capability access;
- military compensation remains viable without player-paid military aircraft costs;
- high-threat specialists receive substantial actual high-threat **flight time**, not merely a large count of tiny missions;
- warzone pay stays bounded relative to normal military service so danger does not become an exploitative money printer.

## First 6,000 run: tuning findings

The first broad run exposed two important session-shape problems.

### 1. Beginner penalties were being measured across the entire 1,200-hour career

The stress metric counted every later-career unsafe consequence as a 'beginner' penalty. This made short-session absolute beginners appear permanently over-punished even after they had learned.

**Fix:** beginner punitive/retry gates are now evaluated only during the actual learning phase (first ~25 completions / 50 hours). Later real mistakes still matter; they just are not misclassified as onboarding harshness.

### 2. Military fast-jet progression was accidentally mission-count biased

Service trust originally increased mainly per completed mission. A player flying short sorties accumulated trust much faster than a marathon military player with the same active service hours.

That delayed high-threat access for some long-session warzone chasers and recreated the same raw-job-count bias previously found in civilian ownership.

**Fixes:**

- service trust gain is now proportional to successful active military flight time in the review model;
- safe-abort trust impact is proportional to flight time;
- warzone participation is measured by **high-threat flight hours**, not number of missions;
- fighter/non-fighter state is recomputed correctly after a fallback training duty, preventing a fallback mission from being mislabeled as a high-threat fighter sortie.

After those changes, the original 6,000-scenario batch passed all defined career/balance gates.

## Fresh independent validation

A second batch of **6,000 careers** uses a separate deterministic seed range.

Final Linux validation at `adfd412`:

- **306/306 xUnit passed**;
- both 6,000-career facts passed;
- deterministic SimLab passed;
- combined comprehensive stress coverage: **12,000 careers / 14.4 million active flight hours**.

## Balance gates used

Examples of enforced invariants:

- employee and military careers cannot end with negative personal cash;
- beginner learning cannot remain excessively punitive during onboarding;
- airline-only career remains economically viable and can reach widebody endgame;
- military-only paths never require civilian work;
- warzone specialists accumulate meaningful high-threat time without runaway pay;
- solo ownership remains reachable and retains long-term positive value;
- all five business types must start reliably under the modeled path;
- normal/strong/expert business owners cannot have material bankruptcy rates;
- no business can repeatedly bankrupt without tripping a failure;
- company value cannot collapse far below its starting capital under ordinary operation;
- session length itself cannot create a hidden progression advantage/disadvantage.

## Production boundary

The audit is deliberately broad, but not every tested system is production-complete.

Most important remaining implementation boundaries:

- full persistent company model (company bank account, fleet, employees, leases, payroll, routes, bases, P&L);
- authoritative aircraft wear/components/repairs;
- persistent licenses/type ratings;
- live generated military campaigns and settlement;
- real airline employment/roster/scheduling;
- real-world playtesting for fun, pacing and clarity.

The stress harness is intended to keep those future systems inside the same balance envelope as they are implemented rather than allowing a later feature to accidentally break another career path.