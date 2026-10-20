using OpenCareer.Application.Fleet;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed record AcceptedJobCancellationResult(
    Guid ContractId,
    string ReservationId,
    bool ReservationWasAlreadyReleased);

public interface IAcceptedJobCancellationService
{
    Task<AcceptedJobCancellationResult> CancelAsync(
        Guid contractId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Cancels an accepted job before flight start, retires its recoverable runtime
/// state, releases its exact Fleet reservation, and clears any prepared plan.
/// Re-entry after a partial cleanup is idempotent.
/// </summary>
public sealed class AcceptedJobCancellationService(
    IJobContractStore contracts,
    JobContractLifecycleService lifecycle,
    JobContractRuntimeState runtime,
    IAircraftReservationLookup reservationLookup,
    IAircraftReservationStore reservationStore,
    IPreparedDispatchPlanStore preparedPlans)
    : IAcceptedJobCancellationService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AcceptedJobCancellationResult> CancelAsync(
        Guid contractId,
        CancellationToken cancellationToken = default)
    {
        if (contractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(contractId));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PersistedJobContract persisted = await contracts
                .ReadJobContractAsync(contractId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Accepted job contract was not found.");

            persisted.Validate();
            if (persisted.Contract.Status == ContractStatus.Accepted)
            {
                persisted = await lifecycle
                    .CancelAsync(contractId, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (persisted.Contract.Status != ContractStatus.Cancelled)
            {
                throw new InvalidOperationException(
                    $"Only an accepted preflight job can be cancelled here; current state is {persisted.Contract.Status}.");
            }

            await runtime
                .RemoveCancelledAsync(persisted, cancellationToken)
                .ConfigureAwait(false);

            string reservationId = JobAcceptanceFleetBridge.GetReservationId(contractId);
            AircraftReservationOwnership? ownership = await reservationLookup
                .FindByReservationIdAsync(reservationId, cancellationToken)
                .ConfigureAwait(false);

            bool alreadyReleased = ownership is null;
            if (ownership is not null)
            {
                ownership.Validate();
                if (!string.Equals(
                        ownership.ReservationId,
                        reservationId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Fleet reservation lookup returned a different reservation identity.");
                }

                AircraftReservationReleaseResult release = await reservationStore
                    .ReleaseReservationAsync(
                        ownership.CanonicalAircraftId,
                        reservationId,
                        cancellationToken)
                    .ConfigureAwait(false);

                alreadyReleased = release == AircraftReservationReleaseResult.AlreadyReleased;
                if (release is not
                    (AircraftReservationReleaseResult.Released
                    or AircraftReservationReleaseResult.AlreadyReleased))
                {
                    throw new InvalidOperationException(
                        release == AircraftReservationReleaseResult.HeldByAnotherReservation
                            ? "The aircraft reservation changed ownership before cancellation could release it."
                            : "The contract-owned aircraft is unavailable for a non-reservation reason.");
                }
            }

            await preparedPlans
                .ClearAsync(contractId, cancellationToken)
                .ConfigureAwait(false);

            return new(contractId, reservationId, alreadyReleased);
        }
        finally
        {
            _gate.Release();
        }
    }
}
