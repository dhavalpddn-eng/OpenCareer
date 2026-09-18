using OpenCareer.Domain.Events;

namespace OpenCareer.Application.WorldFeed;

public sealed record WorldFeedRefreshPolicy(
    TimeSpan MinimumRefreshInterval,
    int RecentPostContextLimit)
{
    public static WorldFeedRefreshPolicy Default { get; } =
        new(TimeSpan.FromMinutes(30), RecentPostContextLimit: 12);

    public void Validate()
    {
        if (MinimumRefreshInterval < TimeSpan.FromMinutes(1)
            || MinimumRefreshInterval > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumRefreshInterval));
        }

        if (RecentPostContextLimit is < 0 or > 50)
            throw new ArgumentOutOfRangeException(nameof(RecentPostContextLimit));
    }
}

public sealed record WorldFeedRefreshResult(
    bool WasSkipped,
    WorldFeedGenerationResult? Generation,
    DateTimeOffset? NextEligibleRefreshAt)
{
    public static WorldFeedRefreshResult Skipped(DateTimeOffset nextEligible) =>
        new(true, null, nextEligible);

    public static WorldFeedRefreshResult Completed(WorldFeedGenerationResult generation) =>
        new(false, generation, null);
}

public sealed class WorldFeedCoordinator
{
    private readonly WorldFeedNarrationService _narrationService;
    private readonly IWorldFeedPostStore _store;
    private readonly WorldFeedRefreshPolicy _policy;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WorldFeedCoordinator(
        WorldFeedNarrationService narrationService,
        IWorldFeedPostStore store,
        WorldFeedRefreshPolicy? policy = null)
    {
        _narrationService = narrationService ?? throw new ArgumentNullException(nameof(narrationService));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _policy = policy ?? WorldFeedRefreshPolicy.Default;
        _policy.Validate();
    }

    public async Task<WorldFeedRefreshResult> RefreshAsync(
        WorldFeedNarrationRequest request,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force)
            {
                var latest = await _store
                    .GetLatestCreatedAtAsync(request.ScopeId, cancellationToken)
                    .ConfigureAwait(false);

                if (latest is { } lastGenerated)
                {
                    var nextEligible = lastGenerated + _policy.MinimumRefreshInterval;
                    if (request.GeneratedAt < nextEligible)
                        return WorldFeedRefreshResult.Skipped(nextEligible);
                }
            }

            var recent = _policy.RecentPostContextLimit == 0
                ? Array.Empty<WorldFeedPost>()
                : await _store
                    .ReadHistoryAsync(
                        request.GeneratedAt,
                        request.ScopeId,
                        _policy.RecentPostContextLimit,
                        cancellationToken)
                    .ConfigureAwait(false);

            var enriched = request with { RecentPosts = recent };
            var generation = await _narrationService
                .GenerateAsync(enriched, cancellationToken)
                .ConfigureAwait(false);

            await _store
                .SaveAsync(generation.Posts, cancellationToken)
                .ConfigureAwait(false);

            return WorldFeedRefreshResult.Completed(generation);
        }
        finally
        {
            _gate.Release();
        }
    }
}
