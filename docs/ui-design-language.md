# OpenCareer UI Design Language

Status: **CANONICAL visual direction for the entire application**.

This document defines how OpenCareer should look and feel across Home, Jobs, Dispatch, Current Flight, Aircraft, Hangar/Bases, Maintenance, Company, Finances, Markets, Military/Government, Logbook, Career, Training/Tutorials, Settings and future screens.

When another design document conflicts with this visual language, this document wins for palette, material treatment and overall aesthetic. Functional behavior still comes from the relevant screen/domain specifications.

## One-sentence definition

OpenCareer is a **retro-modern aviation operations interface for MSFS 2024**, combining dark blue-gray riveted framing, vivid ivory briefing panels, realistic simulator imagery and dense mission-ready information design into one coherent career companion.

## Core identity

The visual reference is inspired by:

- 1950s/1960s aviation operations rooms,
- flight dispatch offices,
- aircraft documentation and briefing folders,
- route boards,
- pilot logbooks,
- classic travel-poster graphics,

while remaining a modern 2026 WinUI application.

OpenCareer must not look like:

- a generic SaaS dashboard,
- a WWII-only game,
- steampunk,
- full sepia nostalgia,
- a sci-fi cockpit,
- a neon game launcher,
- a frosted-glass mobile UI.

The historical influence is visual. The content remains current MSFS 2024 aviation.

## Non-negotiable visual structure

Every major screen should preserve this hierarchy:

1. **Dark cool structural shell**
   - deep blue-gray / charcoal metal,
   - visible framing,
   - subtle rivets / mechanical fasteners where appropriate,
   - inset panel boundaries.

2. **Vivid light content panels**
   - bright ivory / cool cream,
   - noticeably brighter than the earlier beige/olive concepts,
   - crisp black/navy text,
   - restrained paper grain.

3. **Deep navy controls**
   - section headers,
   - primary actions,
   - active navigation,
   - tabs.

4. **Real aviation imagery**
   - aircraft,
   - airports,
   - destinations,
   - route maps,
   - MSFS-style operational imagery.

5. **Small restrained accent colors**
   - brass/gold for prestige/rank/credits,
   - green for confirmed/healthy,
   - amber/orange for caution/unbound,
   - red for danger/failure/critical.

The characteristic contrast is:

```text
dark blue-gray shell
+ vivid ivory information sheets
+ realistic aviation photography
+ restrained navy/brass/status accents
```

Do not drift toward yellow, olive, brown or sepia as the dominant palette.

## Canonical palette direction

Exact production tokens may be tuned for contrast and accessibility, but the visual relationships must remain stable.

### Structural shell

- **Midnight Aviation Navy** — primary shell/background.
- **Blue-Graphite Metal** — secondary structure.
- **Gunmetal Slate** — borders/inset surfaces.
- **Cool Steel Highlight** — inner edges / bevels.

The shell should read as painted aircraft/operations equipment, not pure black.

### Content surfaces

- **Vivid Ivory** — primary information/card background.
- **Cool Cream** — secondary paper surface.
- **Pale Document Gray** — tables/alternate rows.

Whites should feel clean and vivid. Avoid muddy tan.

### Primary accents

- **Deep Aviation Navy** — buttons, headers, active tabs.
- **Desaturated Sky Blue** — selection state/highlights.
- **Restrained Brass** — prestige, rank, finance highlights only.

### Semantic colors

- **Signal Green** — connected, healthy, completed, verified.
- **Amber/Orange** — caution, pending dependency, UNBOUND.
- **Signal Red** — failure, destructive, safety-critical, UNBOUND — REQUIRED.
- **Muted Gray** — unavailable/disabled/non-authoritative.

Color is never the only signal.

## Material language

### Structural metal

Use for:

- top command bar,
- navigation chassis,
- outer application frame,
- section frames,
- footer.

Characteristics:

- dark cool finish,
- thin edge highlights,
- restrained rivet/fastener treatment,
- subtle wear,
- clear physical panel boundaries.

Do not over-texture it. It must remain readable.

### Briefing paper

Use for:

- cards,
- lists,
- mission sheets,
- detail panels,
- ledgers,
- settings sections.

Characteristics:

- bright ivory,
- subtle document grain,
- thin rules/dividers,
- printed/typed operational feel,
- mild age character only.

It should resemble a clean flight operations document, not fantasy parchment.

### Image windows

Use for:

- aircraft hero panels,
- airport/destination imagery,
- world map,
- route map,
- mission scene imagery.

