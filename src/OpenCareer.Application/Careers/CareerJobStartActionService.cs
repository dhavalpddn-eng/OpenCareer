using OpenCareer.Application.Planning;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public enum CareerJobActionKind
{
    StartPreparedFlight = 0,
    AcceptForDispatch,
    AcceptAndStartDevelopmentFlight
}

public sealed record CareerJobStartActionAvailability(
    bool CanStart,
    CareerJobStartInputState State,
    string Detail,
    CareerJobActionKind ActionKind = CareerJobActionKind.StartPreparedFlight);

public interface ICareerJobStartAction
{
    Task<CareerJobStartActionAvailability> ReadAvailabilityAsync(
        Guid offerId,
        string aircraftId,
        CancellationToken cancellationToken = default);

    Task<CareerJobPlayableStartResult> StartAsync(
        Guid offerId,
        string aircraftId,
        CancellationToken cancellationToken = default);

    Task<JobAcceptanceFleetResult> AcceptAsync(
        Guid offerId,
        string aircraftId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This start action does not expose separate job acceptance.");
}

public sealed class CareerJobStartActionService
    : ICareerJobStartAction
{
    private readonly CareerJobStartInputSource _inputs;
    private readonly CareerJobPlayableLoopCoordinator _playableLoop;
    private readonly JobAcceptanceFleetBridge? _acceptance;
    private readonly IAutomaticDispatchPlanService? _automaticDispatch;
    private readonly IAcceptedJobCancellationService? _cancellation;

    public CareerJobStartActionService(
        CareerJobStartInputSource inputs,
        CareerJobPlayableLoopCoordinator playableLoop,
        JobAcceptanceFleetBridge? acceptance = null,
        IAutomaticDispatchPlanService? automaticDispatch = null,
        IAcceptedJobCancellationService? cancellation = null)
    {
        _inputs =
            inputs
            ?? throw new ArgumentNullException(nameof(inputs));
        _playableLoop =
            playableLoop
            ?? throw new ArgumentNullException(nameof(playableLoop));
        _acceptance = acceptance;
        _automaticDispatch = automaticDispatch;
        _cancellation = cancellation;
    }

    public async Task<CareerJobStartActionAvailability> ReadAvailabilityAsync(
        Guid offerId,
        string aircraftId,
        CancellationToken cancellationToken = default)
    {
        CareerJobStartInputSnapshot snapshot =
            await _inputs
                .ReadAsync(
                    offerId,
                    aircraftId,
                    cancellationToken)
                .ConfigureAwait(false);

        CareerJobActionKind actionKind = snapshot.IsAcceptedContract
            || snapshot.State == CareerJobStartInputState.PreparedDispatchUnavailable
                ? CareerJobActionKind.StartPreparedFlight
                : snapshot.Request?.Contract.MarketId == DevelopmentFlight.MarketId
                    ? CareerJobActionKind.AcceptAndStartDevelopmentFlight
                    : CareerJobActionKind.AcceptForDispatch;

        return new(
            snapshot.IsReady,
            snapshot.State,
            snapshot.Detail,
            actionKind);
    }

    public async Task<JobAcceptanceFleetResult> AcceptAsync(
        Guid offerId,
        string aircraftId,
        CancellationToken cancellationToken = default)
    {
        CareerJobStartInputSnapshot snapshot = await _inputs
            .ReadAsync(offerId, aircraftId, cancellationToken)
            .ConfigureAwait(false);

        if (!snapshot.IsReady
            || snapshot.Request is not { } request)
        {
            throw new InvalidOperationException(snapshot.Detail);
        }

        if (snapshot.IsAcceptedContract)
        {
            throw new InvalidOperationException(
                "The job is already accepted and must use prepared start authority.");
        }

        if (_acceptance is not { } acceptance)
        {
            throw new InvalidOperationException(
                "Separate job acceptance is unavailable.");
        }

        if (_automaticDispatch is null || _cancellation is null)
        {
            throw new InvalidOperationException(
                "Automatic job preflight is unavailable.");
        }

        JobAcceptanceFleetResult accepted = await acceptance
            .AcceptAndReserveAsync(
                request.Contract,
                request.DispatchContext,
                cancellationToken)
            .ConfigureAwait(false);

        if (accepted.AcceptedContract is not { } persisted)
        {
            throw new InvalidOperationException(
                $"The job was not accepted and reserved: {accepted.Status}.");
        }

        IAutomaticDispatchPlanService automaticDispatch = _automaticDispatch;
        IAcceptedJobCancellationService cancellation = _cancellation;

        try
        {
            if (accepted.CanonicalAircraftId is null)
            {
                throw new InvalidOperationException(
                    "The accepted job reservation has no canonical aircraft identity.");
            }

            JobContract contract = persisted.Contract;
            JobContractPlanningTerms terms = contract.PlanningTerms
                ?? throw new InvalidOperationException(
                    "The accepted job has no authoritative planning terms.");

            PrepareDispatchPlanResult prepared = await automaticDispatch
                .PrepareAsync(
                    contract.ContractId,
                    accepted.CanonicalAircraftId,
                    accepted.ReservationId,
                    contract.OriginIcao,
                    contract.DestinationIcao,
                    terms.PayloadPounds,
                    terms.RouteDistanceNauticalMiles,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!prepared.IsPrepared)
            {
                throw new InvalidOperationException(
                    $"Automatic job preflight failed: {prepared.Status}. {prepared.Detail}");
            }
        }
        catch (Exception preparationException)
        {
            await CancelFailedPreparationAsync(
                    cancellation,
                    persisted.Contract.ContractId,
                    preparationException)
                .ConfigureAwait(false);
            throw;
        }

        return accepted;
    }

    private static async Task CancelFailedPreparationAsync(
        IAcceptedJobCancellationService cancellation,
        Guid contractId,
        Exception preparationException)
    {
        try
        {
            await cancellation
                .CancelAsync(contractId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception cleanupException)
        {
            throw new InvalidOperationException(
                "Automatic preflight failed and the accepted job could not be safely cancelled.",
                new AggregateException(preparationException, cleanupException));
        }
    }

    public async Task<CareerJobPlayableStartResult> StartAsync(
        Guid offerId,
        string aircraftId,
        CancellationToken cancellationToken = default)
    {
        CareerJobStartInputSnapshot snapshot =
            await _inputs
                .ReadAsync(
                    offerId,
                    aircraftId,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!snapshot.IsReady
            || snapshot.Request is null)
        {
            throw new InvalidOperationException(
                snapshot.Detail);
        }

        if (!snapshot.IsAcceptedContract
            && snapshot.Request.Contract.MarketId != DevelopmentFlight.MarketId)
        {
            throw new InvalidOperationException(
                "A first-time ordinary offer must be accepted and prepared before flight start.");
        }

        return await _playableLoop
            .AcceptAndStartAsync(
                snapshot.Request,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
