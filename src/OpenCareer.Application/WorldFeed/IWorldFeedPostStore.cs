using OpenCareer.Domain.Events;

namespace OpenCareer.Application.WorldFeed;

public interface IWorldFeedPostStore
{
    Task SaveAsync(
        IReadOnlyCollection<WorldFeedPost> posts,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorldFeedPost>> ReadTimelineAsync(
        DateTimeOffset asOf,
        string? scopeId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorldFeedPost>> ReadHistoryAsync(
        DateTimeOffset asOf,
        string? scopeId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetLatestCreatedAtAsync(
        string? scopeId,
        CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);
}
