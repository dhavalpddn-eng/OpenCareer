using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public interface IAircraftPurchaseLedgerStore : IEconomyLedgerStore
{
    Task<LedgerPostResult> PostAircraftPurchaseAsync(
        AircraftPurchaseSettlement settlement,
        CancellationToken cancellationToken = default);

    Task<AircraftPurchaseSettlement?> ReadAircraftPurchaseAsync(
        string ownershipId,
        CancellationToken cancellationToken = default);
}
