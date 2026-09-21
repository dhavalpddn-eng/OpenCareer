using OpenCareer.Application.Dashboard;

namespace OpenCareer.App.Services;

public sealed class UnavailableDashboardSnapshotSource : IDashboardSnapshotSource
{
    public Task<DashboardSnapshot> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DashboardSnapshot.Empty);
}
