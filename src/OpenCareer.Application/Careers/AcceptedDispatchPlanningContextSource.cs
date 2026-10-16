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
    string? ReservationId = null,
    double? RouteDistanceNauticalMiles = null,
    double? PayloadPounds = null);

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
                ? "Accepted legacy job route and reserved aircraft loaded. Payload and distance were not persisted; all planning values remain explicit."
                : "Accepted job route, payload, distance and reserved aircraft loaded. Cruise, fuel, reserve and safety-margin inputs remain explicit.",
            contract.ContractId,
            contract.Kind,
            contract.OriginIcao,
            contract.DestinationIcao,
            ownership.CanonicalAircraftId,
            reservationId,
            contract.PlanningTerms?.RouteDistanceNauticalMiles,
            contract.PlanningTerms?.PayloadPounds);
    }
}
