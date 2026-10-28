using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed record CareerJobCompletionActionAvailability(
    bool CanComplete,
    CareerJobCompletionInputState State,
    string Detail,
    Guid? FlightSessionId = null,
    Guid? ContractId = null);

public sealed record CareerJobCompletionReceipt(
    Guid ContractId,
    decimal GrossCashReceipt,
    decimal PlayerOperatingCosts,
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
        Guid expectedFlightSessionId,
        Guid expectedContractId,
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
            snapshot.Detail,
            snapshot.IsReady ? snapshot.FlightSessionId : null,
            snapshot.IsReady ? snapshot.ContractId : null);
    }

    public async Task<CareerJobCompletionReceipt> CompleteAsync(
        Guid expectedFlightSessionId,
        Guid expectedContractId,
        CancellationToken cancellationToken = default)
    {
        if (expectedFlightSessionId == Guid.Empty)
            throw new ArgumentException("Expected FlightSession ID is required.", nameof(expectedFlightSessionId));

        if (expectedContractId == Guid.Empty)
            throw new ArgumentException("Expected contract ID is required.", nameof(expectedContractId));

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

        if (snapshot.FlightSessionId != expectedFlightSessionId
            || snapshot.ContractId != expectedContractId)
        {
            throw new InvalidOperationException(
                "Career completion inputs changed after readiness was reviewed. Refresh Current Flight and try again.");
        }

        CareerJobPlayableCompletionResult result =
            await _playableLoop.CompleteAsync(
                snapshot.Request,
                cancellationToken)
            .ConfigureAwait(false);

        return new(
            result.CompletedContract.Contract.ContractId,
            result.Terminal.Settlement.Settlement.GrossCashReceipt,
            result.Terminal.Settlement.Settlement.PlayerOperatingCosts,
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
