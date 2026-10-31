using OpenCareer.Domain.Simulation;

namespace OpenCareer.Application.Economy;

public interface IWorldSimulationStateStore
{
    Task SaveAsync(
        WorldSimulationState state,
        CancellationToken cancellationToken = default);

    Task<WorldSimulationState?> GetAsync(
        string marketId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorldSimulationState>> LoadAllAsync(
        CancellationToken cancellationToken = default);
}
