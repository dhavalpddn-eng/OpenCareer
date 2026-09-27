# Explicit engine-1 actuator boundary

MBL-17 slice 10 adds simulator actuation infrastructure only. No career, maintenance,
reliability, flight-runtime, Jobs or UI caller invokes it. Schema remains 17.

## Official MSFS 2024 SDK evidence (checked 2026-09-26)

| Official page | Contract used |
| --- | --- |
| [Aircraft Miscellaneous Events](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/key-events/aircraft-miscellaneous-events/) | TOGGLE_ENGINE1_FAILURE toggles engine 1 failure; no event payload parameter. It is not an idempotent set operation. |
| [Aircraft Engine Variables](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimVars/Aircraft_SimVars/Aircraft_Engine_Variables.htm) | GENERAL ENG FAILED is the indexed engine failure flag, unit Bool; engine 1 is GENERAL ENG FAILED:1. |
| [SimConnect_MapClientEventToSimEvent](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Events_And_Data/SimConnect_MapClientEventToSimEvent.htm) | HRESULT result; HANDLE, client-event DWORD, ANSI event name. |
| [SimConnect_TransmitClientEvent](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/API_Reference/Events_And_Data/SimConnect_TransmitClientEvent.htm) | HRESULT result; HANDLE, object ID, event ID, data, group/priority and flags. The GroupID-is-priority flag interprets the group argument as priority; HRESULT is submission, not simulator application. |
| [SimConnect API Reference — priorities](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/SimConnect_API_Reference.htm) | Highest group priority is 1. Events may be masked before reaching the simulator, so readback is required. |
| [SimConnect SDK](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimConnect/SimConnect_SDK.htm) | Out-of-process clients are supported; native calls are not thread-safe. |

The internal native binding uses StdCall, a pointer-sized handle and 32-bit IDs/flags,
user object 0, payload 0, highest priority 1, and GROUPID_IS_PRIORITY (0x10).
Bool observations use the existing FLOAT64 numeric transport, independently of career
telemetry. Only valid 0/1/-1 values form evidence. Observation timestamps are UTC receipt
times; monotonic elapsed time controls freshness and acknowledgement deadlines.

## Authority and lifecycle

- `ISimulatorFailureActuator.EnsureEngineFailedAsync(1)` exposes intent only. Other
  indexes return UnsupportedEngine before any command. No generic toggle/handle/native
  event API is exposed to Application. Both actuator and read-source DI interfaces resolve
  to the existing singleton `SimConnectConnection`.
- After handshake the same worker maps the stable event, defines the dedicated SimVar
  and subscribes once per second. Optional setup HRESULT/entry-point/packet-correlated
  exceptions disable failure control without disrupting ordinary telemetry. Setup packet
  IDs remain available for late asynchronous rejection. Unrelated exceptions keep the
  existing reconnect behavior.
- One atomic slot covers queued and active work per connection. Concurrent callers get
  Busy. Admission and worker execution require a readback no older than 3 seconds.
  A true readback returns AlreadyFailed without transmitting. False permits one toggle.
- Applied requires a subsequent true readback within the 10-second acknowledgement
  window. Normal engine-running telemetry cannot acknowledge this command. A late true
  packet yields timeout for the original request and AlreadyFailed for an explicit retry.
- Timeout or cancellation after submission retains an unresolved-toggle guard. Even a
  fresh false packet does not prove the delayed toggle was rejected: explicit retry stays
  AcknowledgementTimeout until true readback resolves uncertainty. There is no resend,
  inverse toggle or automatically cleared failure. A correlated server exception returns
  CommandRejected; HRESULT rejection also returns CommandRejected.
- Cancellation stops the caller's wait immediately and drops queued work on the worker;
  it cannot undo a transmitted event. Disconnect/stop/disposal completes pending work
  SimulatorUnavailable. A new native connection gets a new empty actuator session,
  remaps/redefines and needs fresh readback; old requests are never carried or replayed.
- `AirframeFailureEligibilityService` remains independent: CanGenerateFailure=false and
  ComponentModelUnavailable after its maintenance gate. No RNG, probability, condition
  mutation, persistent failure event/history, schema change or automatic caller is added.

## Limits and verification

Deterministic tests exercise the real connection worker, native packet decoder, queue,
exception correlation and DI contract through test transport only. The SDK provides a
toggle, not compare-and-set; OpenCareer serializes its own requests but cannot make
another add-on's concurrent state changes atomic with its readback. Effects on the stock
C172, native runtime readback latency and aircraft/add-on compatibility remain LIVE-ONLY.
The probe is built, not executed against MSFS. Exact-head build/test evidence belongs to
draft PR #106; the frozen playable-loop branch and PR #105 remain untouched.
