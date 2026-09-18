# Conflict / airfield-control review

Updated 2026-09-17.

## Review outcome

The first bundle had a good architecture boundary but was **not ready to merge unchanged**.

### Fixed in v2

1. **Compile blocker — type-name collision**
   - `OpenCareer.Application.Conflict.ConflictBaselineSource` collided with the existing
     `OpenCareer.Domain.Events.ConflictBaselineSource` enum.
   - The application metadata record is now `ConflictBaselineSourceMetadata`.
   - The domain enum reference is explicitly qualified.

2. **Incorrect source-date assumption**
   - The first model rejected a dataset when `PublishedAt > BaselineAsOf`.
   - Monthly/retrospective datasets are often published *after* the period they describe.
   - v2 separates `DataThrough` from nullable `PublishedAt`.
   - `DataThrough` must not exceed the baseline snapshot. Publication may occur later.

3. **ACLED licensing risk**
   - v2 marks ACLED `restricted-research-only` unless a separate license review approves use.

4. **Airfield degradation was too one-way**
   - Active conflict applied damage every day but no background repair/maintenance.
   - A 5,000-run calibration of the illustrative moderate scenario
     (`severity=.8`, `damagePressure=.3`, `resilience=.75`, 180 days) produced an old-model
     median runway serviceability of ~0.136 and services ~0.049, with ~10.5% of runs entering
     a runway-closed state.
   - v2 adds a small resilience/severity-dependent repair floor during active conflict.
   - The same calibration yields median runway ~0.347, services ~0.207 and no runway closures
     in that illustrative moderate scenario. Severe high-damage scenarios can still close an airport.

5. **Boolean eligibility expressions made explicit**
   - Status-pattern conditions are parenthesized before runway/service thresholds.

## New work added after the review

- `AirportConflictRegionIndex`: exact ICAO mapping first, country fallback second, source retained.
- `AirfieldOperationGuard`: civilian passenger/cargo, government, humanitarian, military logistics and reconnaissance.
- Contested airports may support reconnaissance without implying a landing.
- Secured but degraded airports can accept military logistics while still rejecting civilian passenger service.

## Still blocking production merge

- Full Windows/Linux CI on the integrated branch.
- Reviewed UCDP transformation pipeline and first real baseline pack.
- Production airport/country mapping.
- Main-save persistence for conflict + airfield state.
- Job destination filtering through `AirfieldOperationGuard`.
- Conflict/airfield transition emission into the persistent social feed.
