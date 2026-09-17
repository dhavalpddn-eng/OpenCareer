using System.Buffers.Binary;
using OpenCareer.Domain.Events;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Application.WorldFeed;

public sealed class DeterministicWorldFeedNarrator : IWorldFeedNarrator
{
    public string Name => "offline-deterministic";

    public Task<IReadOnlyList<WorldFeedPost>> GenerateAsync(
        WorldFeedNarrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = BuildCandidates(request)
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Key, StringComparer.Ordinal)
            .Take(request.MaximumPosts)
            .ToArray();

        if (candidates.Length == 0)
        {
            candidates =
            [
                new FeedCandidate(
                    "quiet-network",
                    0,
                    WorldFeedPostCategory.CareerNetwork,
                    $"Network quiet around {request.ScopeId}",
                    "No major simulated market or operational changes are active right now. Local work continues to turn over normally.",
                    request.ScopeId,
                    request.GeneratedAt.AddHours(4),
                    Array.Empty<Guid>(),
                    Array.Empty<string>())
            ];
        }

        var posts = new List<WorldFeedPost>(candidates.Length);
        for (var i = 0; i < candidates.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[i];
            var random = DeterministicSeed.CreateStream(
                request.CareerSeed,
                $"world-feed:{request.GeneratedAt.UtcDateTime.Ticks}:{candidate.Key}:{i}");

            posts.Add(new WorldFeedPost(
                CreateGuid(random),
                request.GeneratedAt,
                candidate.Category,
                candidate.Headline,
                candidate.Body,
                candidate.ScopeId,
                candidate.ExpiresAt,
                candidate.RelatedSignalIds,
                candidate.RelatedWorldEventIds,
                IsAiGenerated: false));
        }

