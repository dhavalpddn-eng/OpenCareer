# MSFS 2024 SDK strategy for OpenCareer

This document records the verified development boundary between the external OpenCareer application and any Microsoft Flight Simulator 2024 in-sim package we may need later.

## Source-of-truth order

1. Current official MSFS 2024 SDK documentation queried through Context7.
2. The offline documentation installed with the user's exact MSFS 2024 SDK version, when provided to the project as a reference snapshot.
3. SDK samples installed with MSFS 2024.
4. Community references only when official material does not answer the question.

A separate Custom GPT is not required for this project. The existing OpenCareer project already has persistent project context, GitHub, Context7, Wolfram and web access. If an offline SDK documentation snapshot is supplied later, it should be treated as version-specific evidence rather than replacing the current official documentation.

## External application remains the primary architecture

OpenCareer is primarily an out-of-process .NET application using SimConnect.

Reasons:

- The current MSFS 2024 SDK recommends out-of-process applications for stability and ease of testing/debugging.
- Managed C#/.NET is supported out of process.
- If OpenCareer crashes, it should not normally crash the simulator.
- The economy, persistence, market simulation and UI do not belong in the simulator process.
- SimConnect is not thread-safe, so the adapter will own a single serialized SimConnect boundary and publish immutable telemetry snapshots to the rest of OpenCareer.

The in-sim package, if needed, should stay deliberately tiny.

## What the in-sim bridge is for

The bridge is only justified for capabilities that cannot be reliably performed from the external SimConnect client, especially direct EFB/Coherent route interaction.

Potential responsibilities:

- receive a planned route from OpenCareer over a lightweight local IPC channel,
- send the route to the MSFS 2024 EFB using the supported Coherent planned-route APIs,
- optionally synchronize the EFB route to avionics where supported,
- report route/EFB acknowledgement back to OpenCareer.

The bridge must not contain the economy, persistent save, company simulation, world events, market logic or heavy UI.

## Package authoring

Use the MSFS 2024 Project Editor / Package Tool as the authoritative package build path.

Recommended source layout follows the SDK model:

```text
OpenCareer.MSFS/
  PackageDefinitions/
  PackageSources/
  Sources/
```

The built package output contains `manifest.json`, `layout.json` and compiled/copied package assets.

### Do not make a custom layout generator the primary build system

`layout.json` is part of a compiled package and lists packaged files. The MSFS Package Tool is the authoritative build automation system and should remain responsible for building package output.

A future OpenCareer package utility may **validate** built `manifest.json` / `layout.json`, check expected files, compare package versions and produce diagnostics. It should not replace the Package Tool unless an actual SDK limitation forces us to.

This avoids duplicating Microsoft build behavior and reduces maintenance whenever the SDK changes.

## Modular SimObjects

Modular SimObjects are important when creating or modifying MSFS 2024 aircraft and other simulated objects. They are not a prerequisite for the external OpenCareer career/economy application.

OpenCareer should support modular aircraft as simulator content through:

- SimConnect telemetry and aircraft enumeration,
- capability profiling,
- installed-aircraft registry,
- MSFS facility/performance/configuration data where available.

We should not create or edit Modular SimObjects simply to track flights, missions or the economy.

If OpenCareer later ships custom aircraft, vehicles, mission-specific objects or another real SimObject, then that work gets its own content package and follows the SimObject Editor/project structure required by the SDK.

## Installed aircraft policy

OpenCareer must not rely on a small hard-coded supported-aircraft list.

The registry will identify installed/flyable aircraft and build a capability profile from simulator data plus OpenCareer reference data. Mission generation operates against capability requirements.

Examples:

- C172 -> training, ferry, survey, sightseeing, light delivery.
- C208 -> cargo, passenger, medical, remote logistics.
- Air Tractor -> agriculture and firefighting.
- CJ4 -> charter, government courier, urgent medical transport.
- F-22 -> military training, alert, intercept, escort and patrol when military access is valid.
- C-130 -> military logistics, relief, medevac and austere transport.

Government-only aircraft access remains distinct from civilian ownership.

## SDK knowledge workflow

For every MSFS-specific implementation task:

1. Query Context7 against `/websites/flightsimulator_msfs2024_html` for the current official API/configuration documentation.
2. Compare against the user's offline SDK snapshot if one is available and the installed SDK version matters.
3. Check the matching official SDK sample where appropriate.
4. Record any version-sensitive assumption in the source code or architecture notes.
5. Add a compile/simulation/integration test where the API can be exercised automatically.

Do not copy broad chunks of SDK documentation into the repository. Store concise decisions and links/identifiers instead.

## Automation boundary

Automate:

- installed-aircraft discovery and capability classification,
- package-output validation,
- schema/config sanity checks,
- SimConnect ID/code generation if repetition becomes substantial,
- deterministic economy tests,
- route/runway/weight/fuel validation,
- CI builds.

Leave authoritative generation to MSFS when the SDK already owns that process:

- package compilation,
- asset conversion,
- SimObject structure creation through SimObject Editor,
- Package Tool output generation.

## Performance boundary

The external process owns all expensive work. The in-sim bridge remains event-driven and almost idle during normal flight.

SimConnect telemetry is adaptively sampled and moved through a bounded in-memory handoff. Economy simulation can sleep during active flight and catch up deterministically after shutdown.

The design rule remains: **never calculate during the flight what can safely be calculated before departure or after shutdown.**
