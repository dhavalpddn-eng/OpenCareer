using OpenCareer.Domain.Finance;

namespace OpenCareer.Application.Economy;

public interface IAircraftAcquisitionStore
    : IActivePlayBillingLedgerStore
{
    Task<LedgerPostResult> AcquireAircraftAsync(
        AircraftAcquisitionSettlement settlement,
        CancellationToken cancellationToken = default);

    Task<AircraftOwnershipRecord?> ReadAircraftOwnershipAsync(
        string ownershipId,
        CancellationToken cancellationToken = default);

    Task<AircraftLoanAgreement?> ReadAircraftLoanAsync(
        Guid loanId,
        CancellationToken cancellationToken = default);
}

public sealed record AircraftAcquisitionResult(
    AircraftAcquisitionSettlement Settlement,
    LedgerPostResult PostResult,
    decimal CashBalanceAfter)
{
    public bool WasNewlyAcquired =>
        PostResult == LedgerPostResult.Posted;
}

public sealed class AircraftAcquisitionService(
    IAircraftAcquisitionStore store)
{
    private readonly IAircraftAcquisitionStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    public async Task<AircraftAcquisitionResult> AcquireAsync(
        AircraftAcquisitionSettlement settlement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        settlement.Validate();

        LedgerPostResult result =
            await _store
                .AcquireAircraftAsync(
                    settlement,
                    cancellationToken)
                .ConfigureAwait(false);

        decimal balance =
            await _store
                .ReadCashBalanceAsync(cancellationToken)
                .ConfigureAwait(false);

        return new AircraftAcquisitionResult(
            settlement,
            result,
            balance);
    }
}
