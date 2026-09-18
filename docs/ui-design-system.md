# OpenCareer UI design system

Status: approved visual direction and implementation reference. This document defines the shared shell, visual tokens, layout rules, adaptive behavior and interaction rules for the production WinUI 3 application.

The current executable remains intentionally smaller than this target. Do not display fabricated balances, jobs, aircraft ownership, war state or mission progress merely to match the mockups. Populate a surface only when the corresponding application/domain data exists.

## Product character

OpenCareer should look like a professional aviation operations terminal: dense enough to be useful, calm enough to leave MSFS as the visual focus, and clearly separate ordinary operations from elevated warning/conflict states.

The approved baseline is:

- dark navy/slate shell,
- restrained aviation blue for normal emphasis,
- green for healthy/confirmed state,
- amber for caution,
- red only for dangerous/conflict/failure state,
- thin borders,
- compact information cards,
- readable typography rather than decorative type,
- persistent simulator connection state,
- left NavigationView shell,
- map/route panels only where they carry operational value.

## Canonical production stack

- WinUI 3 / Windows App SDK.
- Left `NavigationView` shell.
- `Frame` navigation.
- MVVM for screen state and commands.
- Standard WinUI controls where possible so text scaling, keyboard navigation and accessibility remain intact.
- `VisualStateManager`, adaptive grids and pane collapse for width changes.
- Do not put SimConnect, persistence or game calculations in Views.

Microsoft's current Windows app guidance supports `NavigationView` as the application shell and adaptive layouts/visual states for width changes. The existing OpenCareer shell already follows this pattern.

## Existing color tokens

These are already defined in `src/OpenCareer.App/App.xaml` and remain authoritative unless a later design pass deliberately changes them.

| Token | Value | Usage |
| --- | --- | --- |
| AppBackgroundBrush | `#0B1520` | page/window background |
| AppSurfaceBrush | `#111E2B` | primary cards |
| AppSurfaceAltBrush | `#172737` | selected/secondary surfaces |
| AppBorderBrush | `#294156` | card/divider borders |
| AppAccentBrush | `#4EA8DE` | normal active/accent state |
| AppSuccessBrush | `#52D273` | connected/healthy/completed |
| AppMutedTextBrush | `#9FB1C2` | labels/secondary text |

Additional semantic tokens should be added centrally rather than hard-coded per page:

- Warning: `#F4B942`
- Danger / active conflict: `#FF4D4D`
- Critical surface tint: very dark red derived from the base surface, never full-red page backgrounds.
- Friendly operational map: use blue family.
- Hostile operational map: use red family.
- Neutral/unknown: slate/gray.

Color must never be the only carrier of meaning. Pair it with iconography and text.

## Typography

Use normal WinUI typography and system font rendering.

- Page title: 28-30 px, Semibold.
- Section title: 18-22 px, Semibold.
- Card primary value: 20-28 px depending on density.
- Body: 14-16 px.
- Secondary/metadata: 12-13 px.
- Eyebrow label: 11-12 px, uppercase only for compact categories.
- Avoid more than three font sizes inside one compact card.

## Reference desktop grid

Reference canvas: 1920x1080.

- Navigation pane target: ~240 px expanded.
- Main content outer padding: 24 px.
- Standard inter-card gap: 16 px.
- 12-column layout for management pages.
- At 1920 px with the 240 px navigation pane, the content area is 1680 px. With 24 px side padding and 16 px gutters, one 12-column unit is approximately 121.3 px.
- Typical card spans:
  - 3 columns: ~396 px
  - 4 columns: ~533 px
  - 6 columns: ~808 px
  - 8 columns: ~1083 px

These are reference proportions, not fixed pixel contracts.

## Card system

Primary cards:

- 12-14 px corner radius.
- 1 px `AppBorderBrush`.
- 16-24 px internal padding.
- Keep one dominant question per card.
- Use inline mini-metrics only when they belong to the same decision.
- Prefer vertical scrolling over squeezing text to unreadable sizes.

