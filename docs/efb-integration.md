# MSFS 2024 EFB integration

Status: accepted architecture direction. The Windows application remains authoritative; the EFB is an optional in-simulator surface.

## Decision

OpenCareer should support two complementary user interfaces:

1. **Windows WinUI 3 app** — authoritative career/session/economy engine, SQLite persistence, SimConnect connection and full management UI.
2. **MSFS 2024 EFB app** — thin in-simulator companion for preflight checklist, current mission, flight state, arrival requirements and recovery status.

Do **not** attempt to embed the WinUI process itself inside MSFS.

Microsoft Flight Simulator 2024 officially supports custom EFB applications. The SDK EFB template is JavaScript/TypeScript/JSX based and is packaged into the simulator. The simulator also provides a Communication API that can send named JSON CommBus messages between JavaScript and SimConnect clients.

Official SDK references:

- https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/EFB/Electronic_Flight_Bag_API.htm
- https://docs.flightsimulator.com/msfs2024/retail/samples-tutorials/samples/efb/efb-template-sample/
- https://docs.flightsimulator.com/msfs2024/retail/programming-apis/javascript/communication-api/
- https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/communication/communication-api/

## Supported architecture

```text
MSFS 2024 EFB app (TypeScript/JSX)
    -> CommBus JSON request/event
    -> OpenCareer.SimConnect CommBus adapter
    -> OpenCareer.Application use case
    -> authoritative Domain + SQLite
    -> immutable EFB projection
    -> CommBus JSON response/event
    -> EFB view
```

The EFB never owns balances, mission completion, flight hours, aircraft ownership, insurance entitlement, scoring or settlement.

## First EFB workflow

### Preflight

Default view before departure:

- mission/flight title,
- current airport and aircraft,
- contextual checklist,
- passenger/cargo readiness,
- fuel/payload summary,
- dispatch warnings,
- primary action/status.

Normal career jobs assume gate/parking cold-and-dark. Mission profiles may explicitly authorize runway starts for emergency/military scenarios.

### Flight

Once airborne:

- current flight phase,
- next mission objective,
- compact route/ETA,
- fuel summary,
- connection/recovery state,
- important abnormal alerts,
- quick access back to checklist.

Do not reproduce the full desktop dashboard in the EFB.

### Arrival

During approach/taxi-in:

- destination/runway when confidently known,
- mission-specific arrival requirements,
- parking/terminal target,
- shutdown/servicing checklist,
- passenger/cargo handoff status.

### Recovery

If the Windows app/SimConnect connection drops:

- show the last known authoritative snapshot as stale,
- show reconnect/suspended state,
- never invent progress locally,
- refresh from Windows once continuity is re-established.

## CommBus contract

Use a versioned JSON envelope, for example:

```json
{
  "schemaVersion": 1,
  "type": "OpenCareer.GetCurrentFlight",
  "correlationId": "opaque-id",
  "payload": {}
}
```

Responses/events should contain a UI projection rather than raw domain objects.

Suggested initial messages:

- `OpenCareer.GetCurrentFlight`
- `OpenCareer.CurrentFlightChanged`
- `OpenCareer.GetChecklist`
- `OpenCareer.ChecklistAction`
- `OpenCareer.GetMission`
- `OpenCareer.RequestRecovery`

Every command is revalidated in the Windows application. The EFB is not trusted merely because it runs inside MSFS.

## Offline-first rule

No Supabase, Vercel or OpenAI dependency is required for the EFB bridge.

The EFB-to-Windows path uses simulator-supported local communication. Optional cloud/AI features may enrich narrative later but must not be required to start, fly, recover or settle a mission.

## Packaging boundary

Do not add the Node/EFB package to the production solution until the C# flight/session contract is stable enough to expose a small versioned projection API.

When implementation starts, create a separate EFB package/tooling area rather than mixing TypeScript into the WinUI or Domain projects.

## Acceptance gate

Before calling the EFB integration complete:

1. build the official SDK EFB template,
2. load it through MSFS Developer Mode,
3. prove a JS -> CommBus -> C# SimConnect message,
4. prove a C# -> CommBus -> EFB response/event,
5. disconnect/reconnect the Windows app without stale EFB authority,
6. verify the app is usable from the 2D EFB panel and aircraft with a modeled EFB where supported.
