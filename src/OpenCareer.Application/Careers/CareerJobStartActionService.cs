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

    public CareerJobStartActionService(
        CareerJobStartInputSource inputs,
        CareerJobPlayableLoopCoordinator playableLoop,
        JobAcceptanceFleetBridge? acceptance = null)
    {
        _inputs =
            inputs
            ?? throw new ArgumentNullException(nameof(inputs));
        _playableLoop =
            playableLoop
            ?? throw new ArgumentNullException(nameof(playableLoop));
        _acceptance = acceptance;
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

        return await acceptance
            .AcceptAndReserveAsync(
                request.Contract,
                request.DispatchContext,
                cancellationToken)
            .ConfigureAwait(false);
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
