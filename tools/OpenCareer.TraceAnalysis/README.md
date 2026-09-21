# Live-probe trace analysis

Offline diagnostics for the JSONL written by `OpenCareer.LiveProbe`. Runs with .NET 10 on Windows or Linux; no MSFS installation, native DLL, cloud service or extra package is required. Reads the input without modifying it and prints a JSON report.

From the repository root:

```powershell
dotnet build tools/OpenCareer.TraceAnalysis/OpenCareer.TraceAnalysis.csproj -c Release
dotnet run --project tools/OpenCareer.TraceAnalysis/OpenCareer.TraceAnalysis.csproj -c Release --no-build -- "C:\Temp\opencareer-live.jsonl"
```

Redirect the second command's output to a different path to retain the report, for example `> "C:\Temp\opencareer-analysis.json"`. Never redirect over the input trace.

The report includes:

- Connection-state/issue counts, connected periods and explicit telemetry-clear records.
- Observed pause/resume, slew and ground-contact flags. Ground-contact changes are raw observations, **not takeoff/landing counts**.
- Minimum, mean and maximum positive snapshot intervals while both adjacent samples are unpaused, out of slew and connected. Disconnects, clearing, invalid records, sequence gaps and backward clocks break continuity; repeated sample timestamps are counted separately. All sample timestamps are wall-clock receipt times, not simulator elapsed time.
- Numeric field minima/maxima with the production boundary's declared units. Coordinate, heading and percentage ranges are checked; aircraft performance thresholds are not inferred. Missing/non-finite fields invalidate the sample rather than becoming zero.
- Line-numbered findings for malformed records, incomplete captures, missing connection/telemetry evidence, clock/sequence problems and inconsistent final clearing/counts. At most 100 findings are retained, with the full count reported. Parsing continues after a malformed line.

| Exit code | Meaning |
| --- | --- |
| `0` | Single complete capture with connection/telemetry evidence and no detected structural findings |
| `2` | Review findings or incomplete evidence; the JSON report is still emitted |
| `64` | Incorrect command arguments |
| `1` | File access or I/O failure |

An intentional simulator-absence/restart test can produce connection-issue findings. Review them against the actions performed; they are not automatically implementation defects. A sample observed outside Connected may also reflect the probe's separate connection/telemetry reads.

**Exit 0 is not live acceptance.** This tool cannot prove the file came from MSFS, identify the aircraft/airport, verify SDK units against instruments, distinguish a pause from an unobserved stall, or establish taxi/takeoff/landing. It does not require every manual scenario to appear before returning 0. Probe polling and pause-event publications can change the observed snapshot cadence; it is not a measurement of native packet frequency. Compare the actual F-22/KRME run with the [live acceptance checklist](../../docs/simulator-connection.md#remaining-live-acceptance-gate).

Tests use explicitly synthetic captures. Raw speed/height/hysteresis calibration and the live runtime gate remain open until the real trace is reviewed. No flight state, career hours, save data or settlement is changed by this analyzer.
