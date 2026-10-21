using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed record CareerJobCompletionActionAvailability(
    bool CanComplete,
    CareerJobCompletionInputState State,
    string Detail);

public sealed record CareerJobCompletionReceipt(
    Guid ContractId,
    decimal NetCashChange,
    decimal CashBalanceAfter,
    string OriginIcao,
    string DestinationIcao,
    Guid LogbookEntryId,
    string CurrentAirportIcao,
    int CareerFlightCount,
    bool IsZeroPayCalibration);

public interface ICareerJobCompletionAction
{
    Task<CareerJobCompletionActionAvailability> ReadAvailabilityAsync(
        CancellationToken cancellationToken = default);

    Task<CareerJobCompletionReceipt> CompleteAsync(
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

    public async Task<CareerJobCompletionReceipt> CompleteAsync(
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

        CareerJobPlayableCompletionResult result =
            await _playableLoop.CompleteAsync(
                snapshot.Request,
                cancellationToken)
            .ConfigureAwait(false);

        return new(
            result.CompletedContract.Contract.ContractId,
            result.Terminal.Settlement.Settlement.NetCashChange,
            result.Terminal.Settlement.CashBalanceAfter,
            result.CompletedContract.Contract.OriginIcao,
            result.CompletedContract.Contract.DestinationIcao,
            result.Terminal.Logbook.Entry.EntryId,
            result.Terminal.CareerProfile.Profile.Location.CurrentAirportIcao,
            result.Terminal.CareerProfile.Profile.Experience.FlightCount,
            DevelopmentFlight.IsDevelopment(
                result.CompletedContract.Contract));
    }
}
