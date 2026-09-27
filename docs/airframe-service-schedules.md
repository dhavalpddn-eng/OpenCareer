# Airframe service schedules

## Authority and scope

OpenCareer continues to pin the versioned `LightAircraftRoutineInspectionV1` gameplay
fallback when a physical airframe is created. Verified component rules are separate catalog
candidates until physical configuration and component service-baseline evidence exist.
Canonical aircraft identity or simulator TITLE alone cannot activate a component schedule.

| Candidate | Version | Required applicability evidence | Component | Documented interval | Runtime status |
| --- | ---: | --- | --- | --- | --- |
| `Cessna172S.LycomingIo360L2A.EngineInspection` | 1 | Exact AirframeId; certified Model 172S; installed IO-360-L2A; component usage baseline | Engine | 50 operating hours | Unavailable until all evidence is explicit |

The broad canonical `msfs-title:Cessna 172 Skyhawk` identity can expose this candidate but
cannot prove its applicability. When explicit evidence is supplied, due assessment compares
exact persisted tracked airborne ticks with the retained component baseline. This mapping is
an OpenCareer proxy; manufacturer documentation does not establish equivalence between the
game's airborne counter and an engine operating-time meter. Persisted landing cycles are also
shown factually, but do not trigger this task because no landing-cycle interval was verified.

Existing and new production airframes therefore remain on the generic 50-hour fallback.
Existing inspection exact-once behavior, dispatch gates and history remain unchanged.
Applicability evidence is not yet persisted; the production maintenance snapshot exposes this
candidate as `ApplicabilityEvidenceRequired`. `TrackedProxyCurrent`/`TrackedProxyDue` is available only to an explicit
caller that supplies matching physical configuration and component-baseline evidence.

## Primary-source ledger

| Organization | Exact source | Revision/date | Fact used |
| --- | --- | --- | --- |
| Federal Aviation Administration | [Type Certificate Data Sheet No. 3A12](https://drs.faa.gov/browse/excelExternalWindow/DRSDOCID153801600120250627164731.0001) | Revision 88, 2025-06-27 | The approved Model 172S configuration identifies the Lycoming IO-360-L2A engine. |
| Lycoming Engines | [O-360, HO-360, IO-360, AIO-360, HIO-360 & TIO-360 Operator's Manual, publication 60297-12 / revision 60297-12-5](https://www.lycoming.com/sites/default/files/file/2025-11/60297-12%20-%20O-360%2C%20HO-360%2C%20IO-360%2C%20AIO-360%2C%20HIO-360%2C%20and%20TIO-360%20Series.pdf) | December 2009 revision | Section 4 specifies the recurring 50-operating-hour engine inspection for the IO-360 series. |

## Deliberate exclusions

- No landing-cycle task is active. [McCauley Service Bulletin SB240F, Revision 6](https://mccauley.txtav.com/-/media/mccauley/files/bulletins/sb240f.pdf)
  uses **takeoff cycles**, plus operator, serial and maintenance-history applicability evidence.
  A retained landing episode is not silently treated as a takeoff cycle.
- No propeller overhaul rule is active. The current model lacks verified installed-component
  identity, component hours, first-installation date and last-overhaul baseline needed to
  evaluate the hour/calendar limits safely.
- Initial 25-hour inspection applicability is not inferred. Prior component life and service
  history are not reconstructed from the time the airframe was registered in OpenCareer.
- There are no costs, downtime, parts, UI, automatic failures, wear thresholds, active
  aircraft-specific dispatch gates or schema changes in this slice.
