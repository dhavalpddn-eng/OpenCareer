# Economy & Ownership core system

Status: **core backend implemented and automated tests passing** on `feature/economy-ownership`.

The high-level roadmap panel is considered complete when these five core systems exist. This does not mean every later UI, job-generator, company-expansion, MRO-parts or balance/playtest task is finished; those remain in the strict master tracker.

## Deterministic economy foundation — complete
Existing deterministic world/market simulation remains the offline-first authority for market pressure, capacity, events, replay/checkpoints and economy invariants.

## Named cargo markets — complete core
Implemented: data-driven named commodities (coffee, phones, TVs, fresh food, live plants), mass/volume/handling/risk data, deterministic origin/destination market snapshots, immutable lot value snapshots, and freight quotes separate from cargo value.
Downstream: bind manifests to FlightSession/jobs, condition evidence, market-driven job generation and atomic freight settlement.

## Credit model — complete core
Career-derived credit compares fictional lenders and evaluates APR, LTV, debt-service capacity, reserve, deposit and civilian ownership eligibility. Military-only assets remain excluded from civilian purchase.

## Dealers & ownership flow — complete core backend
SQLite persistence atomically coordinates career cash/reserve, dealer stock/offers, cash or financed purchase, stock consumption, ownership transfer, persistent loans/amortization schedules, insurance, storage occupancy/lease, initial maintenance state and idempotent purchase receipts. Failed transactions roll back together.
Downstream: polished UI, recurring loan payment/default/restructure, delivery/reposition logistics and live-career balance calibration.

## Maintenance systems — complete core backend
Implemented deterministic airframe/engine/gear wear, discrete damage, hard-landing/overspeed/engine-stress/excess-G inputs, inspection intervals, service cost/downtime, grounding thresholds, persistent maintenance state/history and idempotent service/usage operations. Conservative fallback profiles carry explicit confidence.
Downstream: verified live telemetry binding, simulator-native component state where supported, MRO/parts depth and maintenance UI.

## Verification
- 117/117 xUnit tests passed.
- 29/29 deterministic SimLab scenarios passed.
- Linux Release restore/build passed with warnings-as-errors.
- The first SQLite package selection failed NuGet security audit; the dependency was upgraded to Microsoft.Data.Sqlite 10.0.12 rather than suppressing the warning.
- Windows CI verified the WinUI Release build, live-probe build and **117/117 xUnit tests** after the Windows SQLite pooling cleanup fix. The high-level roadmap image may now mark all five Economy & Ownership core boxes complete.