Characteristics:

- crisp modern image quality,
- strong framing,
- restrained labels over imagery,
- imagery must be operationally relevant.

### Controls

Use:

- deep navy enamel/painted-metal buttons,
- clear borders,
- compact labels,
- mechanical/instrument-panel feel.

Avoid rounded floating glass controls.

## Typography roles

Use three roles.

### Display / section type

Used for:

- page titles,
- major module headers,
- hero headings.

Characteristics:

- condensed sans / industrial grotesk,
- strong uppercase where appropriate,
- aviation-signage tone,
- highly legible.

### Operational text

Used for:

- data,
- tables,
- lists,
- buttons,
- navigation,
- telemetry.

Characteristics:

- clean modern sans,
- compact,
- high readability,
- tabular alignment where useful.

### Script accent

Used only for:

- slogans,
- poster phrases,
- decorative margin notes.

Never use script for critical information.

## Global shell

All major screens should share the same visual chassis.

### Top status bar

May include:

- OpenCareer identity,
- MSFS 2024 connection,
- weather,
- local/simulator time,
- current location,
- career rank/level summary where applicable,
- credits/cash,
- current aircraft state.

The bar is dense, compact and always readable.

### Left navigation

Primary destinations:

- Home,
- Jobs,
- Dispatch,
- Current Flight,
- Map / World,
- Aircraft,
- Hangar / Bases,
- Maintenance,
- Company,
- Finances,
- Markets,
- Military / Government,
- Logbook,
- Career,
- Training/Tutorials,
- Settings.

Feature visibility may follow actual implementation/progression, but visual treatment stays consistent.

### Main workspace

Use a modular grid with:

- one dominant hero/work region,
- one or two secondary side panels,
- lower status/summary cards,
- quick actions,
- occasional brand/poster tile.

### Footer

Thin structural strip for:

- product/version,
- short slogan,
- subtle build/status information.

## Home screen

The Home screen is a career operations desk, not an active mission page.

Recommended structure:

- large welcome/location/aircraft hero,
- **Top Opportunities** panel showing 3–4 jobs worth traveling/flying to,
- career status,
- world map,
- owned/current aircraft summary,
- recent activity,
- quick start,
- optional poster/brand tile.

The opportunity tile should show meaningful differences such as:

- destination,
- pay,
- estimated duration,
- job family,
- difficulty/risk,
- reposition requirement/cost when relevant.

Do not duplicate the hero aircraft card with another identical aircraft card unless it adds different information.

## Jobs

Style:

- departures board + operations ledger.

Show:

- compact sortable list,
- destination,
- type,
- duration,
- reward,
- risk/difficulty,
- aircraft/access requirements,
- relocation/reposition implications,
- right-side detail/briefing sheet.

## Dispatch

Style:

- flight-planning desk / briefing board.

Show:

- aircraft,
- payload,
- fuel,
- runway feasibility,
- weather/environment,
- route,
- costs,
- mission requirements,
- readiness/validation checklist.

## Current Flight

Style:

- live operations board.

Show:

- phase-aware checklist,
- live mission status,
- route/map,
- essential telemetry,
- mission events,
- next required action,
- tutorial overlay when needed.

Checklist rows display Controller and Keyboard bindings inline.

Binding status rules:

- resolved binding: normal,
- UNBOUND: amber/orange,
- UNBOUND — REQUIRED: red,
- Binding unavailable: amber/orange,
- Cockpit only: neutral,
- No input required: neutral.

## Aircraft

Style:

- aircraft dossier / hangar registry.

Show:

- image,
- identity,
- access type,
- condition,
- location,
- maintenance,
- hours/experience,
- ownership/finance restrictions,
- capability.

## Hangar / Bases

Style:

- airport/base operations board.

Show:

- base locations,
- aircraft location,
- hangar/storage,
- services,
- expansion,
- relationships,
- repositioning.

## Maintenance

Style:

- maintenance record / service card.

Show:

- overall condition,
- due items,
- component/service status,
- repairs,
- parts/MRO availability,
- downtime,
- cost,
- history.

## Company

Style:

- operations office.

Show:

- routes,
- bases,
- contracts,
- crew/staff,
- utilization,
- operating performance,
- expansion.

## Finances

Style:

- aviation ledger / bank statement.

Show:

- cash,
- income,
- expenses,
- loans,
- insurance,
- maintenance reserves,
- financing,
- transaction history.

