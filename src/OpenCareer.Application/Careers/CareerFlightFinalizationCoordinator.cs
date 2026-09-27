using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Careers;

public enum CareerFlightFinalizationStatus
{
    Finalized = 0,
    AlreadyFinalized = 1
}

public sealed record CareerFlightFinalizationResult(
    CareerFlightFinalizationStatus Status,
    CareerFlightReservationReleaseResult ReservationRelease);

public sealed class CareerFlightFinalizationCoordinator
{
    private readonly CareerFlightReservationReleaseCoordinator _reservationRelease;
    private readonly FlightSessionPersistenceService _flightPersistence;
    private readonly FlightSessionCoordinator _flightSessions;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CareerFlightFinalizationCoordinator(
        CareerFlightReservationReleaseCoordinator reservationRelease,
        FlightSessionPersistenceService flightPersistence,
        FlightSessionCoordinator flightSessions)
    {
        _reservationRelease =
            reservationRelease
            ?? throw new ArgumentNullException(nameof(reservationRelease));
        _flightPersistence =
            flightPersistence
            ?? throw new ArgumentNullException(nameof(flightPersistence));
        _flightSessions =
            flightSessions
            ?? throw new ArgumentNullException(nameof(flightSessions));
    }

    public async Task<CareerFlightFinalizationResult> FinalizeAsync(
        LogbookAppendResult logbookResult,
        PlayerCareerProfileStoreRecord appliedProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logbookResult);
        ArgumentNullException.ThrowIfNull(logbookResult.Entry);
        ArgumentNullException.ThrowIfNull(appliedProfile);

        LogbookEntry entry =
            logbookResult.Entry;

        Guid contractId =
            ValidateCareerEntry(entry);

        await _gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            FlightSession? beforeRelease =
                _flightSessions.Current;

            if (beforeRelease is not null)
            {
                ValidateMatchingCompletedSession(
                    beforeRelease,
                    entry,
                    contractId);
            }

            CareerFlightReservationReleaseResult release =
                await _reservationRelease
                    .ReleaseAsync(
                        logbookResult,
                        appliedProfile,
                        cancellationToken)
                    .ConfigureAwait(false);

            FlightSession? current =
                _flightSessions.Current;

            if (current is null)
            {
                return new(
                    CareerFlightFinalizationStatus.AlreadyFinalized,
                    release);
            }

            ValidateMatchingCompletedSession(
                current,
                entry,
                contractId);

            await _flightPersistence
                .ClearTerminalAsync(cancellationToken)
                .ConfigureAwait(false);

            return new(
                CareerFlightFinalizationStatus.Finalized,
                release);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Guid ValidateCareerEntry(
        LogbookEntry entry)
    {
        LogbookEntry.Commit(
            entry.EntryId,
            entry.Debrief,
            entry.CommittedAt,
            entry.CommitKind);

        if (entry.CommitKind
                != LogbookCommitKind.AutomaticCareerSettlement
            || entry.Debrief.EntryKind
                != LogbookEntryKind.CareerJob
            || entry.Debrief.ContractId
                is not { } contractId
            || entry.Debrief.Settlement.Status
                != SettlementRecordStatus.Settled)
        {
            throw new InvalidOperationException(
                "Career flight finalization requires a settled automatic career-job logbook entry.");
        }

        return contractId;
    }

    private static void ValidateMatchingCompletedSession(
        FlightSession session,
        LogbookEntry entry,
        Guid contractId)
    {
        if (session.SessionId
                != entry.Debrief.SessionId
            || session.ContractId
                != contractId)
        {
            throw new InvalidOperationException(
                "Current FlightSession does not match the settled career logbook entry.");
        }

        if (session.Status
                != FlightSessionStatus.Completed
            || session.OperationState
                != FlightOperationState.Complete
            || session.Tracking.State
                != FlightTrackingState.Complete
            || session.Tracking.CrashReported)
        {
            throw new InvalidOperationException(
                "Only the matching successfully completed career FlightSession can be finalized.");
        }
    }
}
