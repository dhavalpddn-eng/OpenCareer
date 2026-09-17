using OpenCareer.Domain.Events;

namespace OpenCareer.Application.WorldFeed;

public sealed record WorldFeedNarrationRequest(
    ulong CareerSeed,
    DateTimeOffset GeneratedAt,
    string ScopeId,
    IReadOnlyList<WorldSignal> Signals,
    IReadOnlyList<WorldEventInstance> ActiveEvents,
    IReadOnlyList<WorldFeedPost>? RecentPosts = null,
    int MaximumPosts = 6)
{
    public IReadOnlyList<WorldFeedPost> EffectiveRecentPosts => RecentPosts ?? Array.Empty<WorldFeedPost>();

    public IReadOnlyList<WorldSignal> UsableSignals =>
        Signals.Where(signal => signal.IsUsableAt(GeneratedAt)).ToArray();

    public IReadOnlyList<WorldEventInstance> CurrentEvents =>
        ActiveEvents.Where(worldEvent => worldEvent.IsActiveAt(GeneratedAt)).ToArray();

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ScopeId);
        ArgumentNullException.ThrowIfNull(Signals);
        ArgumentNullException.ThrowIfNull(ActiveEvents);

        if (MaximumPosts is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(MaximumPosts));

        foreach (var signal in Signals)
            signal.Validate();

        foreach (var worldEvent in ActiveEvents)
        {
            if (string.IsNullOrWhiteSpace(worldEvent.InstanceId)
                || string.IsNullOrWhiteSpace(worldEvent.DefinitionId)
                || string.IsNullOrWhiteSpace(worldEvent.Name)
                || worldEvent.EndsAt <= worldEvent.StartsAt)
            {
                throw new ArgumentException("World-event narration inputs must be internally consistent.", nameof(ActiveEvents));
            }
        }

        foreach (var post in EffectiveRecentPosts)
            post.Validate();
    }
}

public interface IWorldFeedNarrator
{
    string Name { get; }

    Task<IReadOnlyList<WorldFeedPost>> GenerateAsync(
        WorldFeedNarrationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record WorldFeedGenerationResult(
    IReadOnlyList<WorldFeedPost> Posts,
    bool UsedOfflineFallback,
    string NarratorName,
    string? FallbackReason = null);

public sealed class WorldFeedNarrationService
{
    private readonly IWorldFeedNarrator? _primaryNarrator;
    private readonly IWorldFeedNarrator _offlineNarrator;

    public WorldFeedNarrationService(
        IWorldFeedNarrator? primaryNarrator,
        IWorldFeedNarrator offlineNarrator)
    {
        _primaryNarrator = primaryNarrator;
        _offlineNarrator = offlineNarrator ?? throw new ArgumentNullException(nameof(offlineNarrator));
    }

    public async Task<WorldFeedGenerationResult> GenerateAsync(
        WorldFeedNarrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (_primaryNarrator is not null)
        {
            try
            {
                var primaryPosts = await _primaryNarrator
                    .GenerateAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                var validated = ValidatePosts(primaryPosts, request);
                if (validated.Count > 0)
                {
                    return new WorldFeedGenerationResult(
                        validated,
                        UsedOfflineFallback: false,
                        _primaryNarrator.Name);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return await GenerateOfflineAsync(
                    request,
                    exception.GetType().Name,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return await GenerateOfflineAsync(request, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorldFeedGenerationResult> GenerateOfflineAsync(
        WorldFeedNarrationRequest request,
        string? reason,
        CancellationToken cancellationToken)
    {
        var posts = await _offlineNarrator
            .GenerateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return new WorldFeedGenerationResult(
            ValidatePosts(posts, request),
            UsedOfflineFallback: true,
            _offlineNarrator.Name,
            reason);
    }

    private static IReadOnlyList<WorldFeedPost> ValidatePosts(
        IReadOnlyList<WorldFeedPost> posts,
        WorldFeedNarrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(posts);

        if (posts.Count > request.MaximumPosts)
            throw new InvalidOperationException("A narrator returned more posts than the request allows.");

        var ids = new HashSet<Guid>();
        foreach (var post in posts)
        {
            post.Validate();
            if (post.CreatedAt != request.GeneratedAt)
                throw new InvalidOperationException("Narrated posts must use the requested generation timestamp.");
            if (!ids.Add(post.PostId))
                throw new InvalidOperationException("A narrator returned duplicate post identifiers.");
        }

        return posts.ToArray();
    }
}
