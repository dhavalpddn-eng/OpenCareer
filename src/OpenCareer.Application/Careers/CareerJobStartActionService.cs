namespace OpenCareer.Application.Careers;

public sealed record CareerJobStartActionAvailability(
    bool CanStart,
    CareerJobStartInputState State,
    string Detail);

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
}

public sealed class CareerJobStartActionService
    : ICareerJobStartAction
{
    private readonly CareerJobStartInputSource _inputs;
    private readonly CareerJobPlayableLoopCoordinator _playableLoop;

    public CareerJobStartActionService(
        CareerJobStartInputSource inputs,
        CareerJobPlayableLoopCoordinator playableLoop)
    {
        _inputs =
            inputs
            ?? throw new ArgumentNullException(nameof(inputs));
        _playableLoop =
            playableLoop
            ?? throw new ArgumentNullException(nameof(playableLoop));
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

        return new(
            snapshot.IsReady,
            snapshot.State,
            snapshot.Detail);
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

        return await _playableLoop
            .AcceptAndStartAsync(
                snapshot.Request,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
