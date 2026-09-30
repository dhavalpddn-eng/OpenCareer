using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Careers;

public sealed record ManualFlightPostflightLogRequest(
    FlightSessionDebriefContext DebriefContext,
    DateTimeOffset LogbookCommittedAt,
    DateTimeOffset ExperienceSavedAt);

public sealed record ManualFlightPostflightLogResult(
    LogbookAppendResult Logbook,
    PlayerCareerProfileStoreRecord CareerProfile);

public sealed class ManualFlightPostflightCoordinator
{
    private readonly FlightSessionCoordinator _sessions;
    private readonly FlightSessionPersistenceService _persistence;
    private readonly LogbookCommitCoordinator _logbook;
    private readonly ILogbookIdempotencySource _logbookLookup;
    private readonly PlayerCareerExperienceCoordinator _experience;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ManualFlightPostflightCoordinator(
        FlightSessionCoordinator sessions,
        FlightSessionPersistenceService persistence,
        LogbookCommitCoordinator logbook,
        ILogbookIdempotencySource logbookLookup,
        PlayerCareerExperienceCoordinator experience)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _logbook = logbook ?? throw new ArgumentNullException(nameof(logbook));
        _logbookLookup = logbookLookup ?? throw new ArgumentNullException(nameof(logbookLookup));
        _experience = experience ?? throw new ArgumentNullException(nameof(experience));
    }

    public async Task<ManualFlightPostflightLogResult> LogAsync(
        ManualFlightPostflightLogRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.DebriefContext);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSession session = RequireCompletedManualSession();
            ValidateManualContext(request.DebriefContext);
            FlightDebrief debrief = FlightSessionDebriefProjector.Create(
                session,
                request.DebriefContext);
            string key = LogbookCommitCoordinator.BuildIdempotencyKey(
                debrief,
                LogbookCommitKind.ManualPilotLog);
            LogbookEntry? existing = await _logbookLookup
                .FindByIdempotencyKeyAsync(key, cancellationToken)
                .ConfigureAwait(false);

            LogbookAppendResult logbook = existing is null
                ? await _logbook.CommitAsync(
                    Guid.NewGuid(),
                    debrief,
                    request.LogbookCommittedAt,
                    LogbookCommitKind.ManualPilotLog,
                    cancellationToken).ConfigureAwait(false)
                : new(
                    LogbookAppendDisposition.AlreadyExists,
                    ValidateRecoveredEntry(existing, session));

            PlayerCareerProfileStoreRecord profile = await _experience
                .ApplyCommittedAsync(
                    logbook.Entry,
                    request.ExperienceSavedAt,
                    cancellationToken)
                .ConfigureAwait(false);

            await _persistence.ClearTerminalAsync(
                    session.SessionId,
                    expectedContractId: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new(logbook, profile);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DiscardAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSession session = RequireCompletedManualSession();
            await _persistence.ClearTerminalAsync(
                    session.SessionId,
                    expectedContractId: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private FlightSession RequireCompletedManualSession()
    {
        FlightSession session = _sessions.Current
            ?? throw new InvalidOperationException("No FlightSession exists for manual postflight processing.");

        if (session.ContractId is not null)
            throw new InvalidOperationException("Contract-linked FlightSessions cannot use manual postflight processing.");

        if (session.Status != FlightSessionStatus.Completed
            || session.OperationState != FlightOperationState.Complete
            || session.Tracking.State != FlightTrackingState.Complete
            || session.Tracking.CrashReported)
        {
            throw new InvalidOperationException("Manual postflight processing requires a successfully completed FlightSession.");
        }

        return session;
    }

    private static void ValidateManualContext(FlightSessionDebriefContext context)
    {
        if (context.EntryKind is not LogbookEntryKind.FreeFlight
            and not LogbookEntryKind.Training
            || context.Settlement.Status != SettlementRecordStatus.NotApplicable)
        {
            throw new InvalidOperationException(
                "Manual postflight logging requires free-flight or training evidence without settlement.");
        }
    }

    private static LogbookEntry ValidateRecoveredEntry(
        LogbookEntry entry,
        FlightSession session)
    {
        LogbookEntry.Commit(entry.EntryId, entry.Debrief, entry.CommittedAt, entry.CommitKind);

        if (entry.CommitKind != LogbookCommitKind.ManualPilotLog
            || entry.Debrief.SessionId != session.SessionId
            || entry.Debrief.DebriefId != session.SessionId
            || entry.Debrief.ContractId is not null)
        {
            throw new InvalidOperationException("Recovered manual Logbook entry does not match the completed FlightSession.");
        }

        return entry;
    }
}
