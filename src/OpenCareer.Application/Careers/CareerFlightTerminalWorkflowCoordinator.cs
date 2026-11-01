using OpenCareer.Application.Economy;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Careers;

public sealed record CareerFlightTerminalWorkflowRequest(
    SettlementPendingContractRequest Settlement,
    SettledJobLogbookContext LogbookContext,
    DateTimeOffset LogbookCommittedAt,
    DateTimeOffset ExperienceSavedAt);

public sealed record CareerFlightTerminalWorkflowResult(
    EconomySettlementResult Settlement,
    LogbookAppendResult Logbook,
    PlayerCareerProfileStoreRecord CareerProfile,
    CareerFlightFinalizationResult Finalization);

public sealed class CareerFlightTerminalWorkflowCoordinator
{
    private readonly SettlementPendingContractCoordinator _settlement;
    private readonly SettledJobLogbookCoordinator _logbook;
    private readonly ILogbookIdempotencySource _logbookLookup;
    private readonly CareerLogbookExperienceCoordinator _experience;
    private readonly PlayerCareerLocationCoordinator _location;
    private readonly CareerFlightFinalizationCoordinator _finalization;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CareerFlightTerminalWorkflowCoordinator(
        SettlementPendingContractCoordinator settlement,
        SettledJobLogbookCoordinator logbook,
        ILogbookIdempotencySource logbookLookup,
        CareerLogbookExperienceCoordinator experience,
        PlayerCareerLocationCoordinator location,
        CareerFlightFinalizationCoordinator finalization)
    {
        _settlement =
            settlement
            ?? throw new ArgumentNullException(nameof(settlement));
        _logbook =
            logbook
            ?? throw new ArgumentNullException(nameof(logbook));
        _logbookLookup =
            logbookLookup
            ?? throw new ArgumentNullException(nameof(logbookLookup));
        _experience =
            experience
            ?? throw new ArgumentNullException(nameof(experience));
        _location =
            location
            ?? throw new ArgumentNullException(nameof(location));
        _finalization =
            finalization
            ?? throw new ArgumentNullException(nameof(finalization));
    }

    public async Task<CareerFlightTerminalWorkflowResult> CompleteAsync(
        CareerFlightTerminalWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settlement);
        ArgumentNullException.ThrowIfNull(request.LogbookContext);

        await _gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            EconomySettlementResult settlement =
                await _settlement
                    .SettleAsync(
                        request.Settlement,
                        cancellationToken)
                    .ConfigureAwait(false);

            string careerLogbookKey =
                LogbookCommitCoordinator
                    .BuildCareerSettlementIdempotencyKey(
                        settlement.Settlement.Transaction
                            .IdempotencyKey);

            LogbookEntry? existingEntry =
                await _logbookLookup
                    .FindByIdempotencyKeyAsync(
                        careerLogbookKey,
                        cancellationToken)
                    .ConfigureAwait(false);

            LogbookAppendResult logbook =
                existingEntry is null
                    ? await _logbook
                        .CommitAsync(
                            settlement,
                            request.LogbookContext,
                            request.LogbookCommittedAt,
                            cancellationToken)
                        .ConfigureAwait(false)
                    : new LogbookAppendResult(
                        LogbookAppendDisposition.AlreadyExists,
                        ValidateRecoveredLogbookEntry(
                            existingEntry,
                            settlement));

            PlayerCareerProfileStoreRecord experiencedProfile =
                await _experience
                    .ApplyAsync(
                        logbook,
                        request.ExperienceSavedAt,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!experiencedProfile.Profile
                    .AppliedExperienceDebriefIds
                    .Contains(
                        logbook.Entry.Debrief.DebriefId))
            {
                throw new InvalidOperationException(
                    "Career travel cannot settle before the committed debrief is applied to Career/Profile experience.");
            }

            PlayerCareerProfileStoreRecord careerProfile =
                await _location
                    .ApplyCompletedTravelAsync(
                        settlement.PersistedContract.Contract,
                        request.ExperienceSavedAt,
                        cancellationToken)
                    .ConfigureAwait(false);

            CareerFlightFinalizationResult finalization =
                await _finalization
                    .FinalizeAsync(
                        logbook,
                        careerProfile,
                        cancellationToken)
                    .ConfigureAwait(false);

            return new(
                settlement,
                logbook,
                careerProfile,
                finalization);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static LogbookEntry ValidateRecoveredLogbookEntry(
        LogbookEntry entry,
        EconomySettlementResult settlement)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(settlement);

        LogbookEntry.Commit(
            entry.EntryId,
            entry.Debrief,
            entry.CommittedAt,
            entry.CommitKind);

        Guid contractId =
            settlement.PersistedContract.Contract.ContractId;

        string settlementKey =
            settlement.Settlement.Transaction.IdempotencyKey;

        FlightSettlementRecord record =
            entry.Debrief.Settlement;

        if (entry.CommitKind
                != LogbookCommitKind.AutomaticCareerSettlement
            || entry.Debrief.EntryKind
                != LogbookEntryKind.CareerJob
            || entry.Debrief.ContractId
                != contractId
            || record.Status
                != SettlementRecordStatus.Settled
            || !string.Equals(
                record.IdempotencyKey,
                settlementKey,
                StringComparison.Ordinal)
            || !string.Equals(
                record.TransactionId,
                settlement.Settlement.Transaction.TransactionId
                    .ToString("D"),
                StringComparison.Ordinal)
            || record.SettledAt
                != settlement.Settlement.Transaction.OccurredAt
            || record.CashDelta
                != settlement.Settlement.NetCashChange)
        {
            throw new InvalidOperationException(
                "Recovered career logbook entry does not match the authoritative Economy settlement.");
        }

        return entry;
    }
}
