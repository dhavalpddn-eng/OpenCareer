using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Logbook;

public sealed record LogbookQuery(
    string? SearchText = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    LogbookEntryKind? EntryKind = null,
    FlightSafetyOutcome? SafetyOutcome = null,
    MissionOutcome? MissionOutcome = null,
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


public static class LogbookQueryMatcher
{
    public static IReadOnlyList<LogbookEntry> Apply(
        IEnumerable<LogbookEntry> entries,
        LogbookQuery query)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();

        return entries
            .Where(entry => Matches(entry, query))
            .OrderByDescending(static entry => entry.Debrief.EndedAt)
            .ThenBy(static entry => entry.EntryId)
            .Take(query.Limit)
            .ToArray();
    }

    public static bool Matches(LogbookEntry entry, LogbookQuery query)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();

        FlightDebrief debrief = entry.Debrief;

        if (query.From is { } from && debrief.EndedAt < from)
            return false;

        if (query.To is { } to && debrief.EndedAt > to)
            return false;

        if (query.EntryKind is { } kind && debrief.EntryKind != kind)
            return false;

        if (query.SafetyOutcome is { } safety && debrief.SafetyOutcome != safety)
            return false;

        if (query.MissionOutcome is { } mission && debrief.MissionOutcome != mission)
            return false;

        if (string.IsNullOrWhiteSpace(query.SearchText))
            return true;

        string search = query.SearchText.Trim();

        return Contains(debrief.Aircraft.DisplayName, search) ||
               Contains(debrief.Aircraft.Family, search) ||
               Contains(debrief.Aircraft.TailNumber, search) ||
               Contains(debrief.Route.PlannedOrigin, search) ||
               Contains(debrief.Route.PlannedDestination, search) ||
               Contains(debrief.Route.ActualDeparture, search) ||
               Contains(debrief.Route.ActualArrival, search) ||
               Contains(debrief.Route.DiversionLocation, search) ||
               Contains(debrief.Payload.CargoDescription, search) ||
               Contains(debrief.Payload.Outcome, search) ||
               debrief.Events.Any(item =>
                   Contains(item.Category, search) ||
                   Contains(item.Text, search));
    }

    private static bool Contains(string? value, string search) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(search, StringComparison.OrdinalIgnoreCase);
}


public enum LogbookAppendDisposition
{
    Appended,
    AlreadyExists
}

public sealed record LogbookAppendResult(
    LogbookAppendDisposition Disposition,
    LogbookEntry Entry);

public interface ILogbookIdempotencySource
{
    Task<LogbookEntry?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public interface ILogbookWriter
{
    Task<LogbookAppendResult> TryAppendAsync(
        LogbookEntry entry,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
