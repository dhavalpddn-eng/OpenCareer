namespace OpenCareer.Application.Careers;

public sealed record CareerJobCompletionActionAvailability(
    bool CanComplete,
    CareerJobCompletionInputState State,
    string Detail);

public interface ICareerJobCompletionAction
{
    Task<CareerJobCompletionActionAvailability> ReadAvailabilityAsync(
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        CancellationToken cancellationToken = default);
}

public sealed class CareerJobCompletionActionService
    : ICareerJobCompletionAction
{
    private readonly CareerJobCompletionInputSource _inputSource;
    private readonly CareerJobPlayableLoopCoordinator _playableLoop;

    public CareerJobCompletionActionService(
        CareerJobCompletionInputSource inputSource,
        CareerJobPlayableLoopCoordinator playableLoop)
    {
        _inputSource =
            inputSource
            ?? throw new ArgumentNullException(nameof(inputSource));
        _playableLoop =
            playableLoop
            ?? throw new ArgumentNullException(nameof(playableLoop));
    }

    public async Task<CareerJobCompletionActionAvailability> ReadAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        CareerJobCompletionInputSnapshot snapshot =
            await _inputSource
                .ReadCurrentAsync(cancellationToken)
                .ConfigureAwait(false);

        return new(
            snapshot.IsReady,
            snapshot.State,
            snapshot.Detail);
    }

    public async Task CompleteAsync(
        CancellationToken cancellationToken = default)
    {
        CareerJobCompletionInputSnapshot snapshot =
            await _inputSource
                .ReadCurrentAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!snapshot.IsReady
            || snapshot.Request is null)
        {
            throw new InvalidOperationException(
                snapshot.Detail);
        }

        await _playableLoop
            .CompleteAsync(
                snapshot.Request,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
