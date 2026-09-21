using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.App.Services;

public sealed class UnavailableLogbookSource : ILogbookSource
{
    public Task<IReadOnlyList<LogbookEntry>> QueryAsync(
        LogbookQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<LogbookEntry>>(
            Array.Empty<LogbookEntry>());
    }

    public Task<LogbookEntry?> GetAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId == Guid.Empty)
            throw new ArgumentException("Logbook entry id is required.", nameof(entryId));

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<LogbookEntry?>(null);
    }
}
