namespace OpenCareer.Application.Dashboard;

public interface IDashboardSnapshotSource
{
    Task<DashboardSnapshot> GetAsync(CancellationToken cancellationToken = default);
}
