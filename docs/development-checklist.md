# OpenCareer development checklist

Updated: 2026-09-17.

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

## Verification

- Linux CI: 152/152 xUnit + 29/29 SimLab
- Windows CI: WinUI x64 Release + live probe compile + 152/152 xUnit
- Live native MSFS behavior: not yet verified
