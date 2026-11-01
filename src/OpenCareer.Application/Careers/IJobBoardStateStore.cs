using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public interface IJobBoardStateStore
{
    Task SaveAsync(
        JobBoardState state,
        CancellationToken cancellationToken = default);

    Task<JobBoardState?> GetAsync(
        string airportIcao,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobBoardState>> LoadAllAsync(
        CancellationToken cancellationToken = default);
}
