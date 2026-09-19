namespace OpenCareer.Application.Dashboard;

public sealed class DashboardGuidanceEngine
{
    public DashboardGuidanceCandidate? SelectPrimary(
        IEnumerable<DashboardGuidanceCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(static candidate => candidate.IsActionable)
            .OrderByDescending(static candidate => candidate.Priority)
            .ThenBy(static candidate => candidate.OrderWithinPriority)
            .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
