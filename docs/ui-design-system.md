# OpenCareer UI design system

> **Visual authority:** `docs/ui-design-language.md` is canonical for palette, material treatment and overall aesthetic. This file defines implementation-oriented layout/tokens/behavior. If older wording here conflicts visually with the canonical design language, follow `ui-design-language.md`.

Status: approved visual direction and implementation reference. This document defines the shared shell, visual tokens, layout rules, adaptive behavior and interaction rules for the production WinUI 3 application.

The current executable remains intentionally smaller than this target. Do not display fabricated balances, jobs, aircraft ownership, war state or mission progress merely to match the mockups. Populate a surface only when the corresponding application/domain data exists.

## Product character

OpenCareer should look like a professional aviation operations terminal: dense enough to be useful, calm enough to leave MSFS as the visual focus, and clearly separate ordinary operations from elevated warning/conflict states.

The approved baseline is:

- crisp graphite/navy shell rather than smoky or cinematic backgrounds,
- restrained aviation blue for normal emphasis,
- teal only for selected economy/market information where a second cool accent helps,
- green for healthy/confirmed state,
- amber for caution/dependency state,
- red only for dangerous/conflict/failure state,
- bright neutral text on clearly separated surfaces,
- thin borders,
- compact information cards,
- readable typography rather than decorative type,
- persistent simulator connection state,
- left NavigationView shell,
- map/route panels only where they carry operational value.

### Clarity rule

Do not use smoke, fog, diffuse cloud overlays, heavy bloom, neon glow, frosted-glass blur, soft vignettes or low-contrast blue-on-blue backgrounds as part of the production UI. Concept images may contain scenery, but production application surfaces should remain visually flat and sharp. Decorative backgrounds must never reduce text/card contrast.

## Canonical production stack

- WinUI 3 / Windows App SDK.
- Left `NavigationView` shell.
- `Frame` navigation.
- MVVM for screen state and commands.
- Standard WinUI controls where possible so text scaling, keyboard navigation and accessibility remain intact.
- `VisualStateManager`, adaptive grids and pane collapse for width changes.
- Do not put SimConnect, persistence or game calculations in Views.

### Implemented WinUI resource library

Application-level resources are split by responsibility:

- `Styles/DesignTokens.xaml` — canonical colors/brushes plus temporary compatibility aliases for older `App*` resource names.
- `Styles/ComponentStyles.xaml` — reusable typography, paper/metal panels, primary/secondary/danger buttons, form controls, tabs, lists, ledger/table rows, semantic badges and semantic status text.
- `App.xaml` — merges the dictionaries once for application-wide use.

Existing Dashboard, Current Flight, Settings, placeholder screens, shell status surfaces and tutorial overlay consume these shared resources. New pages should use the `OpenCareer*` resources rather than hard-coded colors, radii and text styling.

The compatibility `App*` aliases exist only to make incremental migration safe. New code should not introduce additional uses of them.

The design system deliberately keeps standard WinUI control templates instead of replacing them. This preserves platform keyboard focus, high-contrast behavior, text scaling and accessibility semantics while OpenCareer controls the visual surface treatment.

`tests/OpenCareer.Tests/UiDesignSystemContractTests.cs` protects the visual contract by checking the canonical vivid paper/cool shell colors, text contrast, required reusable style keys and duplicate resource keys.

Microsoft's current Windows app guidance supports `NavigationView` as the application shell and adaptive layouts/visual states for width changes. The existing OpenCareer shell already follows this pattern.

## Production color direction

The production WinUI palette is implemented in `src/OpenCareer.App/Styles/DesignTokens.xaml`. Do not duplicate hex values in pages. New UI should consume named brushes/tokens so later tuning remains centralized.

Canonical implemented tokens:

| Token | Value | Usage |
| --- | --- | --- |
| OpenCareerShellBackgroundColor | `#111C26` | outer application shell |
| OpenCareerShellRaisedColor | `#192A37` | raised metal/panes |
| OpenCareerShellInsetColor | `#223846` | inset structural surfaces |
| OpenCareerSteelBorderColor | `#405867` | structural borders |
| OpenCareerSteelHighlightColor | `#6E818D` | restrained edge/highlight |
| OpenCareerShellTextColor | `#FFFDF8` | primary text on dark structure |
| OpenCareerShellMutedTextColor | `#B7C3C9` | secondary shell text |
| OpenCareerPaperColor | `#FFFDF8` | vivid primary briefing paper |
| OpenCareerPaperAltColor | `#F5F2E9` | secondary paper surface |
| OpenCareerPaperMutedColor | `#E9E5DA` | inset/status paper |
| OpenCareerInkColor | `#18252D` | primary ink on paper |
| OpenCareerInkMutedColor | `#5C6870` | secondary ink |
| OpenCareerPaperRuleColor | `#C8C1B4` | document rules/dividers |
| OpenCareerNavyColor | `#29475C` | primary controls/headers |
| OpenCareerNavyRaisedColor | `#365E77` | raised/hover-capable navy |
| OpenCareerSelectionColor | `#9FB7C7` | desaturated selection emphasis |
| OpenCareerBrassColor | `#B58A48` | restrained prestige/finance accent |
| OpenCareerSuccessColor | `#3E7E57` | healthy/verified/completed |
| OpenCareerWarningColor | `#C58327` | caution/pending/unbound |
| OpenCareerDangerColor | `#B8453E` | danger/failure/critical |
| OpenCareerDisabledColor | `#7B878E` | unavailable/disabled |

The vivid paper surface is intentionally much brighter than the earlier beige/olive mockups. Do not reduce it back toward tan or sepia.

### Color usage

- Aviation blue: navigation selection, normal active controls, route lines, primary actions.
- Teal: optional economy/market comparison emphasis, never a second global accent competing with blue.
- Green: completed/healthy/verified only.
- Amber: dependency, caution, waiting on prerequisite, degraded confidence.
- Red: failure, active conflict, urgent threat, destructive action.
- White/off-white: main text.
- Slate: secondary text and inactive structure.

Do not fill whole pages with semantic colors. Apply them to badges, small bars, icons, outlines, route/threat marks and focused status areas.

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

- 10-12 px corner radius.
- 1 px `AppBorderBrush`.
- 16-24 px internal padding.
- Solid surface fills; no frosted/translucent blur.
- No outer glow.
- Shadows, if used at all, stay extremely subtle and structural.
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


## Visual effects policy

Production OpenCareer should favor clarity over cinematic atmosphere.

Allowed:

- crisp 1 px separators,
- subtle selected-row tint,
- restrained focus ring,
- small status-color accents,
- very light elevation differences between surfaces,
- sharp map overlays and route lines.

Avoid:

- smoke/fog textures,
- translucent haze across the content area,
- blurred mountain/cloud imagery behind text,
- glowing blue borders on every card,
- heavy gradients,
- bloom around icons,
- excessive transparency,
- large decorative background images while flying.

For the development progress/checklist screen specifically, use the same flat production palette. It should look like an internal project-control panel, with solid cards and sharp text, not like a promotional splash screen.