        return Task.FromResult<IReadOnlyList<WorldFeedPost>>(posts);
    }

    private static IEnumerable<FeedCandidate> BuildCandidates(WorldFeedNarrationRequest request)
    {
        foreach (var signal in request.UsableSignals)
            yield return FromSignal(signal);

        foreach (var worldEvent in request.CurrentEvents)
            yield return FromEvent(worldEvent, request.ScopeId);
    }

    private static FeedCandidate FromSignal(WorldSignal signal)
    {
        var direction = signal.Magnitude switch
        {
            >= 0.35 => "surging",
            >= 0.10 => "rising",
            <= -0.35 => "dropping sharply",
            <= -0.10 => "softening",
            _ => "steady"
        };

        var priority = Math.Abs(signal.Magnitude) * (0.5 + signal.Confidence);
        return signal.Type switch
        {
            WorldSignalType.PassengerTrend => new(
                $"signal:{signal.SignalId:N}",
                priority,
                WorldFeedPostCategory.Travel,
                $"Passenger demand {direction} around {signal.ScopeId}",
                $"OpenCareer market indicators show passenger demand {direction}. Route availability and pricing pressure may adjust as simulated capacity responds.",
                signal.ScopeId,
                signal.ExpiresAt,
                [signal.SignalId],
                Array.Empty<string>()),

            WorldSignalType.CargoTrend => new(
                $"signal:{signal.SignalId:N}",
                priority,
                WorldFeedPostCategory.Cargo,
                $"Cargo pressure {direction} around {signal.ScopeId}",
                $"Simulated freight demand is {direction}. Cargo operators may post more or fewer jobs as backlog, payload availability and capacity rebalance.",
                signal.ScopeId,
                signal.ExpiresAt,
                [signal.SignalId],
                Array.Empty<string>()),

            WorldSignalType.WeatherDisruption => new(
                $"signal:{signal.SignalId:N}",
                priority,
                WorldFeedPostCategory.Weather,
                $"Weather operations changing around {signal.ScopeId}",
                "The simulated operating picture is being affected by weather. Expect temporary changes to demand, capacity and mission mix.",
                signal.ScopeId,
                signal.ExpiresAt,
                [signal.SignalId],
                Array.Empty<string>()),

            WorldSignalType.AirportDisruption => new(
                $"signal:{signal.SignalId:N}",
                priority,
                WorldFeedPostCategory.Airport,
                $"Airport operations disrupted at {signal.ScopeId}",
                "Simulated airport capacity is constrained. Dispatch availability and service-dependent jobs may shift until operations recover.",
                signal.ScopeId,
                signal.ExpiresAt,
                [signal.SignalId],
                Array.Empty<string>()),

            WorldSignalType.CarrierServiceChange => new(
                $"signal:{signal.SignalId:N}",
                priority,
                WorldFeedPostCategory.Company,
                $"Operator activity changing around {signal.ScopeId}",
                "A simulated service change is affecting the local network. Related passenger, cargo and repositioning work may adjust.",
                signal.ScopeId,
                signal.ExpiresAt,
                [signal.SignalId],
                Array.Empty<string>()),

            WorldSignalType.PublicServiceNeed => new(
                $"signal:{signal.SignalId:N}",
                priority,
                WorldFeedPostCategory.PublicService,
                $"Public-service flying demand {direction} in {signal.ScopeId}",
                "The simulated world is creating additional public-service aviation needs. Qualified operators may see a different contract mix.",
                signal.ScopeId,
                signal.ExpiresAt,
                [signal.SignalId],
                Array.Empty<string>()),

            WorldSignalType.RegionalSecurity => new(
                $"signal:{signal.SignalId:N}",
                priority,
                WorldFeedPostCategory.Security,
                $"Security activity changing in {signal.ScopeId}",
                "A simulated regional security change is affecting aviation demand. Access and mission eligibility remain controlled by OpenCareer rules.",
                signal.ScopeId,
                signal.ExpiresAt,
                [signal.SignalId],
                Array.Empty<string>()),

            _ => new(
                $"signal:{signal.SignalId:N}",
                priority,
                WorldFeedPostCategory.Market,
                $"Market conditions changing around {signal.ScopeId}",
                "OpenCareer market indicators have shifted and may alter the local mix of available work.",
                signal.ScopeId,
                signal.ExpiresAt,
                [signal.SignalId],
                Array.Empty<string>())
        };
    }

    private static FeedCandidate FromEvent(WorldEventInstance worldEvent, string defaultScope)
    {
        var category = EventCategory(worldEvent);
        var scope = string.IsNullOrWhiteSpace(worldEvent.ScopeTarget) ? defaultScope : worldEvent.ScopeTarget;
        var priority = 1.0 + (int)worldEvent.Tier * 0.5;

        return new FeedCandidate(
            $"event:{worldEvent.InstanceId}",
            priority,
            category,
            worldEvent.Name,
            $"A simulated {worldEvent.Tier} event is active for {scope}. The job market and operating environment may change until the event clears.",
            scope,
            worldEvent.EndsAt,
            Array.Empty<Guid>(),
            [worldEvent.InstanceId]);
    }

    private static WorldFeedPostCategory EventCategory(WorldEventInstance worldEvent)
    {
        var opportunities = worldEvent.Effects.MissionOpportunities;
        if (worldEvent.Effects.AirspaceRestriction >= AirspaceRestriction.AuthorizedOperationsOnly
            || opportunities.HasFlag(MissionOpportunity.MilitaryIntercept)
            || opportunities.HasFlag(MissionOpportunity.MilitaryEscort)
            || opportunities.HasFlag(MissionOpportunity.MilitaryPatrol))
        {
            return WorldFeedPostCategory.Security;
        }

        if (worldEvent.DefinitionId.Contains("hurricane", StringComparison.OrdinalIgnoreCase)
            || worldEvent.DefinitionId.Contains("wildfire", StringComparison.OrdinalIgnoreCase)
            || worldEvent.DefinitionId.Contains("storm", StringComparison.OrdinalIgnoreCase))
        {
            return WorldFeedPostCategory.Weather;
        }

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

    private static Guid CreateGuid(DeterministicRandom random)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, random.NextUInt64());
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], random.NextUInt64());
        return new Guid(bytes);
    }

    private sealed record FeedCandidate(
        string Key,
        double Priority,
        WorldFeedPostCategory Category,
        string Headline,
        string Body,
        string ScopeId,
        DateTimeOffset ExpiresAt,
        IReadOnlyList<Guid> RelatedSignalIds,
        IReadOnlyList<string> RelatedWorldEventIds);
}
