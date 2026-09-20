# OpenCareer Production Home / Dashboard

Status: accepted product requirements and implementation contract for MBL-01.

## Product role

Home is a dynamic **career operations desk**, not a duplicate of Current Flight and not a static welcome screen.

It must answer:

1. What is happening now?
2. Is my aircraft/work/company situation healthy?
3. What are my best available opportunities?
4. What should I do next?
5. What just changed in my career/world?

The initial product uses one curated layout. Leave room for later module customization, but do not build a dashboard editor now.

## Hero composition

Use a split hero.

### Left — Flight desk

Show:

- relevant/current aircraft,
- aircraft access type,
- ready-for-work state,
- simulator/telemetry state,
- player location,
- aircraft location,
- distance from player to aircraft,
- blocker when the aircraft is not ready.

A production Home base must come from career persistence. Never substitute the KRME developer fixture.

### Right — Career + company

Career:

- Career Level,
- XP / next-level progress,
- licenses/ratings,
- relevant/total flight hours,
- number of aircraft owned,
- next meaningful milestone,
- latest meaningful milestone/activity.

Company when employed:

- employer/company,
- internal rank,
- standing,
- employment state,
- warning/probation/suspension/termination status.

If not employed, say so rather than fabricating an employer.

## Level + XP

Level/XP are accepted meta-progression.

They can summarize broad progress and provide pacing/prestige, but licenses, ratings, company standing, military/government authorization, aircraft capability, money and dispatch rules remain independent hard gates.

Avoid repetitive XP farming as the primary career loop.

## Company employment lifecycle

The player can be fired.

Supported lifecycle concepts:

- active,
- probation,
- demoted/rank loss,
- suspended,
- terminated/fired,
- later rehire/recovery where company policy allows.

Potential negative inputs can include repeated failed commitments, no-shows, preventable serious aircraft damage, repeated unsafe/reliability problems and company-specific performance expectations.

Fairness rules:

- one ordinary rough landing is not an automatic firing,
- a legitimate go-around/diversion is not a firing offense,
- simulator disconnect/recovery is not employee misconduct,
- defensible safety decisions should not be punished as failure,
- termination from one company cannot permanently brick the career,
- other employers and independent/employee work remain recovery paths.

Exact standing/firing thresholds belong to the Company/Jobs domain, not the Dashboard.

## Top Opportunities

Home shows exactly four best **currently available** jobs when four exist.

### Tiers

- Green — **Standard**
- Blue — **Specialist**
- Purple — **Elite**
- Orange/Gold — **Legendary**

Always display the text label/icon in addition to color.

### Selection order

1. job must currently be available/eligible,
2. higher opportunity tier,
3. higher personalized fit,
4. higher estimated net value,
5. lower reposition burden,
6. stable deterministic tie-breaker.

Personalized fit can later consider:

- license/rating match,
- aircraft access,
- relevant experience,
- company/reputation fit,
- typical play-session duration,
- location/reposition burden,
- route preference/history,
- career goals,
- world/market context.

The Dashboard does not promote a locked Legendary job over an available lower-tier job. Locked/aspirational jobs belong in the full Jobs browser with explicit requirements.

Tier is not permission to skip dispatch feasibility.

## Dynamic next-action engine

Home contains one primary **OpenCareer Recommends** action.

Sources produce structured guidance candidates. The engine deterministically chooses the highest-priority actionable candidate.

Examples:

- active flight -> Current Flight,
- accepted work not dispatched -> Dispatch,
- required maintenance -> Maintenance,
- probation/company review -> Company,
- urgent financial obligation -> Finances,
- no work + available opportunities -> Jobs,
- qualification gate -> Career.

Safety/operational blockers outrank routine recommendations.

## Financial summary

Header:

- current cash,
- signed daily net P/L.

Financial-health card:

- cash,
- today's net,
- upcoming obligations,
- link to Finances.

Do not compute authoritative money in the UI.

## Aircraft readiness

Show:

- current/relevant aircraft,
- access type,
- ready/not ready/unknown,
- home/current location,
- player location,
- distance from player,
- maintenance/dispatch blocker.

The registry/maintenance systems own those facts.

## World module

Home gets a condensed version of Map / World with:

- player/home/fleet geography,
- nearby jobs/routes,
- market signals,
- world events,
- government/military activity if authorized,
- later social/network hotspots.

Full exploration stays in Map / World.

## Recent activity

Show meaningful recent changes, not every telemetry event.

Examples:

- job completed,
- company standing changed,
- qualification earned,
- aircraft purchased/moved/repaired,
- maintenance due,
- finance/loan event,
- world/market event affecting player operations.

## OpenCareer Network

The core social feed is an **in-world simulated network**.

Possible authors:

- passengers,
- companies/employers,
- airports/bases,
- local businesses/organizations,
- customers,
- simulated world actors.

Search fields:

- city/region,
- airport,
- company,
- author,
- topic/text.

The feed updates as structured world/player events occur.

AI can optionally turn a structured event into natural dialogue/post text. The underlying structured event remains authoritative, and AI cannot invent:

- money,
- reputation changes,
- company firing,
- mission completion,
- aircraft ownership,
- market settlement.

Real-world public/social content, if ever connected, is a separate optional source with clear labeling and should not be mixed invisibly with the simulated feed.

## Current implementation boundary

Implemented now:

- snapshot contract,
- opportunity tiers,
- deterministic Top Opportunities selector,
- deterministic primary-guidance selector,
- dynamic WinUI production layout,
- navigation plumbing,
- searchable social-feed UI,
- honest empty states,
- live simulator/player-position projection where available.

Not yet authoritative:

- Jobs,
- Career Level/XP persistence,
- employer/company state,
- cash/daily P/L,
- aircraft registry/location/readiness,
- world/map state,
- simulated social posts.

Those later systems feed `IDashboardSnapshotSource`; the Dashboard must not invent temporary career values to look complete.