Charts must remain visually integrated and restrained.

## Markets

Style:

- route/commodity intelligence board.

Show:

- regional demand,
- supply,
- commodity pressure,
- fuel/service conditions,
- trends,
- opportunity indicators.

## Military / Government

Same application, more procedural/tactical.

May increase:

- stencil-like labels,
- briefing-sheet formality,
- map overlays,
- muted tactical symbols.

Do not change into a separate visual franchise.

## Logbook

Style:

- pilot logbook + flight summary.

Show:

- route,
- aircraft,
- times,
- takeoff/landing events,
- fuel,
- landing metrics,
- mission result,
- incidents,
- settlement.

## Career

Style:

- aviator record file.

Show:

- licenses/ratings,
- reputation,
- relationships,
- experience,
- qualifications,
- milestones,
- access.

Avoid making XP grinding the primary visual story.

## Training / Tutorials

Style:

- flight-school binder / instructional operations sheet.

Support:

- app intro,
- first-job walkthrough,
- mission-family tutorials,
- live checklists,
- diagrams,
- Controller/Keyboard bindings,
- automatic verification from trusted evidence,
- recovery instructions.

## Settings

Style:

- configuration/maintenance panel.

Less decorative than other screens but still uses the same shell/paper language.

## Tables and lists

Use:

- compact rows,
- thin separators,
- clear headers,
- pale alternate row states if needed,
- desaturated-blue selected row,
- aligned numeric columns.

Dense information is acceptable if it remains scannable.

## Tabs

Use physical document/binder tab language:

- seated/inset active tab,
- muted inactive tabs,
- thin framed boundaries.

Do not use glossy browser tabs.

## Cards

Cards should have:

- clear header strip,
- framed content,
- one dominant purpose,
- consistent padding,
- obvious CTA only when actionable.

## Icons

Prefer:

- aircraft silhouettes,
- maps,
- route pins,
- hangars,
- fuel,
- wrench,
- clipboard,
- logbook,
- weather,
- tower/radar,
- finance/coin,
- qualification/rank.

Avoid playful emoji-like icons.

## Motion

Motion should feel mechanical and purposeful:

- tab slide,
- panel reveal,
- checklist tick,
- subtle status pulse,
- route draw,
- restrained hover/press.

Avoid bounce, confetti, loot-box animation and excessive motion.

## Screen-density rule

OpenCareer can be information-dense.

The target is:

**operations-board density with poster-quality composition**.

Density must be organized with:

- clear zones,
- aligned values,
- strong module headers,
- consistent spacing,
- meaningful imagery.

## Mode-specific tone

### Civilian

- optimistic,
- travel-poster influence,
- professional.

### Government

- restrained,
- procedural,
- official.

### Military/conflict

- tactical,
- denser map/briefing treatment,
- stronger warning hierarchy,
- same core palette and shell.

### Training

- calmer,
- sequential,
- instructional,
- reduced distractions.

## Branding

Approved direction:

- serious,
- aspirational,
- aviation-first,
- grounded,
- adventurous.

Approved slogan family includes:

- **Same Skies. More Possibilities.**
- **A Higher Purpose In A Larger World.**
- **Real Places. Real Aviation. Your Story.**
- **Good Flights. Better Journeys.**

Use sparingly.

## Accessibility

The retro styling must never reduce usability.

Requirements:

- high contrast text,
- vivid enough paper backgrounds,
- script never carries critical information,
- status colors always paired with text/icon,
- keyboard navigation,
- text scaling,
- visible focus state,
- no critical information hidden only in imagery.

## Anti-drift rules

Do not introduce:

- olive/green wash across the interface,
- yellow-heavy beige panels,
- dominant brown/sepia,
- bright SaaS cyan,
- neon,
- frosted glass,
- cartoon styling,
- excessive grunge,
- rounded mobile-app cards everywhere.

If a new screen looks like it belongs to a different product, it is wrong.

## Design review checklist

Before approving a screen:

1. Does it retain the dark blue-gray shell?
2. Are paper panels vivid ivory rather than muddy beige?
3. Is navy still the dominant control accent?
4. Does it feel aviation-operational rather than generic?
5. Does it remain grounded in MSFS 2024?
6. Is information dense but scannable?
7. Are decorative vintage elements subordinate to usability?
8. Does it look like the same application as Home/Jobs/Current Flight?
9. Are state colors semantic and restrained?
10. Are images operationally relevant?

If any answer is no, revise before implementation.
