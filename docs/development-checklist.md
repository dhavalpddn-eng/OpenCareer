# OpenCareer development checklist

Updated: 2026-09-18.

This is the text source of truth for the visual roadmap. A green check means the bounded foundation item is implemented and verified; it does not imply the whole dependent chapter is complete.

## Military / Government

- ✅ Conflict UI design — Complete
- ✅ Simulated combat architecture — Complete (foundation)
- ✅ Air support mission flow — Complete (foundation)
- ✅ Threat simulation — Complete (foundation)
- ✅ Military operations screen — Complete (foundation)

### Military integration still open

- ⬜ Career military authorization/qualification persistence
- ⬜ Job offer -> MilitaryOperationPlan conversion
- ⬜ Telemetry -> MilitaryOperationCoordinator runtime
- ⬜ Active mission SQLite recovery
- ⬜ Conflict/airfield destination eligibility wiring
- ⬜ World-feed military/conflict transition binding
- ⬜ Current Flight military strip
- ⬜ Operational map live binding
- ⬜ One-time mission settlement/reputation
- 🔴 Live MSFS validation remains a shared-PC gate


## Economy / Finance

- ✅ Deterministic regional/segment market simulation — Complete (foundation)
- ✅ Cargo/passenger route-demand pressure — Complete (foundation)
- ✅ Career-derived credit underwriting — Complete (foundation)
- ✅ Fictional dealer offers / deterministic discounts — Complete (foundation)
- ✅ Market-aware contract pay quote engine — Complete (foundation)
- ✅ Balanced authoritative economy ledger — Complete
- ✅ SQLite ledger persistence — Complete
- ✅ Exactly-once completed-contract settlement — Complete
- ✅ Concurrent duplicate-settlement protection — Complete
- ✅ Finances ledger screen — Complete (foundation)
- ✅ Active-play recurring ownership-cost proration — Complete (foundation)
- ✅ SQLite active billing-cycle progress + exactly-once recurring ledger posting — Complete (foundation)
- ✅ Casual-session balance guards: 1–3h preferred, regular jobs capped at 6h, no offline catch-up bills — Complete (foundation)

### Economy integration still open

- ⬜ Job offer -> final JobContract pay-quote binding
- ⬜ Verified FlightSession actual costs -> settlement binding
- ⬜ Career starting balance / opening transaction
- ⬜ Aircraft cash purchase atomic ownership transfer
- ⬜ Loan origination, amortization and repayment persistence
- ⬜ Ownership loan/insurance/storage schedule binding into active-play settlement and loan-state advancement
- ⬜ Verified maintenance/service liability binding into authoritative settlement
- ⬜ Paid personal deadhead and employer-duty deadhead settlement
- ⬜ Company payroll and recurring operating costs
- ⬜ Named commodity catalog / manifests / commodity market snapshots
- ⬜ Finance/market balance playtest across the 50–80-hour first-aircraft target

### Economy verification

- Linux branch baseline: **204/204 xUnit + 29/29 SimLab**
- Windows PR integration run **35417940644**: WinUI x64 Release + live probe compile + **221/221 xUnit**
- Current pay figures are gameplay calibration, not claims about real-world pilot compensation or current aircraft prices.

## Verification

- Linux CI: 152/152 xUnit + 29/29 SimLab
- Windows CI: WinUI x64 Release + live probe compile + 152/152 xUnit
- Live native MSFS behavior: not yet verified
