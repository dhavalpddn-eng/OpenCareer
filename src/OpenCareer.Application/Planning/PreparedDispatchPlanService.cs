using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Planning;

public sealed record PreparedDispatchPlan(
    Guid ContractId,
    long ContractVersion,
    string CanonicalAircraftId,
    string ReservationId,
    string OriginIcao,
    string DestinationIcao,
    CruisePerformancePlanningRequest Cruise,
    RouteFuelPlanningRequest RouteFuel,
    OperationDispatchRequirements Requirements,
    RouteFuelWeightPlan FuelWeightPlan,
    string OriginRunwayIdentifier,
    string DestinationRunwayIdentifier,
    DateTimeOffset PreparedAt)
{
    public void Validate()
    {
        if (ContractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(ContractId));
        if (ContractVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(ContractVersion));

        ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalAircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ReservationId);
        ValidateIcao(OriginIcao, nameof(OriginIcao));
        ValidateIcao(DestinationIcao, nameof(DestinationIcao));
        ArgumentNullException.ThrowIfNull(Cruise);
        ArgumentNullException.ThrowIfNull(RouteFuel);
        ArgumentNullException.ThrowIfNull(Requirements);
        ArgumentNullException.ThrowIfNull(FuelWeightPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(OriginRunwayIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(DestinationRunwayIdentifier);

        Cruise.Validate();
        RouteFuel.Validate();
        Requirements.Validate();
        FuelWeightPlan.Validate();

        if (PreparedAt == default
            || Requirements.PlannedFuelPounds is not { } plannedFuel
            || Requirements.PlannedFuelTypeIndex != FuelWeightPlan.FuelTypeIndex
            || Math.Abs(plannedFuel - FuelWeightPlan.RequiredFuelPounds) > 1e-9
            || Math.Abs(
                Requirements.RequiredRangeNauticalMiles
                - RouteFuel.TotalRouteDistanceNauticalMiles) > 1e-9)
        {
            throw new ArgumentException(
                "Prepared dispatch plan identity, time, route, or fuel evidence is inconsistent.");
        }
    }

    private static void ValidateIcao(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 4
            || value.Any(static character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "A normalized four-letter ICAO identifier is required.",
                name);
        }
    }
}

public enum PreparedDispatchPlanSaveResult
{
    Created = 0,
    Replaced,
    AlreadySaved
}

public interface IPreparedDispatchPlanStore
{
    Task<PreparedDispatchPlan?> ReadAsync(
        Guid contractId,
        CancellationToken cancellationToken = default);

    Task<PreparedDispatchPlanSaveResult> SaveAsync(
        PreparedDispatchPlan plan,
        CancellationToken cancellationToken = default);

    Task ClearAsync(
        Guid contractId,
        CancellationToken cancellationToken = default);
}

public sealed record PrepareDispatchPlanRequest(
    Guid ContractId,
    string CanonicalAircraftId,
    string ReservationId,
    string OriginIcao,
    string DestinationIcao,
    CruisePerformancePlanningRequest Cruise,
    RouteFuelPlanningRequest RouteFuel,
    OperationDispatchRequirements Requirements)
{
    public void Validate()
    {
        if (ContractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(ContractId));

        ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalAircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ReservationId);
        ValidateIcao(OriginIcao, nameof(OriginIcao));
        ValidateIcao(DestinationIcao, nameof(DestinationIcao));
        ArgumentNullException.ThrowIfNull(Cruise);
        ArgumentNullException.ThrowIfNull(RouteFuel);
        ArgumentNullException.ThrowIfNull(Requirements);

        Cruise.Validate();
        RouteFuel.Validate();
        Requirements.Validate();

        if (Requirements.PlannedFuelPounds is not null
            || Requirements.PlannedFuelTypeIndex is not null)
        {
            throw new ArgumentException(
                "Prepared dispatch input cannot supply planner-owned fuel fields.",
                nameof(Requirements));
        }

        if (Math.Abs(
                Requirements.RequiredRangeNauticalMiles
                - RouteFuel.TotalRouteDistanceNauticalMiles) > 1e-9)
        {
            throw new ArgumentException(
                "Dispatch requirement range must match the route-fuel distance.",
                nameof(Requirements));
        }
    }

    private static void ValidateIcao(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 4
            || value.Any(static character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "A normalized four-letter ICAO identifier is required.",
                name);
        }
    }
}

