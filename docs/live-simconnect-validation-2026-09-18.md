# Live SimConnect trace review — 2026-09-18

Source: user-run `OpenCareer.LiveProbe` JSONL on Windows x64 with MSFS 2024.

## Connection

- Initial connection: 05:35:33Z.
- Simulator identity: `SunRise`; application `12.2.282174.999`; SimConnect `12.2.0.0`.
- At 05:46:01Z a response timeout moved the connection to reconnecting and cleared telemetry.
- The simulator connection recovered at 05:47:28Z.
- At 06:48:03Z the final simulator loss again moved to reconnecting and cleared telemetry.

This proves stale telemetry clearing and at least one successful reconnect. The trace ends while retrying, so final clean probe shutdown is not proven by this file.

## Sampling

- 4,280 telemetry records.
- No sequence gaps.
- Median steady-state interval ~1.000 s.
- p95 steady-state interval ~1.013 s.
- One long sampling gap corresponds to the reconnect window rather than stale replay.

## Flight evidence

Multiple real airborne segments were observed. The final full test segment began around sequence 3833 and landed at sequence 4136.

Final segment highlights:
- takeoff transition: ~269 KIAS / 271 kt GS, AGL ~12 ft;
- max AGL ~14,306 ft;
- max IAS ~521 kt;
- max GS ~523 kt;
- touchdown transition: ~119 KIAS / 128 kt GS, AGL ~6.6 ft;
- parking brake became set after rollout;
- engines later transitioned from 2 running to 0.

The touchdown sample reported about -2,013 fpm and 4.62 G. Because sampling is only 1 Hz, those values are evidence of a hard/fast contact window, not an authoritative landing-rate score.

## Mapping findings

Passed in this trace:
- latitude/longitude;
- MSL/AGL altitude;
- IAS and ground speed;
- vertical speed sign/range;
- heading;
- pitch/bank/G;
- on-ground;
- parking brake;
- engines-running count;
- fuel;
- payload stability;
- flaps movement;
- pause;
- slew false while not using slew.

Open issue:
- `GearDown` remained false for all 4,280 samples, including parked/on-ground periods. Do not use the current single `GEAR TOTAL PCT EXTENDED` mapping as authoritative for gear-based mission/scoring rules until a fallback source is live-tested.

## Flight-state implications

The trace validates the existing design decision to use an evidence processor plus state reducer rather than one raw boolean:
- loading produced `OnGround=false` while IAS/GS were zero at placeholder position near 0/90;
- one landing produced a brief recontact/bounce-style on-ground/off-ground sequence;
- real takeoff and landing transitions had strong multi-signal evidence.

Do not freeze universal IAS/AGL thresholds from this fast-jet trace alone. Add at least one slower GA calibration flight before claiming generic detector tuning.

Trace SHA-256: `955fa7b5a68ffbdafdf59f72bc174e02c1c6f42846d5b059a08b3977df5d3e7a`.
