# Current work review

Reviewed 2026-09-17 against branch snapshot `803e224989cc42a7c64cac0af35ba6c91ab698f7`. All 48 tracked local files matched that remote snapshot before changes. This is a source/test/design review, not live Windows/MSFS certification or a gameplay study.

## Conclusion

The architecture and documented decisions are coherent. Simulator-independent rules exist, while the application, telemetry integration and transactional persistence remain unfinished. Chapter 2 is still next. Further finance feature expansion should wait for a playable foundation.

## Verification and correction

- Fresh baseline: 34 xUnit tests and 29 deterministic SimLab scenarios passed.
- Corrected dealer acceptance validation to revalidate current stock and reject a sale price inconsistent with the recorded discount/list price. Added regression coverage; 35 xUnit tests pass after the correction.
- This consistency check does not authenticate an offer. Purchase orchestration must still load authoritative dealer/stock/offer records and settle once in SQLite.
- Existing replay, event-scope, contract-lifecycle, financial-health, location and military-access regressions remain green.
- Context7 returned official Windows App SDK guidance supporting .NET 10 WinUI templates. Windows launch verification remains required; Linux tests cannot prove it. [Official template reference](https://github.com/microsoft/windowsappsdk/blob/main/dev/Templates/Dotnet/README.md).

## What makes sense, and what remains unproven

| Area | Finding | Remaining gate |
|---|---|---|
| Architecture | WinUI + isolated SimConnect + pure domain + SQLite responsibilities agree | Implement application and persistent flight loop |
| F-22 / KRME | Military assignment remains distinct from civilian sales; airport retains mixed opportunities | Detect exact add-on and test its telemetry; prove military objectives |
| Economy | Deterministic simulation and temporary-shock handling are tested | Connect wages, inflation, actual job supply and settlement |
| 50–80 hours | Both illustrative acquisition budgets equal $64,000; $1,000 net/hour gives 64 hours | No measured career-income evidence yet; not a demonstrated balance outcome |
| Larger aircraft financing | Deposit savings do not imply loan affordability | Underwrite verified operating net income, costs and debt |
| Credit | Score and affordability are separate; lender terms differ | Define recent-history windows, default rehabilitation and income estimation |
| Dealers | Distinct filters/markups, reproducible promotions and expiry | Persist inventory and issued offers; prevent double purchase and remote teleport |
| Absence | Default quote creates no offline liabilities | Personal deadline, passive income and repayment clock integration |
| Ground rewards | Bounded quote per supplied event collection | Verified manual evidence and one ledger settlement per flight |
| Engagement | Clear progression and airport variety are design goals | Playtesting needed; automated tests cannot establish fun |
| Efficiency | Headless checks run quickly; no new runtime dependencies added by review | No MSFS CPU/memory/frame-time measurements exist |

Wolfram independently checked an illustrative $200,000 loan at 7.5% over 180 months: payment about $1,854.02 before the game's upward cent rounding ($1,854.03). At a 35% debt-service limit and zero existing debt, that rounded payment needs at least $5,297.23 monthly operating net income. These are scenario calculations, not a real loan offer. The current 64-hour savings example alone does not establish that income.

Recovery design issue to settle before persistence: current credit inputs are cumulative counters. Old missed payments can retain a lasting score penalty. Define a recent-history/rehabilitation policy before calling bankruptcy recovery complete. Do not silently erase history or punish protected absence.

## Image retry and connected services

Kling's required CLI 0.2.0 installed and ran. Identity lookup reported no login. The official login flow requires browser OAuth with a localhost callback; authorization has not completed in this headless environment. No image generation was submitted. Preserve the existing [SVG concept](ui-concept.md); it is not a generated Kling result.

No Notion callable tools were exposed; no Notion page was read or changed. Repository Markdown remains authoritative. Data Analytics' scope check found no gameplay dataset for statistical balance/engagement claims; this review uses source, tests and explicit mathematical scenarios, not invented player metrics.

## Next stage

Chapter 2: create the Windows WinUI 3 shell with NavigationView, connection status and an empty Current Flight view. Launch without MSFS, show Waiting/Disconnected, and build/verify on Windows. Then implement SimConnect, normalized telemetry, robust flight detection and durable session recovery before connecting job payouts or aircraft purchases.

## Questions awaiting user decisions

1. Which F-22 add-on/developer and version is installed?
2. Should the military career begin as an already-qualified pilot or with required training sorties?
3. Can one pilot combine military/reserve duty and civilian work in the same save, or should switching careers require a formal transition?
4. Should reconnaissance/escort success emphasize route/altitude/timing, formation/proximity, or both?
5. Does the preferred 1–3-hour session include preparation, taxi and shutdown, or refer to airborne time only?
6. After repaying overdue debt, how quickly should credit recover: several successful jobs, a longer clean-payment history, or a negotiated restructuring path?
7. Will the companion usually run on a second monitor, or in a narrow window alongside the simulator?

These decisions refine later implementation. They do not block building the disconnected application shell.
