using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Logbook;

public sealed record LogbookQuery(
    string? SearchText = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Limit = 100)
{
    public void Validate()
    {
        if (Limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(Limit));

        if (From is { } from && To is { } to && to < from)
            throw new ArgumentException("Logbook query end cannot precede start.");
    }
}

public interface ILogbookSource
{
    Task<IReadOnlyList<LogbookEntry>> QueryAsync(
        LogbookQuery query,
        CancellationToken cancellationToken = default);

    Task<LogbookEntry?> GetAsync(
        Guid entryId,
        CancellationToken cancellationToken = default);
}
