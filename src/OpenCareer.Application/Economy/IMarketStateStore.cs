using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public interface IMarketStateStore
{
    Task SaveAsync(
        MarketState state,
        CancellationToken cancellationToken = default);

    Task<MarketState?> GetAsync(
        string marketId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketState>> LoadAllAsync(
        CancellationToken cancellationToken = default);
}
