# Live world / social feed design

This feature is **optional online flavor + signal collection**. OpenCareer remains fully playable offline. AI/news output is never authoritative for balances, ownership, mission completion, aircraft access, airspace restrictions or contract eligibility.

## Player experience

Add a world/social board that feels like an aviation-focused mix of local news, dispatch chatter, company updates and travel trends. Examples:

- "Weekend demand into Miami is climbing."
- "Express freight backlog building around Memphis."
- "Wildfire response crews are staging in the region."
- "Ceasefire announced; humanitarian and infrastructure flights are increasing."
- "A major carrier added seasonal service."
- "Local skydiving operator expects a busy weekend."

The feed should explain *why* markets and jobs are changing without becoming a wall of raw simulation variables.

## Two-layer architecture

```text
External/public sources (optional)
    -> OnlineSignalCollector
    -> normalized WorldSignal with source + timestamp + confidence
    -> deterministic validation/mapping
    -> authoritative game-world effects

Authoritative game-world effects + stored signals
    -> WorldFeedNarrator (AI optional; deterministic fallback available)
    -> WorldFeedPost
    -> SQLite
    -> WinUI World / Dispatch feed
```

Never reverse this flow. A generated social post cannot create a war, close an airport, move money or complete a mission.

## Offline mode

Offline careers synthesize the feed entirely from OpenCareer's deterministic economy and world events. The same world seed/state produces the same underlying event sequence; prose can use deterministic templates.

Core gameplay must not wait on an internet request.

## Optional OpenAI mode

For an online-enhanced career, a background application service may use the OpenAI **Responses API** with Web Search to gather recent public context and separately generate concise aviation-world summaries. Current OpenAI documentation describes Web Search as a Responses API tool for current information.

Rules:

1. Request only periodically (for example every few hours or on explicit refresh), never per telemetry frame.
2. Require structured output for extracted signals.
3. Every live signal needs source provenance, source timestamp when available, fetched-at time and a confidence/validation state.
4. Persist normalized signals locally before using them.
5. Deterministic code maps a validated signal into bounded simulation inputs.
6. GPT may write the public-facing social post from already validated state, but does not choose numeric rewards or access rules.
7. Cache aggressively. If API/web lookup fails, retain the last validated signals until they expire and fall back to offline feed generation.
8. User can disable online/world-news features completely.

Potential inexpensive production model choice can be decided later from current API pricing/capability; do not hardcode a model name into Domain.

## Proposed persistence

### WorldSignal

- `SignalId`
- `SignalType`
- `GeographicScope`
- `ScopeId`
- `ObservedAt`
- `FetchedAt`
- `ExpiresAt`
- `SourceKind`
- `SourceUri` or stable source reference
- `SourcePublisher`
- `Confidence`
- `ValidationStatus`
- structured attributes/payload

### WorldFeedPost

- `PostId`
- `CreatedAt`
- `ExpiresAt?`
- `ScopeId?`
- `PostCategory`
- `Headline`
- `Body`
- `RelatedSignalIds`
- `RelatedWorldEventIds`
- `IsAiGenerated`
- `SourceDisclosure`

Keep historical posts so the player's career develops a readable world timeline.

## Signal categories

Start with low-risk categories that map well to aviation economics:

- passenger destination trend,
- cargo/logistics trend,
- major public event/tourism surge,
- severe weather/disaster response,
- airport/service disruption,
- airline/cargo-operator route/service change,
- government/public-service need,
- regional security status.

A live geopolitical/security signal requires stronger provenance than a travel trend. Do not infer conflict status from social chatter alone.

## Real regions and conflict

OpenCareer may use real geography. For real current conflicts, factual state must come from curated/reliable current sources and be represented as a sourced `RegionalSecurityState`. The game should generate fictional contracts from the operational category (reconnaissance, transport, medevac, logistics, humanitarian relief) rather than claim the player is participating in a specific real operation.

This avoids fabricating military activity while still allowing the world to react to real conditions.

## Passenger trends

Passenger demand can combine:

- baseline airport/route demand,
- seasonality,
- historical BTS/airport activity,
- major event/tourism signals,
- player-established route relationships,
- temporary live trend signals.

A viral/trending destination should increase demand gradually and decay rather than instantly multiplying every route to that city.

## Cargo trends

Cargo markets should be commodity-aware. Suggested first categories:

- express parcels,
- general freight,
- mail,
- perishables,
- medical supplies,
- aircraft/AOG parts,
- industrial/automotive parts,
- electronics/high-value goods,
- humanitarian supplies,
- government/military logistics.

Each category should have different urgency, aircraft suitability, seasonality, shortage/backlog behavior and pay premiums. Existing `MarketState` demand/capacity/backlog mechanics can drive the underlying economics.

## UI concept

World feed should live beside—not inside—the authoritative job list. A post can link to affected markets/routes/jobs, but the job card must clearly show actual dispatch requirements separately.

Suggested screen:

```text
WORLD / NETWORK

Trending                   Regional Operations
----------------------     ----------------------------
Miami leisure demand ↑     Recovery logistics expanding
Memphis cargo backlog ↑    Medical supply demand ↑
Boston business travel ↑   Security status: sourced

Aviation Feed
------------------------------------------------------
[Cargo] Express volume rises at MEM
[Travel] South Florida weekend bookings trending
[Ops]    Regional recovery flights requested
[Company] Employer relationship unlocked a preferred route
```

## Implementation order

1. deterministic `WorldSignal` + `WorldFeedPost` domain records;
2. SQLite persistence and retention policy;
3. deterministic offline feed renderer;
4. connect existing economy/world-event signals;
5. add passenger/cargo trend signals;
6. add optional online collector behind an interface;
7. add OpenAI Responses/Web Search adapter only in Infrastructure;
8. validate and rate-limit; then expose in WinUI.

Do not add the OpenAI SDK/package to Domain.
