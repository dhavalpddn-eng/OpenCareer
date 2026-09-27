using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public interface ICommodityMarketSnapshotStore
{
    Task SaveAsync(
        CommodityMarketSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<CommodityMarketSnapshot?> GetAsync(
        CommodityMarketScope scope,
        string locationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommodityMarketSnapshot>> LoadAllAsync(
        CancellationToken cancellationToken = default);
}
