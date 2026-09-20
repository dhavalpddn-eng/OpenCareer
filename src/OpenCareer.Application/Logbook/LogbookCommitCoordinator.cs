using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Logbook;

public sealed class LogbookCommitCoordinator
{
    private readonly ILogbookWriter _writer;

    public LogbookCommitCoordinator(ILogbookWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public async Task<LogbookAppendResult> CommitAsync(
        Guid entryId,
        FlightDebrief debrief,
        DateTimeOffset committedAt,
        LogbookCommitKind commitKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(debrief);

        LogbookEntry entry = LogbookEntry.Commit(
            entryId,
            debrief,
            committedAt,
            commitKind);

        string idempotencyKey = BuildIdempotencyKey(debrief, commitKind);

        LogbookAppendResult result = await _writer
            .TryAppendAsync(entry, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(result.Entry);

        if (result.Entry.Debrief.DebriefId != debrief.DebriefId)
        {
            throw new InvalidOperationException(
                "Logbook writer returned an entry for a different debrief.");
        }

        return result;
    }

    public static string BuildIdempotencyKey(
        FlightDebrief debrief,
        LogbookCommitKind commitKind)
    {
        ArgumentNullException.ThrowIfNull(debrief);

        return commitKind switch
        {
            LogbookCommitKind.AutomaticCareerSettlement =>
                debrief.Settlement.Status == SettlementRecordStatus.Settled &&
                !string.IsNullOrWhiteSpace(debrief.Settlement.IdempotencyKey)
                    ? $"career:{debrief.Settlement.IdempotencyKey}"
                    : throw new InvalidOperationException(
                        "Career logbook commits require a settled contract idempotency key."),

            LogbookCommitKind.ManualPilotLog =>
                debrief.ContractId is null
                    ? $"manual:{debrief.DebriefId:N}"
                    : throw new InvalidOperationException(
                        "Contract flights cannot use the manual logbook idempotency key."),

            _ => throw new ArgumentOutOfRangeException(nameof(commitKind))
        };
    }
}
