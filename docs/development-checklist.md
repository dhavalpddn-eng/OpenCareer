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

### Economy integration

- ✅ Job offer -> final JobContract pay-quote binding + immutable accepted economic snapshot — Complete (foundation)
- ⬜ Verified FlightSession actual costs -> settlement binding
- ✅ Career opening-balance ledger transaction — Complete (foundation; starting amount remains a product decision)
- ✅ Aircraft cash/financed purchase atomic ownership transfer + one-time dealer listing consumption — Complete (foundation)
- ✅ Loan origination + deterministic amortization + persisted remaining principal — Complete (foundation)
- ✅ Ownership loan schedule -> active-play settlement + atomic loan-state advancement — Complete (foundation)
- ⬜ Verified maintenance/service liability binding into authoritative settlement
- ✅ Paid personal deadhead and employer-duty deadhead financial settlement — Complete (foundation)
- ⬜ Deadhead settlement -> persisted career-location movement
- ⬜ Company payroll and recurring operating costs
- ⬜ Named commodity catalog / manifests / commodity market snapshots
- ✅ Short-session progression regression: first cash aircraft is reachable with 1–3h work inside the 50–80h target window — Complete (calibration foundation)
- ⬜ Full finance/market distribution playtest across the 50–80-hour target

### Economy verification

- Linux branch baseline: **204/204 xUnit + 29/29 SimLab**
- Windows PR integration run **35417940644**: WinUI x64 Release + live probe compile + **221/221 xUnit**
- Current pay figures are gameplay calibration, not claims about real-world pilot compensation or current aircraft prices.
- Child integration branch: `feature/economy-contract-integration`; see `docs/economy-contract-integration.md` for verified branch-specific counts and remaining gates.

## Verification

- Linux CI: 152/152 xUnit + 29/29 SimLab
- Windows CI: WinUI x64 Release + live probe compile + 152/152 xUnit
- Live native MSFS behavior: not yet verified
