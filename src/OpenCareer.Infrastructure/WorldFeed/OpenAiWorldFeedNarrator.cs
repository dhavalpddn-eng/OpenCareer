using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenCareer.Application.WorldFeed;
using OpenCareer.Domain.Events;

namespace OpenCareer.Infrastructure.WorldFeed;

public sealed class OpenAiWorldFeedOptions
{
    public OpenAiWorldFeedOptions(
        string apiKey,
        string model,
        Uri? endpoint = null,
        TimeSpan? requestTimeout = null)
    {
        ApiKey = apiKey;
        Model = model;
        Endpoint = endpoint;
        RequestTimeout = requestTimeout;
        Validate();
    }

    public string ApiKey { get; }
    public string Model { get; }
    public Uri? Endpoint { get; }
    public TimeSpan? RequestTimeout { get; }

    public Uri EffectiveEndpoint => Endpoint ?? new Uri("https://api.openai.com/v1/responses");
    public TimeSpan EffectiveRequestTimeout => RequestTimeout ?? TimeSpan.FromSeconds(20);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ApiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(Model);

        if (!EffectiveEndpoint.IsAbsoluteUri)
            throw new ArgumentException("OpenAI endpoint must be absolute.", nameof(Endpoint));

        if (EffectiveRequestTimeout < TimeSpan.FromSeconds(1)
            || EffectiveRequestTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
        }
    }
}