A screen should normally contain:

- one page-level purpose/header,
- one dominant working region,
- supporting status/detail cards,
- one clear primary action or decision area.

## Shell behavior

Persistent shell header:

- current page title/context,
- simulator connection status,
- current aircraft identity when available,
- current operation state when meaningful,
- no fake flight/session status from telemetry alone.

Navigation uses the current canonical destinations:

1. Dashboard
2. Dispatch
3. Jobs
4. Current Flight
5. Map / World
6. Aircraft
7. Hangar / Bases
8. Maintenance
9. Company
10. Finances
11. Markets
12. Military / Government
13. Logbook
14. Career
15. Settings

Settings remains a normal navigation item for now because that is what the implemented shell contains.

## Adaptive states

### Wide — 1440 px and above

- Expanded left NavigationView.
- 12-column content grid.
- Two/three-pane work surfaces are allowed.
- Maps may occupy 6-8 columns.

### Standard — 1100-1439 px

- Left navigation remains available but may compact.
- Prefer 8-column equivalent layouts.
- Three-column cards collapse to two columns.
- Right-side detail panes may stack below the main region.

### Compact — 800-1099 px

- Navigation pane collapses/overlays.
- Single dominant work region plus stacked cards.
- Tables can switch to list/detail.
- Avoid side-by-side map + large detail pane.

### Narrow — below 800 px

OpenCareer is a Windows desktop application, not a phone UI. The application should remain usable for recovery/settings/status, but complex dispatch/market/war planning may require horizontal simplification or a minimum practical window size rather than destructive squeezing.

## Live-flight performance rule

Current Flight and active Military/Government operations are runtime-sensitive.

- UI refresh rate is independent of telemetry sampling.
- Do not bind every high-frequency sample directly to visual controls.
- Prefer immutable/coalesced ViewModel snapshots.
- Map tracks are decimated for display.
- Landing high-rate buffers remain in memory and are summarized after the event.
- No expensive animation loops.
- No large continuously changing image backgrounds.

## Empty, unavailable and confidence states

Every data-driven component must support:

- Loading
- Available
- Empty
- Unavailable / not implemented
- Stale
- Low-confidence
- Error

Examples:

- No active job is different from failed job loading.
- No trusted performance profile is different from zero required runway.
- MSFS disconnected is different from no active FlightSession.
- No military authorization is different from no military jobs.
- Unknown aircraft capability is different from incapable aircraft.

## Conflict / war visual mode

War/conflict mode is a contextual visual state, not a completely different application.

The same shell, spacing, typography and cards remain. Differences:

- `Military / Government` can receive a red conflict badge.
- The page may enter a dedicated Conflict Operations layout.
- Red is used for hostile territory, active threats, mission risk and conflict alerts.
- Blue remains friendly/control.
- The persistent connection/aircraft header remains normal.
- A conflict page never implies MSFS has combat.

OpenCareer owns all combat simulation. MSFS 2024 supplies aircraft telemetry only. No UI text should suggest native MSFS weapon, target or damage events exist.

## Accessibility and input

- Use standard WinUI controls where possible.
- Preserve keyboard focus order.
- Provide AutomationProperties names for icon-only controls.
- Support text scaling without clipping critical values.
- Maintain readable contrast.
- Do not make destructive actions one-click if irreversible.
- Tables/lists need meaningful selected state beyond color.
- Maps need text/list equivalents for mission-critical threat/objective data.

## Implementation rule

The mockups are a product specification, not permission to bypass architecture.

Required data flow remains:

```text
Domain/Application state
    -> ViewModel snapshot / commands
    -> WinUI page
```

For live simulator data:

```text
MSFS
    -> OpenCareer.SimConnect
    -> normalized telemetry
    -> application/domain processing
    -> ViewModel
    -> UI
```

For conflict operations:

```text
normalized player telemetry
    + deterministic OpenCareer conflict simulation
    -> mission/threat/combat state
    -> ViewModel
    -> UI
```
