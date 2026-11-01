using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Economy;

public interface IEconomicCycleStateStore
{
    Task SaveAsync(
        EconomicCycleState state,
        CancellationToken cancellationToken = default);

    Task<EconomicCycleState?> GetAsync(
        string regionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EconomicCycleState>> LoadAllAsync(
        CancellationToken cancellationToken = default);
}