/// <summary>
/// Generates player-facing social posts from OpenCareer's already-authoritative simulated state.
/// It never enables web search or any other OpenAI tool and therefore performs no live news collection.
/// </summary>
public sealed class OpenAiWorldFeedNarrator : IWorldFeedNarrator
{
    private const string Instructions =
        """
        You write a concise in-game aviation social/news feed for OpenCareer.
        Every fact supplied in the input is simulated OpenCareer state, not a claim about the current real world.
        Use only supplied facts. Do not browse, infer outside facts, invent current real events, invent named companies,
        invent people, or claim a real military operation is occurring. Recent posts are continuity context only and
        are not new authoritative facts. Write natural short social/news style posts that explain why aviation demand
        or operations feel different. Never state rewards, balances, eligibility, ownership, or mission completion.
        Return only the requested structured output.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OpenAiWorldFeedOptions _options;

    public OpenAiWorldFeedNarrator(HttpClient httpClient, OpenAiWorldFeedOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public string Name => "openai-responses";

    public async Task<IReadOnlyList<WorldFeedPost>> GenerateAsync(
        WorldFeedNarrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var facts = BuildFacts(request);
        if (facts.Count == 0)
            return Array.Empty<WorldFeedPost>();

        var payload = BuildRequestPayload(request, facts);
        using var message = new HttpRequestMessage(HttpMethod.Post, _options.EffectiveEndpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.EffectiveRequestTimeout);

        using var response = await _httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);

        var raw = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        if (raw.Length > 500_000)
            throw new InvalidDataException("OpenAI world-feed response exceeded the allowed size.");

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"OpenAI world-feed request failed with HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);

        var outputText = ExtractOutputText(raw);
        var envelope = JsonSerializer.Deserialize<AiEnvelope>(outputText, JsonOptions)
            ?? throw new InvalidDataException("OpenAI world-feed output was empty.");

        if (envelope.Posts is null || envelope.Posts.Count == 0)
            return Array.Empty<WorldFeedPost>();
        if (envelope.Posts.Count > request.MaximumPosts)
            throw new InvalidDataException("OpenAI returned too many world-feed posts.");

        var factMap = facts.ToDictionary(fact => fact.Key, StringComparer.Ordinal);
        var posts = new List<WorldFeedPost>(envelope.Posts.Count);

        for (var index = 0; index < envelope.Posts.Count; index++)
        {
            var dto = envelope.Posts[index];
            if (!Enum.TryParse<WorldFeedPostCategory>(dto.Category, ignoreCase: true, out var category))
                throw new InvalidDataException($"Unknown world-feed category '{dto.Category}'.");
            if (string.IsNullOrWhiteSpace(dto.Headline) || dto.Headline.Length > 140)
                throw new InvalidDataException("AI world-feed headline is invalid.");
            if (string.IsNullOrWhiteSpace(dto.Body) || dto.Body.Length > 500)
                throw new InvalidDataException("AI world-feed body is invalid.");
            if (dto.FactKeys is null || dto.FactKeys.Count == 0 || dto.FactKeys.Count > 4)
                throw new InvalidDataException("AI world-feed post must reference one to four supplied facts.");

            var referencedFacts = new List<FeedFact>(dto.FactKeys.Count);
            foreach (var key in dto.FactKeys.Distinct(StringComparer.Ordinal))
            {
                if (!factMap.TryGetValue(key, out var fact))
                    throw new InvalidDataException("AI world-feed output referenced an unknown fact.");
                referencedFacts.Add(fact);
            }

            var signalIds = referencedFacts
                .Where(fact => fact.SignalId.HasValue)
                .Select(fact => fact.SignalId!.Value)
                .Distinct()
                .ToArray();
            var eventIds = referencedFacts
                .Where(fact => fact.WorldEventId is not null)
                .Select(fact => fact.WorldEventId!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var scope = referencedFacts
                .Select(fact => fact.ScopeId)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? request.ScopeId;

            var expiry = referencedFacts
                .Select(fact => fact.ExpiresAt)
                .Where(value => value > request.GeneratedAt)
                .DefaultIfEmpty(request.GeneratedAt.AddHours(6))
                .Min();

            var post = new WorldFeedPost(
                CreatePostId(request, index, dto.Headline),
                request.GeneratedAt,
                category,
                dto.Headline.Trim(),
                dto.Body.Trim(),
                scope,
                expiry,
                signalIds,
                eventIds,
                IsAiGenerated: true,
                SourceDisclosure: "AI-generated from OpenCareer simulated state; no live web/news collection.");

            post.Validate();
            posts.Add(post);
        }

        return posts;
    }

    private static object BuildPayload(
        WorldFeedNarrationRequest request,
        IReadOnlyList<FeedFact> facts)
    {
        var input = new
        {
            generatedAt = request.GeneratedAt,
            scopeId = request.ScopeId,
            maximumPosts = request.MaximumPosts,
            facts = facts.Select(fact => new
            {
                key = fact.Key,
                category = fact.Category.ToString(),
                scopeId = fact.ScopeId,
                statement = fact.Statement,
                expiresAt = fact.ExpiresAt
            }),
            recentPosts = request.EffectiveRecentPosts
                .Take(12)
                .Select(post => new
                {
                    category = post.Category.ToString(),
                    headline = post.Headline,
                    body = post.Body
                })
        };

        return new Dictionary<string, object?>
        {
            ["model"] = null,
            ["instructions"] = Instructions,
            ["input"] = JsonSerializer.Serialize(input, JsonOptions),
            ["store"] = false,
            ["max_output_tokens"] = 1800,
            ["text"] = new Dictionary<string, object?>
            {
                ["format"] = new Dictionary<string, object?>
                {
                    ["type"] = "json_schema",
                    ["name"] = "opencareer_world_feed",
                    ["strict"] = true,
                    ["schema"] = BuildSchema()
                }
            }
        };
    }

    private object BuildRequestPayload(
        WorldFeedNarrationRequest request,
        IReadOnlyList<FeedFact> facts)
    {
        var payload = (Dictionary<string, object?>)BuildPayload(request, facts);
        payload["model"] = _options.Model;
        return payload;
    }

    private static Dictionary<string, object?> BuildSchema() =>
        new()
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>
            {
                ["posts"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["maxItems"] = 12,
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["category"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["enum"] = Enum.GetNames<WorldFeedPostCategory>()
                            },
                            ["headline"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["minLength"] = 1,
                                ["maxLength"] = 140
                            },
                            ["body"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["minLength"] = 1,
                                ["maxLength"] = 500
                            },
                            ["factKeys"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["minItems"] = 1,
                                ["maxItems"] = 4,
                                ["uniqueItems"] = true,
                                ["items"] = new Dictionary<string, object?> { ["type"] = "string" }
                            }
                        },
                        ["required"] = new[] { "category", "headline", "body", "factKeys" },
                        ["additionalProperties"] = false
                    }
                }
            },
            ["required"] = new[] { "posts" },
            ["additionalProperties"] = false
        };

    private static List<FeedFact> BuildFacts(WorldFeedNarrationRequest request)
    {
        var facts = new List<FeedFact>();

        foreach (var signal in request.UsableSignals)
        {
            facts.Add(new FeedFact(
                $"signal:{signal.SignalId:N}",
                SignalCategory(signal.Type),
                signal.ScopeId,
                $"Simulated {signal.Type}: magnitude {signal.Magnitude:+0.00;-0.00;0.00}, confidence {signal.Confidence:0.00}.",
                signal.ExpiresAt,
                signal.SignalId,
                null));
        }

        foreach (var worldEvent in request.CurrentEvents)
        {
            var scope = string.IsNullOrWhiteSpace(worldEvent.ScopeTarget)
                ? request.ScopeId
                : worldEvent.ScopeTarget;

            facts.Add(new FeedFact(
                $"event:{worldEvent.InstanceId}",
                EventCategory(worldEvent),
                scope!,
                $"Simulated event '{worldEvent.Name}' is active at tier {worldEvent.Tier}.",
                worldEvent.EndsAt,
                null,
                worldEvent.InstanceId));
        }

        return facts;
    }

    private static WorldFeedPostCategory SignalCategory(WorldSignalType type) => type switch
    {
        WorldSignalType.PassengerTrend => WorldFeedPostCategory.Travel,
        WorldSignalType.CargoTrend => WorldFeedPostCategory.Cargo,
        WorldSignalType.PublicEvent => WorldFeedPostCategory.Market,
        WorldSignalType.WeatherDisruption => WorldFeedPostCategory.Weather,
        WorldSignalType.AirportDisruption => WorldFeedPostCategory.Airport,
        WorldSignalType.CarrierServiceChange => WorldFeedPostCategory.Company,
        WorldSignalType.PublicServiceNeed => WorldFeedPostCategory.PublicService,
        WorldSignalType.RegionalSecurity => WorldFeedPostCategory.Security,
        _ => WorldFeedPostCategory.Market
    };

    private static WorldFeedPostCategory EventCategory(WorldEventInstance worldEvent)
    {
        if (worldEvent.Effects.AirspaceRestriction >= AirspaceRestriction.AuthorizedOperationsOnly)
            return WorldFeedPostCategory.Security;

        var opportunities = worldEvent.Effects.MissionOpportunities;
        if (opportunities.HasFlag(MissionOpportunity.DisasterRelief)
            || opportunities.HasFlag(MissionOpportunity.EmergencyMedical)
            || opportunities.HasFlag(MissionOpportunity.Evacuation)
            || opportunities.HasFlag(MissionOpportunity.SearchAndRescue))
        {
            return WorldFeedPostCategory.PublicService;
        }

        if (opportunities.HasFlag(MissionOpportunity.Cargo)
            || opportunities.HasFlag(MissionOpportunity.AogCourier))
        {
            return WorldFeedPostCategory.Cargo;
        }

        return WorldFeedPostCategory.Market;
    }

    private static string ExtractOutputText(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;

        if (root.TryGetProperty("status", out var status)
            && !string.Equals(status.GetString(), "completed", StringComparison.Ordinal))
        {
            throw new InvalidDataException("OpenAI world-feed response did not complete.");
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("OpenAI world-feed response did not contain output.");

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var itemType)
                || !string.Equals(itemType.GetString(), "message", StringComparison.Ordinal))
            {
                continue;
            }

            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var partType)
                    && string.Equals(partType.GetString(), "output_text", StringComparison.Ordinal)
                    && part.TryGetProperty("text", out var text)
                    && !string.IsNullOrWhiteSpace(text.GetString()))
                {
                    return text.GetString()!;
                }
            }
        }

        throw new InvalidDataException("OpenAI world-feed response did not contain output text.");
    }

    private static Guid CreatePostId(
        WorldFeedNarrationRequest request,
        int index,
        string headline)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"{request.CareerSeed}|{request.GeneratedAt:O}|{request.ScopeId}|{index}|{headline}");
        var hash = SHA256.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16));
    }

    private sealed record FeedFact(
        string Key,
        WorldFeedPostCategory Category,
        string ScopeId,
        string Statement,
        DateTimeOffset ExpiresAt,
        Guid? SignalId,
        string? WorldEventId);

    private sealed record AiEnvelope(List<AiPost>? Posts);
    private sealed record AiPost(string Category, string Headline, string Body, List<string>? FactKeys);
}
