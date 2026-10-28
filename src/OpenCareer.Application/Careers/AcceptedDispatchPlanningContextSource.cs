using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public enum AcceptedDispatchPlanningContextStatus
{
    Ready = 0,
    ContractsUnavailable,
    NoAcceptedContract,
    MultipleAcceptedContracts,
    ReservationUnavailable,
    CancellationCleanupPending
}

public sealed record AcceptedDispatchPlanningContext(
    AcceptedDispatchPlanningContextStatus Status,
    string Detail,
    Guid? ContractId = null,
    ContractKind? Kind = null,
    string? OriginIcao = null,
    string? DestinationIcao = null,
    string? CanonicalAircraftId = null,
    string? ReservationId = null,
    double? RouteDistanceNauticalMiles = null,
    double? PayloadPounds = null,
    IReadOnlyList<Guid>? AcceptedContractIds = null);

public interface IAcceptedDispatchPlanningContextSource
{
    Task<AcceptedDispatchPlanningContext> ReadAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the one persisted Accepted contract and its exact Fleet reservation.
/// New contracts may persist immutable payload and route distance. Legacy
/// contracts and all fuel/performance inputs remain explicitly absent.
/// </summary>
public sealed class AcceptedDispatchPlanningContextSource(
    IJobContractRuntimeSource contracts,
    IAircraftReservationLookup reservations,
    IAcceptedJobCancellationRecoverySource? cancellationRecovery = null)
    : IAcceptedDispatchPlanningContextSource
{
    private readonly IJobContractRuntimeSource _contracts =
        contracts ?? throw new ArgumentNullException(nameof(contracts));
    private readonly IAircraftReservationLookup _reservations =
        reservations ?? throw new ArgumentNullException(nameof(reservations));
    private readonly IAcceptedJobCancellationRecoverySource? _cancellationRecovery =
        cancellationRecovery;

    public async Task<AcceptedDispatchPlanningContext> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_contracts.IsInitialized)
        {
            return new(
                AcceptedDispatchPlanningContextStatus.ContractsUnavailable,
                "Accepted-contract recovery is not initialized. Dispatch will not infer an operation.");
        }

        if (_cancellationRecovery is not null)
        {
            IReadOnlyList<Guid> pending = await _cancellationRecovery
                .ReadPendingContractIdsAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Accepted-job cancellation recovery returned no contract collection.");

            Guid[] pendingIds = pending
                .Where(static contractId => contractId != Guid.Empty)
                .Distinct()
                .OrderBy(static contractId => contractId)
                .ToArray();

            if (pendingIds.Length != pending.Count)
            {
                throw new InvalidDataException(
                    "Accepted-job cancellation recovery returned invalid or duplicate contract identities.");
            }

            if (pendingIds.Length > 0)
            {
                return new(
                    AcceptedDispatchPlanningContextStatus.CancellationCleanupPending,
                    pendingIds.Length == 1
                        ? "A cancelled job still has preflight cleanup pending. Retry cleanup before planning or starting another job."
                        : $"{pendingIds.Length} cancelled jobs still have preflight cleanup pending. Retry cleanup before planning or starting another job.",
                    ContractId: pendingIds.Length == 1
                        ? pendingIds[0]
                        : null,
                    AcceptedContractIds: pendingIds);
            }
        }

        PersistedJobContract[] accepted = _contracts.Current
            .Where(static item => item.Contract.Status == ContractStatus.Accepted)
            .OrderBy(static item => item.Contract.ContractId)
            .ToArray();

        if (accepted.Length == 0)
        {
            return new(
                AcceptedDispatchPlanningContextStatus.NoAcceptedContract,
                "No accepted job is waiting for dispatch preparation.");
        }

        if (accepted.Length > 1)
        {
            return new(
                AcceptedDispatchPlanningContextStatus.MultipleAcceptedContracts,
                $"{accepted.Length} accepted jobs are present. Dispatch will not choose between them; cancel them to clear the blocked preflight state.",
                AcceptedContractIds: accepted
                    .Select(static item => item.Contract.ContractId)
                    .ToArray());
        }

        JobContract contract = accepted[0].Contract;
        contract.Validate();
        string reservationId = JobAcceptanceFleetBridge.GetReservationId(
            contract.ContractId);
        AircraftReservationOwnership? ownership = await _reservations
            .FindByReservationIdAsync(reservationId, cancellationToken)
            .ConfigureAwait(false);

        if (ownership is null)
        {
            return new(
                AcceptedDispatchPlanningContextStatus.ReservationUnavailable,
                "The accepted job has no matching Fleet reservation. Aircraft was not preselected.",
                contract.ContractId,
                contract.Kind,
                contract.OriginIcao,
                contract.DestinationIcao,
                ReservationId: reservationId,
                RouteDistanceNauticalMiles:
                    contract.PlanningTerms?.RouteDistanceNauticalMiles,
                PayloadPounds:
                    contract.PlanningTerms?.PayloadPounds);
        }

        ownership.Validate();
        if (!string.Equals(
                ownership.ReservationId,
                reservationId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Fleet reservation lookup returned a different reservation identity.");
        }

        return new(
            AcceptedDispatchPlanningContextStatus.Ready,
            contract.PlanningTerms is null
                ? "Accepted legacy job route and reserved aircraft loaded. Payload and distance were not persisted, so the reservation cannot prepare a dispatch plan."
                : "Accepted job route, payload, distance and reserved aircraft loaded and locked to this contract. Cruise, fuel, reserve and safety-margin inputs remain explicit.",
            contract.ContractId,
            contract.Kind,
            contract.OriginIcao,
            contract.DestinationIcao,
            ownership.CanonicalAircraftId,
            reservationId,
            contract.PlanningTerms?.RouteDistanceNauticalMiles,
            contract.PlanningTerms?.PayloadPounds,
            [contract.ContractId]);
    }
}
