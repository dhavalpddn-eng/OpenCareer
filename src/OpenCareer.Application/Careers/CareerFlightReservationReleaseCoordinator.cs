using OpenCareer.Application.Fleet;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Careers;

public enum CareerFlightReservationReleaseStatus
{
    Released = 0,
    AlreadyReleased = 1
}

public sealed record CareerFlightReservationReleaseResult(
    CareerFlightReservationReleaseStatus Status,
    string ReservationId,
    string? CanonicalAircraftId);

public sealed class CareerFlightReservationReleaseCoordinator
{
    private readonly IAircraftReservationLookup _reservationLookup;
    private readonly IAircraftReservationStore _reservationStore;
    private readonly SemaphoreSlim _gate =
        new(1, 1);

    public CareerFlightReservationReleaseCoordinator(
        IAircraftReservationLookup reservationLookup,
        IAircraftReservationStore reservationStore)
    {
        _reservationLookup =
            reservationLookup
            ?? throw new ArgumentNullException(nameof(reservationLookup));
        _reservationStore =
            reservationStore
            ?? throw new ArgumentNullException(nameof(reservationStore));
    }

    public async Task<CareerFlightReservationReleaseResult> ReleaseAsync(
        LogbookAppendResult logbookResult,
        PlayerCareerProfileStoreRecord appliedProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logbookResult);
        ArgumentNullException.ThrowIfNull(logbookResult.Entry);
        ArgumentNullException.ThrowIfNull(appliedProfile);

        appliedProfile.Validate();

        LogbookEntry entry =
            logbookResult.Entry;

        LogbookEntry.Commit(
            entry.EntryId,
            entry.Debrief,
            entry.CommittedAt,
            entry.CommitKind);

        Guid contractId =
            ValidateAppliedCareerEntry(
                entry,
                appliedProfile);

        string expectedSettlementKey =
            ContractSettlementEngine.GetIdempotencyKey(
                contractId);

        if (!string.Equals(
                entry.Debrief.Settlement.IdempotencyKey,
                expectedSettlementKey,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Committed career logbook settlement identity does not match its contract.");
        }

        string reservationId =
            JobAcceptanceFleetBridge.GetReservationId(
                contractId);

        await _gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            AircraftReservationOwnership? ownership =
                await _reservationLookup
                    .FindByReservationIdAsync(
                        reservationId,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (ownership is null)
            {
                return new(
                    CareerFlightReservationReleaseStatus.AlreadyReleased,
                    reservationId,
                    CanonicalAircraftId:
                        null);
            }

            ownership.Validate();

            if (!string.Equals(
                    ownership.ReservationId,
                    reservationId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Fleet reservation lookup returned a different reservation identity.");
            }

            AircraftReservationReleaseResult released =
                await _reservationStore
                    .ReleaseReservationAsync(
                        ownership.CanonicalAircraftId,
                        reservationId,
                        cancellationToken)
                    .ConfigureAwait(false);

            return released switch
            {
                AircraftReservationReleaseResult.Released =>
                    new(
                        CareerFlightReservationReleaseStatus.Released,
                        reservationId,
                        ownership.CanonicalAircraftId),

                AircraftReservationReleaseResult.AlreadyReleased =>
                    new(
                        CareerFlightReservationReleaseStatus.AlreadyReleased,
                        reservationId,
                        ownership.CanonicalAircraftId),

                AircraftReservationReleaseResult.NotReserved =>
                    throw new InvalidOperationException(
                        "The contract-owned aircraft is unavailable for a non-reservation reason."),

                AircraftReservationReleaseResult.HeldByAnotherReservation =>
                    throw new InvalidOperationException(
                        "The aircraft reservation changed ownership before career completion could release it."),

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(released),
                        released,
                        "Unknown Fleet reservation release result.")
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Guid ValidateAppliedCareerEntry(
        LogbookEntry entry,
        PlayerCareerProfileStoreRecord appliedProfile)
    {
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
                "Fleet release requires a settled automatic career-job logbook entry.");
        }

        if (!appliedProfile.Profile
                .AppliedExperienceDebriefIds
                .Contains(entry.Debrief.DebriefId))
        {
            throw new InvalidOperationException(
                "Fleet reservation cannot release before the committed debrief is applied to Career/Profile experience.");
        }

        if (appliedProfile.SavedAt
            < entry.CommittedAt)
        {
            throw new InvalidOperationException(
                "Applied Career/Profile state cannot predate the authoritative logbook commit.");
        }

        return contractId;
    }
}
