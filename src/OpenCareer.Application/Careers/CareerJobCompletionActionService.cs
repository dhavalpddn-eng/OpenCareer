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

        if (!snapshot.IsReady
            && await IsMatchingTerminalRecoveryPendingAsync(
                    snapshot,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return new(
                CanComplete: true,
                CareerJobCompletionInputState.TerminalRecoveryPending,
                "Settlement or finalization recovery is pending for this completed flight. Retry Complete Career Flight; exactly-once settlement prevents duplicate pay.",
                snapshot.FlightSessionId,
                snapshot.ContractId);
        }

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

        if (snapshot.FlightSessionId != expectedFlightSessionId
            || snapshot.ContractId != expectedContractId)
        {
            throw new InvalidOperationException(
                "Career completion inputs changed after readiness was reviewed. Refresh Current Flight and try again.");
        }

        CareerJobPlayableCompletionResult result;
        if (snapshot.IsReady
            && snapshot.Request is not null)
        {
            result =
                await _playableLoop
                    .CompleteAsync(
                        snapshot.Request,
                        cancellationToken)
                    .ConfigureAwait(false);
        }
        else if (await IsMatchingTerminalRecoveryPendingAsync(
                    snapshot,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            result =
                await _playableLoop
                    .ResumePendingAsync(
                        expectedContractId,
                        cancellationToken)
                    .ConfigureAwait(false);
        }
        else
        {
            throw new InvalidOperationException(
                snapshot.Detail);
        }

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

    private async Task<bool> IsMatchingTerminalRecoveryPendingAsync(
        CareerJobCompletionInputSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.ContractId is not { } contractId
            || snapshot.FlightSessionId is null)
        {
            return false;
        }

        Guid? pendingContractId =
            await _playableLoop
                .ReadPendingTerminalContractIdAsync(cancellationToken)
                .ConfigureAwait(false);

        return pendingContractId == contractId;
    }
}
