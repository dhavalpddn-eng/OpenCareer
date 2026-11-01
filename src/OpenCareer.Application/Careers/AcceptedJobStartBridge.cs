using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Careers;

public sealed class AcceptedJobStartBridge(
    JobContractLifecycleService lifecycle)
{
    private readonly JobContractLifecycleService _lifecycle =
        lifecycle
        ?? throw new ArgumentNullException(nameof(lifecycle));

    public async Task<PersistedJobContract> StartAsync(
        AcceptedJobDispatchResult acceptedDispatch,
        ContractDispatchContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acceptedDispatch);
        ArgumentNullException.ThrowIfNull(context);

        JobAcceptanceFleetResult fleet =
            acceptedDispatch.FleetResult
            ?? throw new InvalidOperationException(
                "Accepted-job dispatch result is missing Fleet association.");

        if (fleet.Status is not
            (JobAcceptanceFleetStatus.AcceptedAndReserved
            or JobAcceptanceFleetStatus.AcceptedAndReservationReused))
        {
            throw new InvalidOperationException(
                "A job cannot start without an authoritative aircraft reservation.");
        }

        PersistedJobContract accepted =
            fleet.AcceptedContract
            ?? throw new InvalidOperationException(
                "A job cannot start without the authoritative accepted contract.");

        accepted.Validate();

        if (accepted.Contract.Status
            != ContractStatus.Accepted)
        {
            throw new InvalidOperationException(
                "Only an accepted job contract can enter InProgress.");
        }

        string expectedReservationId =
            JobAcceptanceFleetBridge.GetReservationId(
                accepted.Contract.ContractId);

        if (!string.Equals(
                fleet.ReservationId,
                expectedReservationId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The retained aircraft reservation does not belong to the accepted contract.");
        }

        if (string.IsNullOrWhiteSpace(
                fleet.CanonicalAircraftId))
        {
            throw new InvalidOperationException(
                "The retained aircraft reservation is missing canonical aircraft identity.");
        }

        DispatchFeasibilityResult dispatch =
            acceptedDispatch.DispatchResult
            ?? throw new InvalidOperationException(
                "A job cannot start without an authoritative Dispatch evaluation.");

        if (dispatch.Status
            != DispatchFeasibilityStatus.Feasible)
        {
            throw new InvalidOperationException(
                $"Dispatch does not permit contract start: {dispatch.Status}.");
        }

        ContractDispatchContext authoritativeContext =
            context with
            {
                DispatchFeasibilityVerified = true
            };

        return await _lifecycle
            .StartAsync(
                accepted.Contract.ContractId,
                authoritativeContext,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
