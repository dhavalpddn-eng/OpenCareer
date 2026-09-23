using OpenCareer.Application.Planning;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Careers;

public sealed record AcceptedJobDispatchResult(
    JobAcceptanceFleetResult FleetResult,
    DispatchFeasibilityResult? DispatchResult);

public sealed class AcceptedJobDispatchBridge(
    JobAcceptanceFleetBridge fleetBridge,
    OperationDispatchPlanningService dispatchPlanning)
{
    private readonly JobAcceptanceFleetBridge _fleetBridge =
        fleetBridge
        ?? throw new ArgumentNullException(nameof(fleetBridge));

    private readonly OperationDispatchPlanningService _dispatchPlanning =
        dispatchPlanning
        ?? throw new ArgumentNullException(nameof(dispatchPlanning));

    public async Task<AcceptedJobDispatchResult> AcceptReserveAndEvaluateAsync(
        JobContractCreationRequest request,
        ContractDispatchContext context,
        OperationDispatchRequirements requirements,
        Guid? selectedProviderAircraftInstanceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirements);

        request.Validate();
        requirements.Validate();

        if (requirements.PayloadPounds + 1e-9
            < request.PayloadPounds)
        {
            throw new ArgumentException(
                "Dispatch payload cannot be lower than the accepted job payload.",
                nameof(requirements));
        }

        if (requirements.RequiredRangeNauticalMiles + 1e-9
            < request.Offer.DistanceNm)
        {
            throw new ArgumentException(
                "Dispatch range cannot be lower than the accepted job route distance.",
                nameof(requirements));
        }

        JobAcceptanceFleetResult fleetResult =
            await _fleetBridge
                .AcceptAndReserveAsync(
                    request,
                    context,
                    selectedProviderAircraftInstanceId,
                    cancellationToken)
                .ConfigureAwait(false);

        if (fleetResult.Status is not
            (JobAcceptanceFleetStatus.AcceptedAndReserved
            or JobAcceptanceFleetStatus.AcceptedAndReservationReused))
        {
            return new(
                fleetResult,
                DispatchResult: null);
        }

        PersistedJobContract accepted =
            fleetResult.AcceptedContract
            ?? throw new InvalidOperationException(
                "Successful Fleet association did not retain the authoritative accepted contract.");

        DispatchFeasibilityResult dispatchResult =
            selectedProviderAircraftInstanceId is not null
                ? await _dispatchPlanning
                    .EvaluateRegisteredAircraftAsync(
                        context.Aircraft.AircraftId,
                        accepted.Contract.OriginIcao,
                        accepted.Contract.DestinationIcao,
                        requirements,
                        fleetResult.ReservationId,
                        cancellationToken)
                    .ConfigureAwait(false)
                : await _dispatchPlanning
                    .EvaluateAsync(
                        context.Aircraft.AircraftId,
                        accepted.Contract.OriginIcao,
                        accepted.Contract.DestinationIcao,
                        requirements,
                        fleetResult.ReservationId,
                        cancellationToken)
                    .ConfigureAwait(false);

        return new(
            fleetResult,
            dispatchResult);
    }
}
