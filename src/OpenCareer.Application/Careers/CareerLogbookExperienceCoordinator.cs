using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Careers;

public sealed class CareerLogbookExperienceCoordinator
{
    private readonly PlayerCareerExperienceCoordinator _experience;

    public CareerLogbookExperienceCoordinator(
        PlayerCareerExperienceCoordinator experience)
    {
        _experience =
            experience
            ?? throw new ArgumentNullException(nameof(experience));
    }

    public async Task<PlayerCareerProfileStoreRecord> ApplyAsync(
        LogbookAppendResult logbookResult,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logbookResult);
        ArgumentNullException.ThrowIfNull(logbookResult.Entry);

        LogbookEntry entry =
            logbookResult.Entry;

        LogbookEntry.Commit(
            entry.EntryId,
            entry.Debrief,
            entry.CommittedAt,
            entry.CommitKind);

        if (entry.CommitKind
                != LogbookCommitKind.AutomaticCareerSettlement
            || entry.Debrief.EntryKind
                != LogbookEntryKind.CareerJob
            || entry.Debrief.ContractId is null
            || entry.Debrief.Settlement.Status
                != SettlementRecordStatus.Settled)
        {
            throw new InvalidOperationException(
                "Only a settled automatic career-job logbook entry can apply player career experience.");
        }

        if (savedAt
            < entry.CommittedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(savedAt),
                "Career experience cannot be saved before the authoritative logbook commit.");
        }

        return await _experience
            .ApplyCommittedAsync(
                entry,
                savedAt,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
