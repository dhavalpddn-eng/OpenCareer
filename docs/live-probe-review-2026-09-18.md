# Live probe review — 2026-09-18

Source capture: `opencareer-live(1).jsonl` (kept outside Git because it is a 3.7 MB runtime artifact).

## Verified evidence

- 4,939 JSONL records: one session start, 14 connection records, 4,921 telemetry records and three telemetry-clear records.
- Simulator identity reported `SunRise`, application `12.2.282174.999`, SimConnect `12.2.0.0`.
- Connection progressed from simulator-absent waiting to Connected.
- Three connected periods were observed, with telemetry clearing on loss and later reconnect.
- Telemetry sequence 1 through 4,921 was continuous with no gaps.
- Active, contiguous snapshot cadence had a 0.994 second mean and 1.000 second median.
- Pause/resume, taxi, airborne, approach and ground-contact changes were present.
- The capture included KRME-area operation plus later aircraft/location loads. Placeholder loading data near latitude 0 / longitude 90 was also visible and must not be treated as flight evidence.

## Concrete discrepancy and correction

`gearDown` was false in every telemetry record, including parked samples. The mapping accepted only a 0–100 percentage threshold while the live runtime produced the fully-extended value on the 0–1 scale. Commit `2accedb67d60c0864a0529e32ced56955556c9ec` accepts both observed 0–1 and documented 0–100 percentage representations and adds boundary regression tests.

## Gate still open

- The file has no `sessionEnd`; it ends while reconnecting after `ConnectionLost` / `ConnectionFailed`.
- A short rerun must verify corrected gear-down behavior and stop the probe cleanly.
- Aircraft identity/title/type is not part of the telemetry boundary yet.
- Instrument-by-instrument sign/unit comparison is not fully documented.
- IAS/AGL/takeoff/landing hysteresis thresholds remain intentionally unfrozen.
- Loading placeholders and aircraft/location discontinuities must be rejected or reset by the future `FlightEvidenceProcessor`.
