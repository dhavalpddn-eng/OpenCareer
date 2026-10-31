using OpenCareer.Application.Economy;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Logbook;

public sealed record SettledJobLogbookContext(
    AircraftDebrief Aircraft,
    string? ActualDeparture,
    string? ActualArrival,
    string? DiversionLocation,
    PayloadDebrief Payload,
    FlightSafetyOutcome SafetyOutcome,
    MissionOutcome MissionOutcome,
    double ReputationDelta,
    IReadOnlyList<FlightDebriefEvent>? Events = null,
    bool PositionJumpObserved = false);

public sealed class SettledJobLogbookCoordinator
{
    private readonly FlightSessionCoordinator _flightSessions;
    private readonly LogbookCommitCoordinator _logbook;

    public SettledJobLogbookCoordinator(
        FlightSessionCoordinator flightSessions,
        LogbookCommitCoordinator logbook)
    {
        _flightSessions =
            flightSessions
            ?? throw new ArgumentNullException(nameof(flightSessions));
        _logbook =
            logbook
            ?? throw new ArgumentNullException(nameof(logbook));
    }

    public async Task<LogbookAppendResult> CommitAsync(
        EconomySettlementResult settlementResult,
        SettledJobLogbookContext context,
        DateTimeOffset committedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settlementResult);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Aircraft);
        ArgumentNullException.ThrowIfNull(context.Payload);

        settlementResult.PersistedContract.Validate();
        settlementResult.Settlement.Validate();
        settlementResult.Settlement.Transaction.Validate();
        context.Aircraft.Validate();
        context.Payload.Validate();

        if (!double.IsFinite(context.ReputationDelta))
        {
            throw new ArgumentOutOfRangeException(
                nameof(context),
                "Reputation delta must be finite.");
        }

        JobContract contract =
            settlementResult.PersistedContract.Contract;

        if (contract.Status
                != ContractStatus.Completed
            || contract.CompletedAt is null)
        {
            throw new InvalidOperationException(
                "Automatic career logbook commit requires a completed persisted contract.");
        }

        Guid contractId =
            contract.ContractId;

        string expectedSettlementKey =
            ContractSettlementEngine.GetIdempotencyKey(
                contractId);

        if (settlementResult.Settlement.ContractId
                != contractId
            || settlementResult.Settlement.Transaction.TransactionId
                != contractId
            || !string.Equals(
                settlementResult.Settlement.Transaction.IdempotencyKey,
                expectedSettlementKey,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Economy settlement does not match the completed contract.");
        }

        FlightSession session =
            _flightSessions.Current
            ?? throw new InvalidOperationException(
                "No FlightSession exists for the settled career job.");

        if (session.ContractId
                != contractId
            || session.Status
                != FlightSessionStatus.Completed
            || session.OperationState
                != FlightOperationState.Complete
            || session.Tracking.State
                != FlightTrackingState.Complete)
        {
            throw new InvalidOperationException(
                "Settled career logbook commit requires the matching completed FlightSession.");
        }

        if (session.Tracking.CrashReported)
        {
            throw new InvalidOperationException(
                "A crashed FlightSession cannot be committed as a successful settled career job.");
        }

        DateTimeOffset settledAt =
            settlementResult.Settlement.Transaction.OccurredAt;

        DateTimeOffset sessionEndedAt =
            session.Milestones.CompletedAt
            ?? session.UpdatedAt;

        if (settledAt
            < sessionEndedAt)
        {
            throw new InvalidOperationException(
                "Authoritative settlement cannot precede the completed FlightSession.");
        }

        if (committedAt
            < settledAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(committedAt),
                "Career logbook commit cannot precede authoritative settlement.");
        }

        var settlementRecord =
            new FlightSettlementRecord(
                SettlementRecordStatus.Settled,
                expectedSettlementKey,
                settlementResult.Settlement.Transaction.TransactionId
                    .ToString("D"),
                settledAt,
                settlementResult.Settlement.NetCashChange,
                context.ReputationDelta);

        settlementRecord.Validate();

        var debriefContext =
            new FlightSessionDebriefContext(
                LogbookEntryKind.CareerJob,
                context.Aircraft,
                context.ActualDeparture,
                context.ActualArrival,
                context.DiversionLocation,
                context.Payload,
                context.SafetyOutcome,
                context.MissionOutcome,
                settlementRecord,
                context.Events,
                context.PositionJumpObserved);

        FlightDebrief debrief =
            FlightSessionDebriefProjector.Create(
                session,
                debriefContext);

        if (debrief.ContractId
                != contractId
            || debrief.SessionId
                != session.SessionId)
        {
            throw new InvalidOperationException(
                "Projected career debrief identity does not match the settled job flight.");
        }

        return await _logbook
            .CommitAsync(
                session.SessionId,
                debrief,
                committedAt,
                LogbookCommitKind.AutomaticCareerSettlement,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