public enum PrepareDispatchPlanStatus
{
    Prepared = 0,
    AlreadyPrepared,
    ContractUnavailable,
    ContractNotAccepted,
    ContractTermsUnavailable,
    ContractMismatch,
    ReservationUnavailable,
    ReservationMismatch,
    PlanningInsufficient,
    PlanningInfeasible,
    AuthorityChanged
}

public sealed record PrepareDispatchPlanResult(
    PrepareDispatchPlanStatus Status,
    string Detail,
    PreparedDispatchPlan? Plan = null)
{
    public bool IsPrepared =>
        Status is PrepareDispatchPlanStatus.Prepared
            or PrepareDispatchPlanStatus.AlreadyPrepared;
}

/// <summary>
/// Re-evaluates and persists one exact Accepted-contract dispatch plan. This
/// service prepares no FlightSession and grants no permission to start; a later
/// consumer must recheck the contract, reservation, and current physical data.
/// </summary>
public sealed class PreparedDispatchPlanService(
    IJobContractStore contracts,
    IAircraftReservationLookup reservations,
    ICompositeDispatchPlanningService planning,
    IPreparedDispatchPlanStore plans,
    TimeProvider timeProvider)
{
    private readonly IJobContractStore _contracts =
        contracts ?? throw new ArgumentNullException(nameof(contracts));
    private readonly IAircraftReservationLookup _reservations =
        reservations ?? throw new ArgumentNullException(nameof(reservations));
    private readonly ICompositeDispatchPlanningService _planning =
        planning ?? throw new ArgumentNullException(nameof(planning));
    private readonly IPreparedDispatchPlanStore _plans =
        plans ?? throw new ArgumentNullException(nameof(plans));
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<PrepareDispatchPlanResult> PrepareAsync(
        PrepareDispatchPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ContractAuthority initial = await ReadAuthorityAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            if (initial.Blocked is not null)
                return initial.Blocked;

            PersistedJobContract initialContract = initial.Contract
                ?? throw new InvalidDataException(
                    "Unblocked dispatch authority did not retain its contract.");

            CompositeDispatchPlanningResult evaluated = await _planning
                .EvaluateAsync(
                    request.CanonicalAircraftId,
                    request.OriginIcao,
                    request.DestinationIcao,
                    request.Cruise,
                    request.RouteFuel,
                    request.Requirements,
                    request.ReservationId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (evaluated.Status != CompositeDispatchPlanningStatus.Evaluated
                || evaluated.FuelWeightPlan is null
                || evaluated.DispatchResult is null)
            {
                return new(
                    PrepareDispatchPlanStatus.PlanningInsufficient,
                    "Authoritative fuel, performance, airport, runway, or weather evidence is insufficient; no plan was persisted.");
            }

            if (evaluated.DispatchResult.Status
                == DispatchFeasibilityStatus.InsufficientData)
            {
                return new(
                    PrepareDispatchPlanStatus.PlanningInsufficient,
                    "The current physical dispatch evaluation has insufficient authoritative data; no plan was persisted.");
            }

            if (evaluated.DispatchResult.Status
                == DispatchFeasibilityStatus.Infeasible)
            {
                return new(
                    PrepareDispatchPlanStatus.PlanningInfeasible,
                    "The current composite dispatch evaluation is not feasible; no plan was persisted.");
            }

            ContractAuthority current = await ReadAuthorityAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            if (current.Blocked is not null
                || current.Contract is not { } currentContract
                || currentContract.Version != initialContract.Version)
            {
                return new(
                    PrepareDispatchPlanStatus.AuthorityChanged,
                    "The accepted contract or Fleet reservation changed during evaluation; no plan was persisted.");
            }

            string originRunway = evaluated.DispatchResult.OriginRunwayIdentifier
                ?? throw new InvalidDataException(
                    "A feasible dispatch evaluation did not identify an origin runway.");
            string destinationRunway = evaluated.DispatchResult.DestinationRunwayIdentifier
                ?? throw new InvalidDataException(
                    "A feasible dispatch evaluation did not identify a destination runway.");

            var candidate = new PreparedDispatchPlan(
                request.ContractId,
                currentContract.Version,
                request.CanonicalAircraftId,
                request.ReservationId,
                request.OriginIcao,
                request.DestinationIcao,
                request.Cruise,
                request.RouteFuel,
                evaluated.FuelWeightPlan.ApplyTo(request.Requirements),
                evaluated.FuelWeightPlan,
                originRunway,
                destinationRunway,
                _timeProvider.GetUtcNow());
            candidate.Validate();

            PreparedDispatchPlan? existing = await _plans
                .ReadAsync(request.ContractId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null
                && (existing with { PreparedAt = candidate.PreparedAt }) == candidate)
            {
                candidate = existing;
            }

            PreparedDispatchPlanSaveResult saved = await _plans
                .SaveAsync(candidate, cancellationToken)
                .ConfigureAwait(false);

            return new(
                saved == PreparedDispatchPlanSaveResult.AlreadySaved
                    ? PrepareDispatchPlanStatus.AlreadyPrepared
                    : PrepareDispatchPlanStatus.Prepared,
                saved == PreparedDispatchPlanSaveResult.AlreadySaved
                    ? "The exact feasible dispatch plan was already prepared."
                    : "The exact feasible dispatch plan was persisted. Flight start remains a separate authority.",
                candidate);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ContractAuthority> ReadAuthorityAsync(
        PrepareDispatchPlanRequest request,
        CancellationToken cancellationToken)
    {
        PersistedJobContract? persisted = await _contracts
            .ReadJobContractAsync(request.ContractId, cancellationToken)
            .ConfigureAwait(false);

        if (persisted is null)
        {
            return new(null, new(
                PrepareDispatchPlanStatus.ContractUnavailable,
                "The accepted contract is unavailable."));
        }

        persisted.Validate();
        JobContract contract = persisted.Contract;
        if (contract.ContractId != request.ContractId)
        {
            return new(null, new(
                PrepareDispatchPlanStatus.ContractMismatch,
                "The contract store returned a different contract identity."));
        }

        if (contract.Status != ContractStatus.Accepted)
        {
            return new(null, new(
                PrepareDispatchPlanStatus.ContractNotAccepted,
                $"The contract is {contract.Status}, not Accepted."));
        }

        if (contract.PlanningTerms is not { } terms)
        {
            return new(null, new(
                PrepareDispatchPlanStatus.ContractTermsUnavailable,
                "The accepted contract has no authoritative payload and route-distance terms."));
        }

        string expectedReservation = JobAcceptanceFleetBridge.GetReservationId(
            contract.ContractId);
        if (!string.Equals(
                request.ReservationId,
                expectedReservation,
                StringComparison.Ordinal))
        {
            return new(null, new(
                PrepareDispatchPlanStatus.ReservationMismatch,
                "The supplied Fleet reservation does not belong to the accepted contract."));
        }

        if (!string.Equals(request.OriginIcao, contract.OriginIcao, StringComparison.Ordinal)
            || !string.Equals(request.DestinationIcao, contract.DestinationIcao, StringComparison.Ordinal)
            || Math.Abs(request.Requirements.PayloadPounds - terms.PayloadPounds) > 1e-9
            || Math.Abs(request.Requirements.RequiredRangeNauticalMiles - terms.RouteDistanceNauticalMiles) > 1e-9
            || Math.Abs(request.RouteFuel.TotalRouteDistanceNauticalMiles - terms.RouteDistanceNauticalMiles) > 1e-9)
        {
            return new(null, new(
                PrepareDispatchPlanStatus.ContractMismatch,
                "The supplied route, payload, or distance does not match the immutable accepted contract."));
        }

        AircraftReservationOwnership? ownership = await _reservations
            .FindByReservationIdAsync(request.ReservationId, cancellationToken)
            .ConfigureAwait(false);

        if (ownership is null)
        {
            return new(null, new(
                PrepareDispatchPlanStatus.ReservationUnavailable,
                "The accepted contract's Fleet reservation is unavailable."));
        }

        ownership.Validate();
        if (!string.Equals(ownership.ReservationId, request.ReservationId, StringComparison.Ordinal)
            || !string.Equals(ownership.CanonicalAircraftId, request.CanonicalAircraftId, StringComparison.OrdinalIgnoreCase))
        {
            return new(null, new(
                PrepareDispatchPlanStatus.ReservationMismatch,
                "The Fleet reservation does not belong to the supplied aircraft and contract."));
        }

        return new(persisted, null);
    }

    private sealed record ContractAuthority(
        PersistedJobContract? Contract,
        PrepareDispatchPlanResult? Blocked);
}
