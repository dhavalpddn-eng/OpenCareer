using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public enum AcceptedDispatchPlanningContextStatus
{
    Ready = 0,
    ContractsUnavailable,
    NoAcceptedContract,
    MultipleAcceptedContracts,
    ReservationUnavailable
}

public sealed record AcceptedDispatchPlanningContext(
    AcceptedDispatchPlanningContextStatus Status,
    string Detail,
    Guid? ContractId = null,
    ContractKind? Kind = null,
    string? OriginIcao = null,
    string? DestinationIcao = null,
    string? CanonicalAircraftId = null,
    string? ReservationId = null);

public interface IAcceptedDispatchPlanningContextSource
{
    Task<AcceptedDispatchPlanningContext> ReadAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the one persisted Accepted contract and its exact Fleet reservation.
/// The contract does not persist payload, distance or fuel-planning inputs, so
/// this source deliberately projects none of those values.
/// </summary>
public sealed class AcceptedDispatchPlanningContextSource(
    IJobContractRuntimeSource contracts,
    IAircraftReservationLookup reservations)
    : IAcceptedDispatchPlanningContextSource
{
    private readonly IJobContractRuntimeSource _contracts =
        contracts ?? throw new ArgumentNullException(nameof(contracts));
    private readonly IAircraftReservationLookup _reservations =
        reservations ?? throw new ArgumentNullException(nameof(reservations));

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
                "Multiple accepted jobs are present. Dispatch will not choose between them.");
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
                ReservationId: reservationId);
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
            "Accepted job route and reserved aircraft loaded. Payload, distance, cruise, fuel and reserve inputs remain explicit because the contract does not persist those planning values.",
            contract.ContractId,
            contract.Kind,
            contract.OriginIcao,
            contract.DestinationIcao,
            ownership.CanonicalAircraftId,
            reservationId);
    }
}
